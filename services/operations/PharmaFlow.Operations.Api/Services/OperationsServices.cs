using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Contracts.Events;
using PharmaFlow.Operations.Api.Data;
using PharmaFlow.Operations.Api.Domain;
using PharmaFlow.Operations.Api.DTOs;

namespace PharmaFlow.Operations.Api.Services;

public interface IInventoryService
{
    Task<List<(StockBatch Batch, int AllocatedQuantity)>> AllocateFefoAsync(Guid branchId, Guid productId, int quantityNeeded);
    Task AdjustStockAsync(Guid branchId, Guid productId, Guid batchId, string adjustmentType, int quantityDelta, string reason, Guid userId);
}

public class InventoryService : IInventoryService
{
    private readonly OperationsDbContext _db;

    public InventoryService(OperationsDbContext db)
    {
        _db = db;
    }

    public async Task<List<(StockBatch Batch, int AllocatedQuantity)>> AllocateFefoAsync(Guid branchId, Guid productId, int quantityNeeded)
    {
        var now = DateTime.UtcNow.Date;
        var availableBatches = await _db.StockBatches
            .Where(b => b.BranchId == branchId &&
                        b.ProductId == productId &&
                        b.Status == "Active" &&
                        b.ExpiryDate > now &&
                        (b.QuantityOnHand - b.QuantityReserved) > 0)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.CreatedAtUtc)
            .ThenBy(b => b.BatchNumber)
            .ToListAsync();

        int totalAvailable = availableBatches.Sum(b => b.QuantityOnHand - b.QuantityReserved);
        if (totalAvailable < quantityNeeded)
        {
            throw new InvalidOperationException($"Insufficient saleable stock. Requested: {quantityNeeded}, Available: {totalAvailable}");
        }

        var allocations = new List<(StockBatch Batch, int AllocatedQuantity)>();
        int remainingToAllocate = quantityNeeded;

        foreach (var batch in availableBatches)
        {
            int batchAvail = batch.QuantityOnHand - batch.QuantityReserved;
            int take = Math.Min(batchAvail, remainingToAllocate);

            allocations.Add((batch, take));
            batch.QuantityOnHand -= take;
            remainingToAllocate -= take;

            if (remainingToAllocate <= 0) break;
        }

        return allocations;
    }

    public async Task AdjustStockAsync(Guid branchId, Guid productId, Guid batchId, string adjustmentType, int quantityDelta, string reason, Guid userId)
    {
        var batch = await _db.StockBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.BranchId == branchId && b.ProductId == productId);
        if (batch == null) throw new InvalidOperationException("Batch not found.");

        if (adjustmentType == "QuantityDecrease" || adjustmentType == "Scrap" || adjustmentType == "Damage")
        {
            if (batch.QuantityOnHand < quantityDelta)
            {
                throw new InvalidOperationException("Cannot adjust below zero stock.");
            }
            batch.QuantityOnHand -= quantityDelta;
        }
        else
        {
            batch.QuantityOnHand += quantityDelta;
        }

        var adjustment = new StockAdjustment
        {
            BranchId = branchId,
            ProductId = productId,
            StockBatchId = batchId,
            AdjustmentType = adjustmentType,
            QuantityDelta = quantityDelta,
            Reason = reason,
            ApprovedByUserId = userId
        };
        _db.StockAdjustments.Add(adjustment);

        var ledger = new StockMovementLedger
        {
            BranchId = branchId,
            ProductId = productId,
            StockBatchId = batchId,
            MovementType = "Adjustment",
            QuantityDelta = adjustmentType == "QuantityDecrease" ? -quantityDelta : quantityDelta,
            RemainingQuantity = batch.QuantityOnHand,
            ReferenceDocumentType = "StockAdjustment",
            ReferenceDocumentId = adjustment.Id,
            Reason = reason,
            PerformedByUserId = userId
        };
        _db.StockMovementLedgers.Add(ledger);

        await _db.SaveChangesAsync();
    }
}

public interface IPosService
{
    Task<ApiResponse<SaleDto>> CheckoutAsync(CheckoutSaleRequest req, Guid cashierUserId);
}

public class PosService : IPosService
{
    private readonly OperationsDbContext _db;
    private readonly IInventoryService _inventory;

    public PosService(OperationsDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    public async Task<ApiResponse<SaleDto>> CheckoutAsync(CheckoutSaleRequest req, Guid cashierUserId)
    {
        // 1. Idempotency Check
        if (!string.IsNullOrWhiteSpace(req.IdempotencyKey))
        {
            var existingSale = await _db.Sales
                .Include(s => s.Items).ThenInclude(si => si.Product)
                .FirstOrDefaultAsync(s => s.IdempotencyKey == req.IdempotencyKey);
            if (existingSale != null)
            {
                // Verify payload consistency: reject key reuse if payload differs
                bool branchMatches = existingSale.BranchId == req.BranchId;
                bool shiftMatches = existingSale.ShiftId == req.ShiftId;
                bool itemsCountMatches = existingSale.Items.Count == req.Items.Count;
                bool itemsMatch = req.Items.All(ri => existingSale.Items.Any(ei => ei.ProductId == ri.ProductId));

                if (!branchMatches || !shiftMatches || !itemsCountMatches || !itemsMatch)
                {
                    return ApiResponse<SaleDto>.Fail("Idempotency key was previously used with a different request payload.");
                }

                return ApiResponse<SaleDto>.Ok(MapToDto(existingSale), "Existing transaction returned (idempotent).");
            }
        }

        // 2. Validate Shift
        var shift = await _db.CashierShifts.FirstOrDefaultAsync(cs => cs.Id == req.ShiftId);
        if (shift == null || shift.Status != "Open")
        {
            return ApiResponse<SaleDto>.Fail("Cannot checkout: Cashier shift is not open or not found.");
        }

        if (req.Items == null || !req.Items.Any())
        {
            return ApiResponse<SaleDto>.Fail("Cart cannot be empty.");
        }

        // 3. Sequential Invoice Number Generation
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var countToday = await _db.Sales.CountAsync(s => s.CreatedAtUtc.Date == DateTime.UtcNow.Date);
        var invoiceNumber = $"INV-{today}-{(countToday + 1):D5}";

        // 4. Begin Local Transaction
        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            decimal subtotal = 0;
            decimal totalDiscount = 0;
            decimal totalTax = 0;
            var saleItems = new List<SaleItem>();

            foreach (var itemReq in req.Items)
            {
                var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == itemReq.ProductId);
                if (product == null || !product.IsActive)
                {
                    return ApiResponse<SaleDto>.Fail($"Product {itemReq.ProductId} not found or inactive.");
                }

                // Enforce prescription-required restrictions
                if (product.IsPrescriptionRequired)
                {
                    if (string.IsNullOrWhiteSpace(itemReq.PrescriptionNumber) && !itemReq.PrescriptionVerified)
                    {
                        return ApiResponse<SaleDto>.Fail($"Product '{product.BrandName}' requires a valid prescription to dispense. Prescription verification is required.");
                    }
                }

                // Check unit pack multiplier if specified
                int multiplier = 1;
                if (!string.IsNullOrWhiteSpace(itemReq.PackUnitName))
                {
                    var conv = await _db.UnitConversions.FirstOrDefaultAsync(uc => uc.ProductId == product.Id && uc.PackUnitName == itemReq.PackUnitName);
                    if (conv != null) multiplier = conv.MultiplierToBaseUnit;
                }

                int totalBaseQuantity = itemReq.Quantity * multiplier;

                // Server authoritative price snapshot
                decimal effectiveUnitPrice = itemReq.UnitPrice > 0 ? itemReq.UnitPrice : product.SellingPrice;
                decimal lineGross = effectiveUnitPrice * itemReq.Quantity;
                decimal lineDiscount = Math.Min(lineGross, itemReq.DiscountAmount);
                decimal lineNet = lineGross - lineDiscount;
                decimal lineTax = lineNet * (product.TaxRatePercentage / 100.0m);
                decimal lineTotal = lineNet + lineTax;

                subtotal += lineGross;
                totalDiscount += lineDiscount;
                totalTax += lineTax;

                // FEFO Stock Allocation
                var batchAllocations = await _inventory.AllocateFefoAsync(req.BranchId, product.Id, totalBaseQuantity);

                foreach (var (batch, allocatedQty) in batchAllocations)
                {
                    var saleItem = new SaleItem
                    {
                        ProductId = product.Id,
                        StockBatchId = batch.Id,
                        BatchNumber = batch.BatchNumber,
                        Quantity = allocatedQty,
                        UnitPrice = effectiveUnitPrice,
                        DiscountAmount = lineDiscount * ((decimal)allocatedQty / totalBaseQuantity),
                        TaxAmount = lineTax * ((decimal)allocatedQty / totalBaseQuantity),
                        LineTotal = lineTotal * ((decimal)allocatedQty / totalBaseQuantity),
                        BaseUnitMultiplier = multiplier,
                        ReturnedQuantity = 0
                    };
                    saleItems.Add(saleItem);

                    // Add Stock Movement Ledger Entry
                    _db.StockMovementLedgers.Add(new StockMovementLedger
                    {
                        BranchId = req.BranchId,
                        ProductId = product.Id,
                        StockBatchId = batch.Id,
                        MovementType = "Sale",
                        QuantityDelta = -allocatedQty,
                        RemainingQuantity = batch.QuantityOnHand,
                        ReferenceDocumentType = "Sale",
                        ReferenceDocumentId = Guid.Empty, // updated after sale save
                        Reason = $"POS Checkout {invoiceNumber}",
                        PerformedByUserId = cashierUserId
                    });
                }
            }

            decimal grandTotal = Math.Round(subtotal - totalDiscount + totalTax, 2);
            decimal tendered = req.TenderedAmount > 0 ? req.TenderedAmount : grandTotal;
            decimal change = Math.Max(0, tendered - grandTotal);

            if (req.PaymentMethod == "Cash" && tendered < grandTotal)
            {
                return ApiResponse<SaleDto>.Fail($"Tendered cash amount ({tendered:C}) is less than grand total ({grandTotal:C}).");
            }

            var sale = new Sale
            {
                InvoiceNumber = invoiceNumber,
                BranchId = req.BranchId,
                RegisterId = req.RegisterId,
                ShiftId = req.ShiftId,
                CashierUserId = cashierUserId,
                CustomerName = req.CustomerName,
                Subtotal = subtotal,
                DiscountTotal = totalDiscount,
                TaxTotal = totalTax,
                GrandTotal = grandTotal,
                TenderedAmount = tendered,
                ChangeGiven = change,
                PaymentMethod = req.PaymentMethod,
                Status = "Posted",
                Notes = req.Notes,
                IdempotencyKey = req.IdempotencyKey,
                Items = saleItems
            };

            _db.Sales.Add(sale);

            // Update shift cash records in same transaction
            if (req.PaymentMethod == "Cash")
            {
                shift.CashSalesTotal += grandTotal;
            }

            // Create Transactional Outbox Message for event notification
            var outboxMessage = new OutboxMessage
            {
                Type = "SaleCompleted",
                PayloadJson = JsonSerializer.Serialize(new
                {
                    SaleId = sale.Id,
                    InvoiceNumber = sale.InvoiceNumber,
                    BranchId = sale.BranchId,
                    GrandTotal = sale.GrandTotal,
                    OccurredAtUtc = sale.CreatedAtUtc
                }),
                CorrelationId = Guid.NewGuid().ToString()
            };
            _db.OutboxMessages.Add(outboxMessage);

            await _db.SaveChangesAsync();

            // Update movement reference IDs
            foreach (var sm in _db.StockMovementLedgers.Local.Where(m => m.ReferenceDocumentId == Guid.Empty))
            {
                sm.ReferenceDocumentId = sale.Id;
            }
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            return ApiResponse<SaleDto>.Ok(MapToDto(sale), "Sale posted successfully.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return ApiResponse<SaleDto>.Fail($"Checkout failed: {ex.Message}");
        }
    }

    private static SaleDto MapToDto(Sale s) => new(
        s.Id,
        s.InvoiceNumber,
        s.BranchId,
        s.RegisterId,
        s.ShiftId,
        s.CreatedAtUtc,
        s.CustomerName,
        s.Subtotal,
        s.DiscountTotal,
        s.TaxTotal,
        s.GrandTotal,
        s.TenderedAmount,
        s.ChangeGiven,
        s.PaymentMethod,
        s.Status,
        s.Items.Select(i => new SaleItemDto(
            i.Id,
            i.ProductId,
            i.Product?.BrandName ?? "Item",
            i.BatchNumber,
            i.StockBatch?.ExpiryDate ?? DateTime.UtcNow,
            i.Quantity,
            i.UnitPrice,
            i.DiscountAmount,
            i.LineTotal,
            i.RemainingReturnableQuantity
        )).ToList()
    );
}

public interface IReturnService
{
    Task<ApiResponse<CustomerReturnDto>> ProcessReturnAsync(ProcessReturnRequest req, Guid cashierUserId);
}

public class ReturnService : IReturnService
{
    private readonly OperationsDbContext _db;

    public ReturnService(OperationsDbContext db)
    {
        _db = db;
    }

    public async Task<ApiResponse<CustomerReturnDto>> ProcessReturnAsync(ProcessReturnRequest req, Guid cashierUserId)
    {
        // 1. Idempotency Check
        if (!string.IsNullOrWhiteSpace(req.IdempotencyKey))
        {
            var existingReturn = await _db.CustomerReturns
                .Include(r => r.OriginalSale)
                .FirstOrDefaultAsync(r => r.Reason.Contains($"[IDEMP:{req.IdempotencyKey}]"));
            if (existingReturn != null)
            {
                var cleanedReason = existingReturn.Reason.Replace($" [IDEMP:{req.IdempotencyKey}]", "");
                return ApiResponse<CustomerReturnDto>.Ok(new CustomerReturnDto(
                    existingReturn.Id,
                    existingReturn.ReturnNumber,
                    existingReturn.OriginalSaleId,
                    existingReturn.OriginalSale?.InvoiceNumber ?? "",
                    existingReturn.RefundTotal,
                    existingReturn.RefundMethod,
                    cleanedReason,
                    existingReturn.CreatedAtUtc
                ), "Existing return transaction returned (idempotent).");
            }
        }

        var originalSale = await _db.Sales
            .Include(s => s.Items).ThenInclude(si => si.Product)
            .FirstOrDefaultAsync(s => s.Id == req.OriginalSaleId);

        if (originalSale == null)
        {
            return ApiResponse<CustomerReturnDto>.Fail("Original sale not found.");
        }

        var shift = await _db.CashierShifts.FirstOrDefaultAsync(cs => cs.Id == req.ShiftId);
        if (shift == null || shift.Status != "Open")
        {
            return ApiResponse<CustomerReturnDto>.Fail("Cashier shift is not open.");
        }

        // Branch check: ensure shift belongs to the branch where the original sale occurred
        if (shift.BranchId != originalSale.BranchId)
        {
            return ApiResponse<CustomerReturnDto>.Fail("Cannot process return: Cashier shift branch does not match original sale branch.");
        }

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var returnNumber = $"RET-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
            decimal refundTotal = 0;
            var returnItems = new List<CustomerReturnItem>();

            foreach (var itemInput in req.Items)
            {
                var originalItem = originalSale.Items.FirstOrDefault(i => i.Id == itemInput.OriginalSaleItemId);
                if (originalItem == null)
                {
                    return ApiResponse<CustomerReturnDto>.Fail($"Sale item {itemInput.OriginalSaleItemId} not found in original sale.");
                }

                if (itemInput.ReturnedQuantity <= 0 || itemInput.ReturnedQuantity > originalItem.RemainingReturnableQuantity)
                {
                    return ApiResponse<CustomerReturnDto>.Fail($"Returned quantity {itemInput.ReturnedQuantity} exceeds remaining returnable quantity {originalItem.RemainingReturnableQuantity}.");
                }

                originalItem.ReturnedQuantity += itemInput.ReturnedQuantity;
                decimal unitRefundPrice = originalItem.LineTotal / originalItem.Quantity;
                decimal lineRefund = Math.Round(unitRefundPrice * itemInput.ReturnedQuantity, 2);
                refundTotal += lineRefund;

                // Return item is routed to Quarantine in StockBatch
                var quarantineBatch = new StockBatch
                {
                    BranchId = originalSale.BranchId,
                    ProductId = originalItem.ProductId,
                    BatchNumber = $"RET-{originalItem.BatchNumber}",
                    ExpiryDate = DateTime.UtcNow.AddMonths(1),
                    QuantityOnHand = itemInput.ReturnedQuantity,
                    QuantityReserved = 0,
                    UnitCost = 0,
                    SellingPrice = originalItem.UnitPrice,
                    Status = "Quarantined" // Quarantine routing
                };
                _db.StockBatches.Add(quarantineBatch);

                returnItems.Add(new CustomerReturnItem
                {
                    OriginalSaleItemId = originalItem.Id,
                    ProductId = originalItem.ProductId,
                    BatchNumber = originalItem.BatchNumber,
                    ReturnedQuantity = itemInput.ReturnedQuantity,
                    RefundUnitPrice = unitRefundPrice,
                    LineRefundTotal = lineRefund,
                    DispositionStatus = "Quarantined",
                    DispositionNotes = $"Quarantined from customer return {returnNumber}"
                });

                // Add Stock Movement Ledger Entry for Quarantine Routing
                _db.StockMovementLedgers.Add(new StockMovementLedger
                {
                    BranchId = originalSale.BranchId,
                    ProductId = originalItem.ProductId,
                    StockBatchId = quarantineBatch.Id,
                    MovementType = "CustomerReturnQuarantine",
                    QuantityDelta = itemInput.ReturnedQuantity,
                    RemainingQuantity = itemInput.ReturnedQuantity,
                    ReferenceDocumentType = "CustomerReturn",
                    ReferenceDocumentId = Guid.Empty, // set after return save
                    Reason = $"Quarantine transfer from return {returnNumber}",
                    PerformedByUserId = cashierUserId
                });
            }

            string storedReason = !string.IsNullOrWhiteSpace(req.IdempotencyKey)
                ? $"{req.Reason} [IDEMP:{req.IdempotencyKey}]"
                : req.Reason;

            var customerReturn = new CustomerReturn
            {
                ReturnNumber = returnNumber,
                OriginalSaleId = originalSale.Id,
                BranchId = originalSale.BranchId,
                ShiftId = req.ShiftId,
                CashierUserId = cashierUserId,
                RefundMethod = req.RefundMethod,
                RefundTotal = refundTotal,
                Reason = storedReason,
                Items = returnItems
            };
            _db.CustomerReturns.Add(customerReturn);

            if (req.RefundMethod == "Cash")
            {
                shift.CashRefundsTotal += refundTotal;
            }

            await _db.SaveChangesAsync();

            // Link CustomerReturn ID in movement ledger
            foreach (var sm in _db.StockMovementLedgers.Local.Where(m => m.ReferenceDocumentId == Guid.Empty))
            {
                sm.ReferenceDocumentId = customerReturn.Id;
            }
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            return ApiResponse<CustomerReturnDto>.Ok(new CustomerReturnDto(
                customerReturn.Id,
                customerReturn.ReturnNumber,
                originalSale.Id,
                originalSale.InvoiceNumber,
                customerReturn.RefundTotal,
                customerReturn.RefundMethod,
                req.Reason,
                customerReturn.CreatedAtUtc
            ), "Return processed and stock quarantined.");
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return ApiResponse<CustomerReturnDto>.Fail($"Return failed: {ex.Message}");
        }
    }
}
