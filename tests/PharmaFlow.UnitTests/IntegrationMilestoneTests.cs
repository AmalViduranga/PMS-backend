using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Identity.Api.Controllers;
using PharmaFlow.Identity.Api.Data;
using PharmaFlow.Identity.Api.Domain;
using PharmaFlow.Identity.Api.DTOs;
using PharmaFlow.Identity.Api.Services;
using PharmaFlow.Operations.Api.Controllers;
using PharmaFlow.Operations.Api.Data;
using PharmaFlow.Operations.Api.Domain;
using PharmaFlow.Operations.Api.DTOs;
using PharmaFlow.Operations.Api.Services;
using Xunit;

namespace PharmaFlow.UnitTests;

public class IntegrationMilestoneTests
{
    private OperationsDbContext CreateOperationsDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OperationsDbContext(options);
    }

    private IdentityDbContext CreateIdentityDb(string dbName)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new IdentityDbContext(options);
    }

    // -------------------------------------------------------------
    // 1. Goods Receipt: Prevent Duplicate Posting
    // -------------------------------------------------------------
    [Fact]
    public async Task GoodsReceipt_DuplicateInvoiceSubmission_IsRejected()
    {
        var db = CreateOperationsDb(nameof(GoodsReceipt_DuplicateInvoiceSubmission_IsRejected));
        var branchId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var poId = Guid.NewGuid();
        var poItemId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var po = new PurchaseOrder
        {
            Id = poId,
            PoNumber = "PO-20261014-001",
            SupplierId = supplierId,
            BranchId = branchId,
            Status = "Approved",
            TotalCost = 500,
            Items = new List<PurchaseOrderItem>
            {
                new()
                {
                    Id = poItemId,
                    ProductId = productId,
                    OrderedQuantity = 10,
                    UnitCost = 50,
                    ReceivedQuantity = 0
                }
            }
        };

        var existingGrn = new GoodsReceipt
        {
            Id = Guid.NewGuid(),
            GrnNumber = "GRN-001",
            PurchaseOrderId = poId,
            SupplierInvoiceNumber = "INV-SUP-991",
            ReceivedAtUtc = DateTime.UtcNow,
            ReceivedByUserId = userId,
            TotalAcquisitionCost = 500
        };

        db.PurchaseOrders.Add(po);
        db.GoodsReceipts.Add(existingGrn);
        await db.SaveChangesAsync();

        var controller = new PurchasingController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimConstants.UserId, userId.ToString())
                }, "TestAuth"))
            }
        };
        var req = new ReceiveGoodsReceiptRequest(
            "INV-SUP-991", // Duplicate supplier invoice number
            new List<ReceiveGrnItemInput>
            {
                new(poItemId, productId, "B-001", DateTime.UtcNow.AddMonths(6), 5, 0, 1, 50)
            },
            "Duplicate attempt"
        );

        var result = await controller.ReceiveGoods(poId, req);
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var apiRes = Assert.IsAssignableFrom<ApiResponse<string>>(badRequest.Value);
        Assert.False(apiRes.Success);
        Assert.Contains("already been processed", apiRes.Message);
    }

    // -------------------------------------------------------------
    // 2. Concurrency / Stock Integrity: Attempt to Sell Final Unit
    // -------------------------------------------------------------
    [Fact]
    public async Task AllocateFefo_WhenFinalUnitExhausted_ThrowsInsufficientStock()
    {
        var db = CreateOperationsDb(nameof(AllocateFefo_WhenFinalUnitExhausted_ThrowsInsufficientStock));
        var branchId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var batch = new StockBatch
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ProductId = productId,
            BatchNumber = "BATCH-FINAL",
            ExpiryDate = DateTime.UtcNow.Date.AddDays(30),
            QuantityOnHand = 1, // Only 1 unit left!
            QuantityReserved = 0,
            Status = "Active"
        };
        db.StockBatches.Add(batch);
        await db.SaveChangesAsync();

        var inventory = new InventoryService(db);

        // First allocation takes the 1 available unit
        var firstAlloc = await inventory.AllocateFefoAsync(branchId, productId, 1);
        Assert.Single(firstAlloc);
        Assert.Equal(1, firstAlloc[0].AllocatedQuantity);
        Assert.Equal(0, batch.QuantityOnHand);

        // Immediate subsequent allocation attempt throws exception
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inventory.AllocateFefoAsync(branchId, productId, 1));
        Assert.Contains("Insufficient saleable stock", ex.Message);
    }

    // -------------------------------------------------------------
    // 3. POS Retries: Valid Idempotency Key Returns Identical Result
    // -------------------------------------------------------------
    [Fact]
    public async Task PosCheckout_ValidIdempotencyRetry_ReturnsExistingSaleWithoutDuplication()
    {
        var db = CreateOperationsDb(nameof(PosCheckout_ValidIdempotencyRetry_ReturnsExistingSaleWithoutDuplication));
        var branchId = Guid.NewGuid();
        var registerId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var cashierId = Guid.NewGuid();

        var shift = new CashierShift
        {
            Id = shiftId,
            BranchId = branchId,
            RegisterId = registerId,
            CashierUserId = cashierId,
            Status = "Open",
            OpeningFloat = 5000
        };

        var product = new Product
        {
            Id = productId,
            Sku = "MED-01",
            BrandName = "Test Med",
            GenericName = "Generic",
            SellingPrice = 100,
            IsActive = true
        };

        var batch = new StockBatch
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ProductId = productId,
            BatchNumber = "B-01",
            ExpiryDate = DateTime.UtcNow.Date.AddYears(1),
            QuantityOnHand = 20,
            QuantityReserved = 0,
            Status = "Active"
        };

        db.CashierShifts.Add(shift);
        db.Products.Add(product);
        db.StockBatches.Add(batch);
        await db.SaveChangesAsync();

        var inventory = new InventoryService(db);
        var pos = new PosService(db, inventory);

        var req = new CheckoutSaleRequest(
            branchId,
            registerId,
            shiftId,
            "Customer A",
            new List<CheckoutLineItemRequest>
            {
                new(productId, 2, null, 100, 0)
            },
            200,
            "Cash",
            "Initial sale",
            "IDEMP-KEY-999"
        );

        // 1. Initial checkout
        var result1 = await pos.CheckoutAsync(req, cashierId);
        Assert.True(result1.Success);
        var originalInvoice = result1.Data!.InvoiceNumber;
        Assert.Equal(18, batch.QuantityOnHand); // 20 - 2

        // 2. Retry with same idempotency key and same payload
        var result2 = await pos.CheckoutAsync(req, cashierId);
        Assert.True(result2.Success);
        Assert.Equal(originalInvoice, result2.Data!.InvoiceNumber);
        Assert.Contains("idempotent", result2.Message);

        // Assert stock was NOT deducted twice
        Assert.Equal(18, batch.QuantityOnHand);
        // Assert only 1 sale in database
        Assert.Equal(1, await db.Sales.CountAsync());
    }

    // -------------------------------------------------------------
    // 4. POS Idempotency: Payload Mismatch Rejected
    // -------------------------------------------------------------
    [Fact]
    public async Task PosCheckout_IdempotencyKeyReuseWithDifferentPayload_IsRejected()
    {
        var db = CreateOperationsDb(nameof(PosCheckout_IdempotencyKeyReuseWithDifferentPayload_IsRejected));
        var branchId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var productId1 = Guid.NewGuid();
        var productId2 = Guid.NewGuid();
        var cashierId = Guid.NewGuid();

        var shift = new CashierShift
        {
            Id = shiftId,
            BranchId = branchId,
            RegisterId = Guid.NewGuid(),
            CashierUserId = cashierId,
            Status = "Open"
        };

        var product1 = new Product { Id = productId1, Sku = "P1", BrandName = "Med1", SellingPrice = 50, IsActive = true };
        var product2 = new Product { Id = productId2, Sku = "P2", BrandName = "Med2", SellingPrice = 80, IsActive = true };

        var batch1 = new StockBatch { BranchId = branchId, ProductId = productId1, BatchNumber = "B1", ExpiryDate = DateTime.UtcNow.AddYears(1), QuantityOnHand = 10, Status = "Active" };
        var batch2 = new StockBatch { BranchId = branchId, ProductId = productId2, BatchNumber = "B2", ExpiryDate = DateTime.UtcNow.AddYears(1), QuantityOnHand = 10, Status = "Active" };

        db.CashierShifts.Add(shift);
        db.Products.AddRange(product1, product2);
        db.StockBatches.AddRange(batch1, batch2);
        await db.SaveChangesAsync();

        var pos = new PosService(db, new InventoryService(db));

        var req1 = new CheckoutSaleRequest(branchId, shift.RegisterId, shiftId, "Customer 1", new List<CheckoutLineItemRequest> { new(productId1, 1, null, 50, 0) }, 50, "Cash", null, "IDEMP-REUSE");
        var res1 = await pos.CheckoutAsync(req1, cashierId);
        Assert.True(res1.Success);

        // Submitting with SAME key but different product item!
        var req2 = new CheckoutSaleRequest(branchId, shift.RegisterId, shiftId, "Customer 1", new List<CheckoutLineItemRequest> { new(productId2, 1, null, 80, 0) }, 80, "Cash", null, "IDEMP-REUSE");
        var res2 = await pos.CheckoutAsync(req2, cashierId);

        Assert.False(res2.Success);
        Assert.Contains("different request payload", res2.Message);
    }

    // -------------------------------------------------------------
    // 5. Prescription Gating: Rx Items Without Verification Blocked
    // -------------------------------------------------------------
    [Fact]
    public async Task PosCheckout_PrescriptionRequiredItem_WithoutVerification_IsBlocked()
    {
        var db = CreateOperationsDb(nameof(PosCheckout_PrescriptionRequiredItem_WithoutVerification_IsBlocked));
        var branchId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var shift = new CashierShift { Id = shiftId, BranchId = branchId, RegisterId = Guid.NewGuid(), Status = "Open" };
        var product = new Product { Id = productId, Sku = "RX-01", BrandName = "Amoxicillin", SellingPrice = 45, IsActive = true, IsPrescriptionRequired = true };

        db.CashierShifts.Add(shift);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var pos = new PosService(db, new InventoryService(db));

        // Checkout without prescription verification
        var req = new CheckoutSaleRequest(
            branchId,
            shift.RegisterId,
            shiftId,
            "Patient",
            new List<CheckoutLineItemRequest>
            {
                new(productId, 1, null, 45, 0, PrescriptionNumber: null, PrescriptionVerified: false)
            },
            45,
            "Cash",
            null,
            "RX-KEY"
        );

        var result = await pos.CheckoutAsync(req, Guid.NewGuid());
        Assert.False(result.Success);
        Assert.Contains("requires a valid prescription", result.Message);
    }

    // -------------------------------------------------------------
    // 6. Returns & Quarantine: Route to Quarantine & Retries
    // -------------------------------------------------------------
    [Fact]
    public async Task CustomerReturn_RoutesToQuarantine_AndRespectsIdempotency()
    {
        var db = CreateOperationsDb(nameof(CustomerReturn_RoutesToQuarantine_AndRespectsIdempotency));
        var branchId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var saleItemId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var cashierId = Guid.NewGuid();

        var shift = new CashierShift { Id = shiftId, BranchId = branchId, RegisterId = Guid.NewGuid(), CashierUserId = cashierId, Status = "Open", CashRefundsTotal = 0 };
        var saleItem = new SaleItem { Id = saleItemId, SaleId = saleId, ProductId = productId, BatchNumber = "B-ORIG", Quantity = 4, ReturnedQuantity = 0, UnitPrice = 100, LineTotal = 400 };
        var sale = new Sale { Id = saleId, InvoiceNumber = "INV-RET-TEST", BranchId = branchId, ShiftId = shiftId, RegisterId = shift.RegisterId, CashierUserId = cashierId, GrandTotal = 400, Items = new List<SaleItem> { saleItem } };

        db.CashierShifts.Add(shift);
        db.Sales.Add(sale);
        await db.SaveChangesAsync();

        var returnService = new ReturnService(db);

        var req = new ProcessReturnRequest(
            saleId,
            shiftId,
            "Cash",
            "Damaged package",
            new List<ReturnItemLineInput> { new(saleItemId, 2) },
            "RET-IDEMP-01"
        );

        // 1. First return attempt
        var res1 = await returnService.ProcessReturnAsync(req, cashierId);
        Assert.True(res1.Success);
        Assert.Equal(200, res1.Data!.RefundTotal);

        // Verify quarantined batch created
        var quarantineBatch = await db.StockBatches.FirstOrDefaultAsync(b => b.ProductId == productId && b.Status == "Quarantined");
        Assert.NotNull(quarantineBatch);
        Assert.Equal(2, quarantineBatch.QuantityOnHand);

        // Verify shift cash refund recorded
        Assert.Equal(200, shift.CashRefundsTotal);

        // 2. Retry with same idempotency key
        var res2 = await returnService.ProcessReturnAsync(req, cashierId);
        Assert.True(res2.Success);
        Assert.Contains("idempotent", res2.Message);
        Assert.Equal(res1.Data.ReturnNumber, res2.Data!.ReturnNumber);

        // Assert quarantine stock was not added twice
        Assert.Equal(1, await db.StockBatches.CountAsync(b => b.ProductId == productId && b.Status == "Quarantined"));
        // Assert only 1 customer return record in DB
        Assert.Equal(1, await db.CustomerReturns.CountAsync());
    }

    // -------------------------------------------------------------
    // 7. Staff Security: Deny Customer Accounts Staff Access
    // -------------------------------------------------------------
    [Fact]
    public async Task StaffLogin_CustomerAccount_IsDeniedStaffAccess()
    {
        var db = CreateIdentityDb(nameof(StaffLogin_CustomerAccount_IsDeniedStaffAccess));
        var passwordHasher = new PasswordHasher();

        var customerUser = new User
        {
            Id = Guid.NewGuid(),
            Email = "customer@storefront.local",
            PasswordHash = passwordHasher.HashPassword("CustomerPass123!"),
            FullName = "Store Customer",
            ActorType = "customer", // Customer actor!
            IsActive = true
        };
        db.Users.Add(customerUser);
        await db.SaveChangesAsync();

        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "Jwt:Secret", "PharmaFlow_Ultra_Secure_Dev_Signing_Key_2026_AtLeast32Bytes!" },
            { "Jwt:Issuer", "PharmaFlow.Identity" },
            { "Jwt:Audience", "PharmaFlow.Services" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var jwtService = new TokenService(config);
        var bootstrapService = new BootstrapService(db, passwordHasher, config, NullLogger<BootstrapService>.Instance);
        var emailSender = new SmtpEmailSender(config, NullLogger<SmtpEmailSender>.Instance);
        var authController = new AuthController(db, passwordHasher, jwtService, bootstrapService, emailSender, NullLogger<AuthController>.Instance);

        var loginReq = new LoginRequest("customer@storefront.local", "CustomerPass123!");
        var result = await authController.Login(loginReq);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var apiRes = Assert.IsAssignableFrom<ApiResponse<LoginResponse>>(unauthorized.Value);
        Assert.False(apiRes.Success);
        Assert.Contains("Customer accounts cannot access the staff", apiRes.Message);
    }

    // -------------------------------------------------------------
    // 8. Password Recovery: Expired and Reused Token Rejected
    // -------------------------------------------------------------
    [Fact]
    public async Task PasswordReset_ExpiredAndReusedTokens_AreRejected()
    {
        var db = CreateIdentityDb(nameof(PasswordReset_ExpiredAndReusedTokens_AreRejected));
        var passwordHasher = new PasswordHasher();
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "Jwt:Secret", "PharmaFlow_Ultra_Secure_Dev_Signing_Key_2026_AtLeast32Bytes!" },
            { "Jwt:Issuer", "PharmaFlow.Identity" },
            { "Jwt:Audience", "PharmaFlow.Services" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemoryConfig).Build();

        var jwtService = new TokenService(config);
        var bootstrapService = new BootstrapService(db, passwordHasher, config, NullLogger<BootstrapService>.Instance);
        var emailSender = new SmtpEmailSender(config, NullLogger<SmtpEmailSender>.Instance);
        var authController = new AuthController(db, passwordHasher, jwtService, bootstrapService, emailSender, NullLogger<AuthController>.Instance);

        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = "pharmacist@pharmaflow.local",
            PasswordHash = passwordHasher.HashPassword("OldValidPass123!"),
            FullName = "Pharmacist",
            ActorType = "staff",
            IsActive = true
        };

        var rawToken = "valid-reset-token-1234567890abcdef";
        var resetToken = new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = passwordHasher.HashPassword(rawToken),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            CreatedAtUtc = DateTime.UtcNow
        };

        var activeRefreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = "ACTIVE-REFRESH-TOKEN",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(7)
        };

        db.Users.Add(user);
        db.PasswordResetTokens.Add(resetToken);
        db.RefreshTokens.Add(activeRefreshToken);
        await db.SaveChangesAsync();

        // 1. Successful reset using valid token
        var resetReq = new ResetPasswordRequest("pharmacist@pharmaflow.local", rawToken, "NewSecurePass123!");
        var resetResult = await authController.ResetPassword(resetReq);

        var okResult = Assert.IsType<OkObjectResult>(resetResult);
        var okRes = Assert.IsAssignableFrom<ApiResponse<bool>>(okResult.Value);
        Assert.True(okRes.Success);

        // Verify password was changed and old password rejected
        var updatedUser = await db.Users.FindAsync(userId);
        Assert.True(passwordHasher.VerifyPassword("NewSecurePass123!", updatedUser!.PasswordHash));
        Assert.False(passwordHasher.VerifyPassword("OldValidPass123!", updatedUser.PasswordHash));

        // Verify token was atomically consumed (single-use)
        var consumedToken = await db.PasswordResetTokens.FindAsync(resetToken.Id);
        Assert.NotNull(consumedToken!.UsedAtUtc);

        // Verify active refresh token was revoked
        var rt = await db.RefreshTokens.FindAsync(activeRefreshToken.Id);
        Assert.NotNull(rt!.RevokedAtUtc);

        // 2. Reuse of same token is rejected
        var reuseResult = await authController.ResetPassword(resetReq);
        var badRequest = Assert.IsType<BadRequestObjectResult>(reuseResult);
        var badRes = Assert.IsAssignableFrom<ApiResponse<bool>>(badRequest.Value);
        Assert.False(badRes.Success);
        Assert.Contains("Invalid or expired", badRes.Message);
    }
}
