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
[Route("api/shifts")]
[Authorize]
public class ShiftsController : ControllerBase
{
    private readonly OperationsDbContext _db;

    public ShiftsController(OperationsDbContext db)
    {
        _db = db;
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentShift([FromQuery] Guid? registerId)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var query = _db.CashierShifts
            .Include(s => s.Branch)
            .Include(s => s.Register)
            .Where(s => s.CashierUserId == userId && s.Status == "Open");

        if (registerId.HasValue)
        {
            query = query.Where(s => s.RegisterId == registerId.Value);
        }

        var shift = await query.OrderByDescending(s => s.OpenedAtUtc).FirstOrDefaultAsync();
        if (shift == null)
        {
            return Ok(ApiResponse<CashierShiftDto?>.Ok(null, "No open shift found."));
        }

        return Ok(ApiResponse<CashierShiftDto>.Ok(MapShift(shift)));
    }

    [HttpPost("open")]
    public async Task<IActionResult> OpenShift([FromBody] OpenShiftRequest req)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        // Check if cashier already has an open shift on this register
        var existingOpen = await _db.CashierShifts
            .FirstOrDefaultAsync(s => s.CashierUserId == userId && s.RegisterId == req.RegisterId && s.Status == "Open");

        if (existingOpen != null)
        {
            return BadRequest(ApiResponse<Guid>.Fail("You already have an open shift on this register."));
        }

        var shift = new CashierShift
        {
            BranchId = req.BranchId,
            RegisterId = req.RegisterId,
            CashierUserId = userId,
            OpeningFloat = req.OpeningFloat,
            CashSalesTotal = 0,
            CashRefundsTotal = 0,
            CashInTotal = 0,
            CashOutTotal = 0,
            Status = "Open",
            Notes = req.Notes,
            OpenedAtUtc = DateTime.UtcNow
        };

        _db.CashierShifts.Add(shift);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Guid>.Ok(shift.Id, "Shift successfully opened."));
    }

    [HttpPost("{id}/close")]
    public async Task<IActionResult> CloseShift(Guid id, [FromBody] CloseShiftRequest req)
    {
        var shift = await _db.CashierShifts.FirstOrDefaultAsync(s => s.Id == id);
        if (shift == null) return NotFound(ApiResponse<bool>.Fail("Shift not found."));

        if (shift.Status != "Open")
        {
            return BadRequest(ApiResponse<bool>.Fail("Shift is already closed."));
        }

        shift.Status = "Closed";
        shift.ClosedAtUtc = DateTime.UtcNow;
        shift.ClosingCountedCash = req.ClosingCountedCash;
        if (!string.IsNullOrWhiteSpace(req.Notes))
        {
            shift.Notes = string.IsNullOrWhiteSpace(shift.Notes) ? req.Notes : $"{shift.Notes} | {req.Notes}";
        }

        await _db.SaveChangesAsync();

        return Ok(ApiResponse<object>.Ok(new
        {
            shift.Id,
            shift.ExpectedCash,
            shift.ClosingCountedCash,
            shift.Variance,
            shift.ClosedAtUtc
        }, "Shift successfully closed."));
    }

    [HttpPost("{id}/cash-movement")]
    public async Task<IActionResult> RecordCashMovement(Guid id, [FromBody] CashMovementRequest req)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var shift = await _db.CashierShifts.FirstOrDefaultAsync(s => s.Id == id);
        if (shift == null || shift.Status != "Open")
        {
            return BadRequest(ApiResponse<bool>.Fail("Shift is not open."));
        }

        var movement = new CashMovement
        {
            ShiftId = shift.Id,
            MovementType = req.MovementType,
            Amount = req.Amount,
            Reason = req.Reason,
            PerformedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.CashMovements.Add(movement);

        if (req.MovementType == "CashIn") shift.CashInTotal += req.Amount;
        else if (req.MovementType == "CashOut") shift.CashOutTotal += req.Amount;

        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Guid>.Ok(movement.Id, "Cash movement recorded successfully."));
    }

    private static CashierShiftDto MapShift(CashierShift s) => new(
        s.Id,
        s.BranchId,
        s.Branch?.Name ?? "Branch",
        s.RegisterId,
        s.Register?.Name ?? "Register",
        s.CashierUserId,
        s.OpenedAtUtc,
        s.ClosedAtUtc,
        s.OpeningFloat,
        s.CashSalesTotal,
        s.CashRefundsTotal,
        s.CashInTotal,
        s.CashOutTotal,
        s.ExpectedCash,
        s.ClosingCountedCash,
        s.Variance,
        s.Status,
        s.Notes
    );
}
