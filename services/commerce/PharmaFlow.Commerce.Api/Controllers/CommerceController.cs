using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Commerce.Api.Data;
using PharmaFlow.Commerce.Api.Domain;

namespace PharmaFlow.Commerce.Api.Controllers;

public record CustomerProfileInput(
    string FullName,
    string Email,
    string Phone,
    string DeliveryAddress,
    Guid? PreferredBranchId);

public record SubmitOrderRequest(
    Guid BranchId,
    List<SubmitOrderItemInput> Items,
    Guid? PrescriptionDocumentId,
    string? Notes);

public record SubmitOrderItemInput(
    Guid ProductId,
    int Quantity);

[ApiController]
[Route("api/commerce")]
[Authorize]
public class CommerceController : ControllerBase
{
    private readonly CommerceDbContext _db;

    public CommerceController(CommerceDbContext db)
    {
        _db = db;
    }

    [AllowAnonymous]
    [HttpGet("catalogue")]
    public async Task<IActionResult> GetPublishedCatalogue(
        [FromQuery] int pageIndex = 0,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        var query = _db.ProductCatalogProjections
            .Where(p => p.IsPublishedOnline && p.IsOrderEligible)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p => p.BrandName.ToLower().Contains(s) || p.GenericName.ToLower().Contains(s));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(p => p.BrandName)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(ApiResponse<PagedResult<ProductCatalogProjection>>.Ok(new PagedResult<ProductCatalogProjection>(items, total, pageIndex, pageSize)));
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var profile = await _db.CustomerProfiles.FirstOrDefaultAsync(c => c.IdentityUserId == userId);
        if (profile == null)
        {
            // Auto-create initial customer profile from claims
            var email = User.FindFirstValue(ClaimConstants.Email) ?? "";
            var name = User.FindFirstValue(ClaimConstants.FullName) ?? "";
            profile = new CustomerProfile
            {
                IdentityUserId = userId,
                Email = email,
                FullName = name,
                Phone = "",
                DeliveryAddress = ""
            };
            _db.CustomerProfiles.Add(profile);
            await _db.SaveChangesAsync();
        }

        return Ok(ApiResponse<CustomerProfile>.Ok(profile));
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] CustomerProfileInput input)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var profile = await _db.CustomerProfiles.FirstOrDefaultAsync(c => c.IdentityUserId == userId);
        if (profile == null)
        {
            profile = new CustomerProfile { IdentityUserId = userId };
            _db.CustomerProfiles.Add(profile);
        }

        profile.FullName = input.FullName.Trim();
        profile.Email = input.Email.Trim();
        profile.Phone = input.Phone.Trim();
        profile.DeliveryAddress = input.DeliveryAddress.Trim();
        profile.PreferredBranchId = input.PreferredBranchId;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<CustomerProfile>.Ok(profile, "Profile updated successfully."));
    }

    [HttpPost("orders")]
    public async Task<IActionResult> SubmitOrder([FromBody] SubmitOrderRequest req)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var profile = await _db.CustomerProfiles.FirstOrDefaultAsync(c => c.IdentityUserId == userId);
        if (profile == null)
        {
            profile = new CustomerProfile
            {
                IdentityUserId = userId,
                Email = User.FindFirstValue(ClaimConstants.Email) ?? "customer@pharmaflow.local",
                FullName = User.FindFirstValue(ClaimConstants.FullName) ?? "Valued Customer"
            };
            _db.CustomerProfiles.Add(profile);
            await _db.SaveChangesAsync();
        }

        if (req.Items == null || !req.Items.Any())
        {
            return BadRequest(ApiResponse<Guid>.Fail("Order must contain at least one item."));
        }

        var orderRef = $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}";
        decimal totalAmount = 0;
        var orderItems = new List<CustomerOrderItem>();

        foreach (var itemInput in req.Items)
        {
            var product = await _db.ProductCatalogProjections.FirstOrDefaultAsync(p => p.Id == itemInput.ProductId);
            var unitPrice = product?.SellingPrice ?? 0;
            var productName = product?.BrandName ?? "Product";

            totalAmount += unitPrice * itemInput.Quantity;

            orderItems.Add(new CustomerOrderItem
            {
                ProductId = itemInput.ProductId,
                ProductName = productName,
                Quantity = itemInput.Quantity,
                UnitPrice = unitPrice
            });
        }

        var order = new CustomerOrder
        {
            OrderReference = orderRef,
            CustomerId = profile.Id,
            BranchId = req.BranchId,
            Status = "PendingStaffReview",
            TotalAmount = totalAmount,
            PrescriptionDocumentId = req.PrescriptionDocumentId,
            Notes = req.Notes,
            SubmittedAtUtc = DateTime.UtcNow,
            Items = orderItems
        };

        _db.CustomerOrders.Add(order);

        // Transactional Outbox for asynchronous notification to Operations
        _db.OutboxMessages.Add(new CommerceOutboxMessage
        {
            Type = "CustomerOrderSubmitted",
            PayloadJson = JsonSerializer.Serialize(new
            {
                OrderId = order.Id,
                OrderReference = order.OrderReference,
                CustomerId = profile.Id,
                BranchId = order.BranchId,
                TotalAmount = order.TotalAmount,
                PrescriptionDocumentId = order.PrescriptionDocumentId
            }),
            CorrelationId = Guid.NewGuid().ToString()
        });

        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            OrderId = order.Id,
            OrderReference = order.OrderReference,
            order.Status,
            order.TotalAmount
        }, "Click-and-collect order request submitted successfully."));
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetCustomerOrders()
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var profile = await _db.CustomerProfiles.FirstOrDefaultAsync(c => c.IdentityUserId == userId);
        if (profile == null) return Ok(ApiResponse<List<object>>.Ok(new List<object>()));

        var orders = await _db.CustomerOrders
            .Include(o => o.Items)
            .Where(o => o.CustomerId == profile.Id)
            .OrderByDescending(o => o.SubmittedAtUtc)
            .Select(o => new
            {
                o.Id,
                o.OrderReference,
                o.BranchId,
                o.Status,
                o.TotalAmount,
                o.PrescriptionDocumentId,
                o.SubmittedAtUtc,
                ItemsCount = o.Items.Count
            })
            .ToListAsync();

        return Ok(ApiResponse<object>.Ok(orders));
    }

    [HttpGet("orders/{id}")]
    public async Task<IActionResult> GetOrderDetails(Guid id)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var profile = await _db.CustomerProfiles.FirstOrDefaultAsync(c => c.IdentityUserId == userId);
        if (profile == null) return NotFound(ApiResponse<object>.Fail("Order not found."));

        var order = await _db.CustomerOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == profile.Id);

        if (order == null) return NotFound(ApiResponse<object>.Fail("Order not found."));

        return Ok(ApiResponse<object>.Ok(order));
    }

    [HttpPost("orders/{id}/cancel")]
    public async Task<IActionResult> CancelOrder(Guid id, [FromBody] string? reason)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var profile = await _db.CustomerProfiles.FirstOrDefaultAsync(c => c.IdentityUserId == userId);
        if (profile == null) return NotFound(ApiResponse<bool>.Fail("Order not found."));

        var order = await _db.CustomerOrders.FirstOrDefaultAsync(o => o.Id == id && o.CustomerId == profile.Id);
        if (order == null) return NotFound(ApiResponse<bool>.Fail("Order not found."));

        if (order.Status != "PendingStaffReview")
        {
            return BadRequest(ApiResponse<bool>.Fail($"Cannot cancel order in '{order.Status}' status. Only pending orders can be cancelled."));
        }

        order.Status = "Cancelled";
        order.CancelledAtUtc = DateTime.UtcNow;
        order.CancellationReason = reason ?? "Cancelled by customer";

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Order successfully cancelled."));
    }
}
