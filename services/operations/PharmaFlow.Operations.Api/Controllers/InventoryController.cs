using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Operations.Api.Data;
using PharmaFlow.Operations.Api.Domain;
using PharmaFlow.Operations.Api.DTOs;
using PharmaFlow.Operations.Api.Services;

namespace PharmaFlow.Operations.Api.Controllers;

[ApiController]
[Route("api/inventory")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly OperationsDbContext _db;
    private readonly IInventoryService _inventory;

    public InventoryController(OperationsDbContext db, IInventoryService inventory)
    {
        _db = db;
        _inventory = inventory;
    }

    [HttpGet("batches")]
    public async Task<IActionResult> GetBatches(
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? productId,
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 0,
        [FromQuery] int pageSize = 50)
    {
        var query = _db.StockBatches
            .Include(b => b.Product)
            .AsQueryable();

        if (branchId.HasValue) query = query.Where(b => b.BranchId == branchId.Value);
        if (productId.HasValue) query = query.Where(b => b.ProductId == productId.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(b => b.Status == status);

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(b => b.ExpiryDate)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(b => new StockBatchDto(
                b.Id,
                b.BranchId,
                b.ProductId,
                b.Product.BrandName,
                b.Product.Sku,
                b.BatchNumber,
                b.ExpiryDate,
                b.QuantityOnHand,
                b.QuantityReserved,
                b.QuantityOnHand - b.QuantityReserved,
                b.UnitCost,
                b.SellingPrice,
                b.Status,
                b.Status == "Active" && b.ExpiryDate > DateTime.UtcNow.Date && (b.QuantityOnHand - b.QuantityReserved) > 0
            ))
            .ToListAsync();

        return Ok(ApiResponse<PagedResult<StockBatchDto>>.Ok(new PagedResult<StockBatchDto>(items, total, pageIndex, pageSize)));
    }

    [HttpPost("adjust")]
    public async Task<IActionResult> AdjustStock([FromBody] StockAdjustmentRequest req)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        try
        {
            await _inventory.AdjustStockAsync(req.BranchId, req.ProductId, req.StockBatchId, req.AdjustmentType, req.QuantityDelta, req.Reason, userId);
            return Ok(ApiResponse<bool>.Ok(true, "Stock adjusted successfully."));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<bool>.Fail(ex.Message));
        }
    }

    [HttpGet("near-expiry")]
    public async Task<IActionResult> GetNearExpiry([FromQuery] Guid? branchId, [FromQuery] int daysThreshold = 90)
    {
        var thresholdDate = DateTime.UtcNow.Date.AddDays(daysThreshold);
        var query = _db.StockBatches
            .Include(b => b.Product)
            .Where(b => b.Status == "Active" && b.ExpiryDate <= thresholdDate && b.QuantityOnHand > 0);

        if (branchId.HasValue) query = query.Where(b => b.BranchId == branchId.Value);

        var items = await query
            .OrderBy(b => b.ExpiryDate)
            .Select(b => new StockBatchDto(
                b.Id,
                b.BranchId,
                b.ProductId,
                b.Product.BrandName,
                b.Product.Sku,
                b.BatchNumber,
                b.ExpiryDate,
                b.QuantityOnHand,
                b.QuantityReserved,
                b.QuantityOnHand - b.QuantityReserved,
                b.UnitCost,
                b.SellingPrice,
                b.Status,
                b.Status == "Active" && b.ExpiryDate > DateTime.UtcNow.Date
            ))
            .ToListAsync();

        return Ok(ApiResponse<List<StockBatchDto>>.Ok(items));
    }

    [HttpGet("low-stock")]
    public async Task<IActionResult> GetLowStock([FromQuery] Guid? branchId)
    {
        var products = await _db.Products
            .Include(p => p.StockBatches)
            .Where(p => p.IsActive)
            .ToListAsync();

        var lowStockList = new List<object>();
        foreach (var p in products)
        {
            var batches = p.StockBatches.Where(b => b.Status == "Active" && b.ExpiryDate > DateTime.UtcNow.Date);
            if (branchId.HasValue) batches = batches.Where(b => b.BranchId == branchId.Value);

            int totalStock = batches.Sum(b => b.QuantityOnHand - b.QuantityReserved);
            if (totalStock <= p.ReorderThreshold)
            {
                lowStockList.Add(new
                {
                    ProductId = p.Id,
                    p.Sku,
                    p.BrandName,
                    p.GenericName,
                    p.ReorderThreshold,
                    CurrentStock = totalStock,
                    p.BaseUnit
                });
            }
        }

        return Ok(ApiResponse<object>.Ok(lowStockList));
    }
}
