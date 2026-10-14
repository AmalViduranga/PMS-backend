using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Operations.Api.Data;
using PharmaFlow.Operations.Api.Domain;
using PharmaFlow.Operations.Api.DTOs;

namespace PharmaFlow.Operations.Api.Controllers;

[ApiController]
[Route("api/purchasing")]
[Authorize]
public class PurchasingController : ControllerBase
{
    private readonly OperationsDbContext _db;

    public PurchasingController(OperationsDbContext db)
    {
        _db = db;
    }

    // -------------------------------------------------------------
    // Suppliers
    // -------------------------------------------------------------
    [HttpGet("suppliers")]
    public async Task<IActionResult> GetSuppliers()
    {
        var suppliers = await _db.Suppliers
            .OrderBy(s => s.Name)
            .Select(s => new SupplierDto(s.Id, s.Code, s.Name, s.ContactPerson, s.Email, s.Phone, s.Address, s.PaymentTermsDays, s.IsActive))
            .ToListAsync();

        return Ok(ApiResponse<List<SupplierDto>>.Ok(suppliers));
    }

    [HttpPost("suppliers")]
    public async Task<IActionResult> CreateSupplier([FromBody] CreateSupplierRequest req)
    {
        var existing = await _db.Suppliers.FirstOrDefaultAsync(s => s.Code.ToLower() == req.Code.Trim().ToLower());
        if (existing != null) return BadRequest(ApiResponse<Guid>.Fail("Supplier with this code already exists."));

        var supplier = new Supplier
        {
            Code = req.Code.Trim().ToUpperInvariant(),
            Name = req.Name.Trim(),
            ContactPerson = req.ContactPerson.Trim(),
            Email = req.Email.Trim(),
            Phone = req.Phone.Trim(),
            Address = req.Address.Trim(),
            PaymentTermsDays = req.PaymentTermsDays > 0 ? req.PaymentTermsDays : 30,
            IsActive = true
        };

        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Guid>.Ok(supplier.Id, "Supplier created successfully."));
    }

    // -------------------------------------------------------------
    // Purchase Orders
    // -------------------------------------------------------------
    [HttpGet("purchase-orders")]
    public async Task<IActionResult> GetPurchaseOrders([FromQuery] Guid? branchId, [FromQuery] string? status)
    {
        var query = _db.PurchaseOrders
            .Include(po => po.Supplier)
            .Include(po => po.Branch)
            .Include(po => po.Items).ThenInclude(poi => poi.Product)
            .AsQueryable();

        if (branchId.HasValue) query = query.Where(po => po.BranchId == branchId.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(po => po.Status == status);

        var list = await query
            .OrderByDescending(po => po.CreatedAtUtc)
            .Select(po => new PurchaseOrderDto(
                po.Id,
                po.PoNumber,
                po.SupplierId,
                po.Supplier.Name,
                po.BranchId,
                po.Branch.Name,
                po.Status,
                po.TotalCost,
                po.CreatedAtUtc,
                po.Items.Select(i => new PurchaseOrderItemDto(
                    i.Id,
                    i.ProductId,
                    i.Product.BrandName,
                    i.OrderedQuantity,
                    i.UnitCost,
                    i.ReceivedQuantity,
                    i.BonusQuantity
                )).ToList()
            ))
            .ToListAsync();

        return Ok(ApiResponse<List<PurchaseOrderDto>>.Ok(list));
    }

    [HttpPost("purchase-orders")]
    public async Task<IActionResult> CreatePurchaseOrder([FromBody] CreatePurchaseOrderRequest req)
    {
        var userIdStr = User.GetUserId();
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var poNumber = $"PO-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
        decimal totalCost = 0;
        var poItems = new List<PurchaseOrderItem>();

        foreach (var item in req.Items)
        {
            var lineCost = item.OrderedQuantity * item.UnitCost;
            totalCost += lineCost;

            poItems.Add(new PurchaseOrderItem
            {
                ProductId = item.ProductId,
                OrderedQuantity = item.OrderedQuantity,
                UnitCost = item.UnitCost,
                TaxRate = item.TaxRate,
                ReceivedQuantity = 0,
                BonusQuantity = 0
            });
        }

        var po = new PurchaseOrder
        {
            PoNumber = poNumber,
            SupplierId = req.SupplierId,
            BranchId = req.BranchId,
            Status = "Draft",
            TotalCost = totalCost,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            Items = poItems
        };

        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Guid>.Ok(po.Id, "Purchase order draft created successfully."));
    }

    [HttpPost("purchase-orders/{id}/approve")]
    public async Task<IActionResult> ApprovePurchaseOrder(Guid id)
    {
        var userIdStr = User.GetUserId();
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(p => p.Id == id);
        if (po == null) return NotFound(ApiResponse<bool>.Fail("Purchase order not found."));

        if (po.Status != "Draft" && po.Status != "Submitted")
        {
            return BadRequest(ApiResponse<bool>.Fail($"PO cannot be approved in '{po.Status}' status."));
        }

        po.Status = "Approved";
        po.ApprovedByUserId = userId;
        po.ApprovedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Purchase order approved successfully."));
    }

    // -------------------------------------------------------------
    // Goods Receipt (Receiving with Batch Creation & FEFO)
    // -------------------------------------------------------------
    [HttpPost("purchase-orders/{id}/receive")]
    public async Task<IActionResult> ReceiveGoods(Guid id, [FromBody] ReceiveGoodsReceiptRequest req)
    {
        var userIdStr = User.GetUserId();
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var po = await _db.PurchaseOrders
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (po == null) return NotFound(ApiResponse<string>.Fail("Purchase order not found."));

        if (po.Status != "Approved" && po.Status != "PartiallyReceived")
        {
            return BadRequest(ApiResponse<string>.Fail($"Cannot receive PO in '{po.Status}' status. Must be Approved."));
        }

        // Duplicate goods receipt invoice check
        if (!string.IsNullOrWhiteSpace(req.SupplierInvoiceNumber))
        {
            var duplicateInvoice = await _db.GoodsReceipts.FirstOrDefaultAsync(g => g.PurchaseOrderId == po.Id && g.SupplierInvoiceNumber.Trim().ToLower() == req.SupplierInvoiceNumber.Trim().ToLower());
            if (duplicateInvoice != null)
            {
                return BadRequest(ApiResponse<string>.Fail($"Goods receipt with supplier invoice '{req.SupplierInvoiceNumber}' has already been processed for this purchase order."));
            }
        }

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
            var grnNumber = $"GRN-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
            decimal totalAcquisitionCost = 0;
            var grnItems = new List<GoodsReceiptItem>();

            foreach (var itemInput in req.Items)
            {
                var poItem = po.Items.FirstOrDefault(i => i.Id == itemInput.PurchaseOrderItemId);
                if (poItem == null)
                {
                    return BadRequest(ApiResponse<string>.Fail($"PO Item {itemInput.PurchaseOrderItemId} not found in this order."));
                }

                // Over-receipt control check
                int totalReceiving = itemInput.ReceivedQuantity + itemInput.BonusQuantity;
                if (poItem.ReceivedQuantity + itemInput.ReceivedQuantity > poItem.OrderedQuantity)
                {
                    return BadRequest(ApiResponse<string>.Fail($"Over-receipt error: Receiving {itemInput.ReceivedQuantity} units exceeds remaining ordered quantity ({poItem.OrderedQuantity - poItem.ReceivedQuantity})."));
                }

                poItem.ReceivedQuantity += itemInput.ReceivedQuantity;
                poItem.BonusQuantity += itemInput.BonusQuantity;

                int baseQuantity = totalReceiving * (itemInput.MultiplierToBaseUnit > 0 ? itemInput.MultiplierToBaseUnit : 1);
                decimal lineCost = itemInput.ReceivedQuantity * itemInput.UnitAcquisitionCost;
                totalAcquisitionCost += lineCost;

                var grnItem = new GoodsReceiptItem
                {
                    PurchaseOrderItemId = poItem.Id,
                    ProductId = itemInput.ProductId,
                    BatchNumber = itemInput.BatchNumber.Trim(),
                    ExpiryDate = DateTime.SpecifyKind(itemInput.ExpiryDate.ToUniversalTime(), DateTimeKind.Utc),
                    ReceivedQuantity = itemInput.ReceivedQuantity,
                    BonusQuantity = itemInput.BonusQuantity,
                    MultiplierToBaseUnit = itemInput.MultiplierToBaseUnit,
                    UnitAcquisitionCost = itemInput.UnitAcquisitionCost
                };
                grnItems.Add(grnItem);

                // Add or update StockBatch in Operations
                var existingBatch = await _db.StockBatches
                    .FirstOrDefaultAsync(b => b.BranchId == po.BranchId && b.ProductId == itemInput.ProductId && b.BatchNumber == itemInput.BatchNumber.Trim());

                if (existingBatch != null)
                {
                    existingBatch.QuantityOnHand += baseQuantity;
                    existingBatch.UnitCost = itemInput.UnitAcquisitionCost;
                }
                else
                {
                    var product = await _db.Products.FindAsync(itemInput.ProductId);
                    var newBatch = new StockBatch
                    {
                        BranchId = po.BranchId,
                        ProductId = itemInput.ProductId,
                        BatchNumber = itemInput.BatchNumber.Trim(),
                        ExpiryDate = DateTime.SpecifyKind(itemInput.ExpiryDate.ToUniversalTime(), DateTimeKind.Utc),
                        QuantityOnHand = baseQuantity,
                        QuantityReserved = 0,
                        UnitCost = itemInput.UnitAcquisitionCost,
                        SellingPrice = product?.SellingPrice ?? (itemInput.UnitAcquisitionCost * 1.3m),
                        Status = "Active"
                    };
                    _db.StockBatches.Add(newBatch);
                    await _db.SaveChangesAsync();
                    existingBatch = newBatch;
                }

                // Add to Stock Movement Ledger
                _db.StockMovementLedgers.Add(new StockMovementLedger
                {
                    BranchId = po.BranchId,
                    ProductId = itemInput.ProductId,
                    StockBatchId = existingBatch.Id,
                    MovementType = "GoodsReceipt",
                    QuantityDelta = baseQuantity,
                    RemainingQuantity = existingBatch.QuantityOnHand,
                    ReferenceDocumentType = "GoodsReceipt",
                    ReferenceDocumentId = Guid.Empty, // set after grn created
                    Reason = $"Goods receipt {grnNumber} for PO {po.PoNumber}",
                    PerformedByUserId = userId
                });
            }

            var grn = new GoodsReceipt
            {
                GrnNumber = grnNumber,
                PurchaseOrderId = po.Id,
                SupplierInvoiceNumber = req.SupplierInvoiceNumber,
                ReceivedAtUtc = DateTime.UtcNow,
                ReceivedByUserId = userId,
                TotalAcquisitionCost = totalAcquisitionCost,
                Notes = req.Notes,
                Items = grnItems
            };
            _db.GoodsReceipts.Add(grn);

            // Update PO Status
            bool allFullyReceived = po.Items.All(i => i.ReceivedQuantity >= i.OrderedQuantity);
            po.Status = allFullyReceived ? "Received" : "PartiallyReceived";

            await _db.SaveChangesAsync();

            // Link GRN ID in movement ledger
            foreach (var sm in _db.StockMovementLedgers.Local.Where(m => m.ReferenceDocumentId == Guid.Empty))
            {
                sm.ReferenceDocumentId = grn.Id;
            }
            await _db.SaveChangesAsync();

            await tx.CommitAsync();

            return Ok(ApiResponse<string>.Ok(grn.GrnNumber, "Goods receipt successfully processed and stock updated."));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return BadRequest(ApiResponse<string>.Fail($"Goods receipt failed: {ex.Message}"));
        }
    }
}
            }
            return BadRequest(ApiResponse<string>.Fail($"Goods receipt failed: {ex.Message}"));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return BadRequest(ApiResponse<string>.Fail($"Goods receipt failed: {ex.Message}"));
        }
    }
}
