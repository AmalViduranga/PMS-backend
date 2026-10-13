using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Operations.Api.Data;
using PharmaFlow.Operations.Api.DTOs;
using PharmaFlow.Operations.Api.Services;

namespace PharmaFlow.Operations.Api.Controllers;

[ApiController]
[Route("api/pos")]
[Authorize]
public class PosController : ControllerBase
{
    private readonly IPosService _posService;
    private readonly IReturnService _returnService;
    private readonly OperationsDbContext _db;

    public PosController(IPosService posService, IReturnService returnService, OperationsDbContext db)
    {
        _posService = posService;
        _returnService = returnService;
        _db = db;
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutSaleRequest req)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var result = await _posService.CheckoutAsync(req, userId);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPost("returns")]
    public async Task<IActionResult> ProcessReturn([FromBody] ProcessReturnRequest req)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var result = await _returnService.ProcessReturnAsync(req, userId);
        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpGet("sales/{id}")]
    public async Task<IActionResult> GetSaleById(Guid id)
    {
        var sale = await _db.Sales
            .Include(s => s.Items).ThenInclude(si => si.Product)
            .Include(s => s.Items).ThenInclude(si => si.StockBatch)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (sale == null) return NotFound(ApiResponse<SaleDto>.Fail("Sale not found."));

        var dto = new SaleDto(
            sale.Id,
            sale.InvoiceNumber,
            sale.BranchId,
            sale.RegisterId,
            sale.ShiftId,
            sale.CreatedAtUtc,
            sale.CustomerName,
            sale.Subtotal,
            sale.DiscountTotal,
            sale.TaxTotal,
            sale.GrandTotal,
            sale.TenderedAmount,
            sale.ChangeGiven,
            sale.PaymentMethod,
            sale.Status,
            sale.Items.Select(i => new SaleItemDto(
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

        return Ok(ApiResponse<SaleDto>.Ok(dto));
    }

    [HttpGet("sales/invoice/{invoiceNumber}")]
    public async Task<IActionResult> GetSaleByInvoice(string invoiceNumber)
    {
        var sale = await _db.Sales
            .Include(s => s.Items).ThenInclude(si => si.Product)
            .Include(s => s.Items).ThenInclude(si => si.StockBatch)
            .FirstOrDefaultAsync(s => s.InvoiceNumber == invoiceNumber.Trim());

        if (sale == null) return NotFound(ApiResponse<SaleDto>.Fail("Invoice not found."));

        var dto = new SaleDto(
            sale.Id,
            sale.InvoiceNumber,
            sale.BranchId,
            sale.RegisterId,
            sale.ShiftId,
            sale.CreatedAtUtc,
            sale.CustomerName,
            sale.Subtotal,
            sale.DiscountTotal,
            sale.TaxTotal,
            sale.GrandTotal,
            sale.TenderedAmount,
            sale.ChangeGiven,
            sale.PaymentMethod,
            sale.Status,
            sale.Items.Select(i => new SaleItemDto(
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

        return Ok(ApiResponse<SaleDto>.Ok(dto));
    }

    [HttpGet("sales")]
    public async Task<IActionResult> GetSales(
        [FromQuery] int pageIndex = 0,
        [FromQuery] int pageSize = 20,
        [FromQuery] Guid? branchId = null,
        [FromQuery] string? search = null)
    {
        var query = _db.Sales.Include(s => s.Items).ThenInclude(si => si.Product).AsQueryable();

        if (branchId.HasValue) query = query.Where(s => s.BranchId == branchId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(x => x.InvoiceNumber.Contains(s) || (x.CustomerName != null && x.CustomerName.Contains(s)));
        }

        var total = await query.CountAsync();
        var sales = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(s => new SaleDto(
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
                    i.Product.BrandName,
                    i.BatchNumber,
                    DateTime.UtcNow,
                    i.Quantity,
                    i.UnitPrice,
                    i.DiscountAmount,
                    i.LineTotal,
                    i.RemainingReturnableQuantity
                )).ToList()
            ))
            .ToListAsync();

        return Ok(ApiResponse<PagedResult<SaleDto>>.Ok(new PagedResult<SaleDto>(sales, total, pageIndex, pageSize)));
    }
}
