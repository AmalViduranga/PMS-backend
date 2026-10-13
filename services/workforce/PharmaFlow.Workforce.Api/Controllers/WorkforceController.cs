using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Contracts.Common;
using PharmaFlow.Workforce.Api.Data;
using PharmaFlow.Workforce.Api.Domain;

namespace PharmaFlow.Workforce.Api.Controllers;

public record CreateEmployeeRequest(
    string EmployeeCode,
    string FirstName,
    string LastName,
    string Designation,
    string Department,
    Guid PrimaryBranchId,
    string ContactNumber,
    string Email,
    string? EmergencyContact,
    Guid? IdentityUserId);

public record ClockInOutRequest(
    Guid EmployeeId,
    Guid BranchId);

public record AttendanceCorrectionRequest(
    DateTime ClockInUtc,
    DateTime ClockOutUtc,
    string Status,
    string Reason);

public record CreateScheduleRequest(
    Guid EmployeeId,
    Guid BranchId,
    DateTime ShiftDate,
    TimeSpan StartTime,
    TimeSpan EndTime,
    string? Notes);

public record SubmitLeaveRequest(
    Guid EmployeeId,
    Guid LeaveTypeId,
    DateTime StartDate,
    DateTime EndDate,
    string Reason);

public record ReviewLeaveRequest(
    bool Approved,
    string? RejectionReason);

[ApiController]
[Route("api/workforce")]
[Authorize]
public class WorkforceController : ControllerBase
{
    private readonly WorkforceDbContext _db;

    public WorkforceController(WorkforceDbContext db)
    {
        _db = db;
    }

    // -------------------------------------------------------------
    // Employees
    // -------------------------------------------------------------
    [HttpGet("employees")]
    public async Task<IActionResult> GetEmployees([FromQuery] Guid? branchId, [FromQuery] string? search)
    {
        var query = _db.Employees.AsQueryable();

        if (branchId.HasValue) query = query.Where(e => e.PrimaryBranchId == branchId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(e => e.FirstName.ToLower().Contains(s) || e.LastName.ToLower().Contains(s) || e.EmployeeCode.ToLower().Contains(s));
        }

        var employees = await query.OrderBy(e => e.FirstName).ToListAsync();
        return Ok(ApiResponse<List<Employee>>.Ok(employees));
    }

    [HttpPost("employees")]
    public async Task<IActionResult> CreateEmployee([FromBody] CreateEmployeeRequest req)
    {
        var existing = await _db.Employees.FirstOrDefaultAsync(e => e.EmployeeCode.ToLower() == req.EmployeeCode.Trim().ToLower());
        if (existing != null) return BadRequest(ApiResponse<Guid>.Fail("Employee code already exists."));

        var employee = new Employee
        {
            EmployeeCode = req.EmployeeCode.Trim().ToUpperInvariant(),
            FirstName = req.FirstName.Trim(),
            LastName = req.LastName.Trim(),
            Designation = req.Designation.Trim(),
            Department = req.Department.Trim(),
            PrimaryBranchId = req.PrimaryBranchId,
            ContactNumber = req.ContactNumber.Trim(),
            Email = req.Email.Trim(),
            EmergencyContact = req.EmergencyContact,
            IdentityUserId = req.IdentityUserId,
            EmploymentStatus = "Active",
            JoinedDate = DateTime.UtcNow.Date
        };

        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();

        // Seed initial leave balances for current year
        var leaveTypes = await _db.LeaveTypes.ToListAsync();
        int currentYear = DateTime.UtcNow.Year;
        foreach (var lt in leaveTypes)
        {
            _db.LeaveBalanceLedgers.Add(new LeaveBalanceLedger
            {
                EmployeeId = employee.Id,
                LeaveTypeId = lt.Id,
                Year = currentYear,
                TotalAllocated = lt.AnnualAllowanceDays,
                UsedDays = 0,
                PendingDays = 0
            });
        }
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Guid>.Ok(employee.Id, "Employee created successfully with initial leave balances."));
    }

    // -------------------------------------------------------------
    // Attendance & Breaks
    // -------------------------------------------------------------
    [HttpPost("attendance/clock-in")]
    public async Task<IActionResult> ClockIn([FromBody] ClockInOutRequest req)
    {
        var today = DateTime.UtcNow.Date;
        var existing = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.EmployeeId == req.EmployeeId && a.Date == today);
        if (existing != null)
        {
            return BadRequest(ApiResponse<Guid>.Fail("Employee has already clocked in for today."));
        }

        var record = new AttendanceRecord
        {
            EmployeeId = req.EmployeeId,
            BranchId = req.BranchId,
            Date = today,
            ClockInUtc = DateTime.UtcNow,
            Status = "Present"
        };

        _db.AttendanceRecords.Add(record);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Guid>.Ok(record.Id, "Clocked in successfully."));
    }

    [HttpPost("attendance/clock-out")]
    public async Task<IActionResult> ClockOut([FromBody] ClockInOutRequest req)
    {
        var today = DateTime.UtcNow.Date;
        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.EmployeeId == req.EmployeeId && a.Date == today);
        if (record == null)
        {
            return BadRequest(ApiResponse<bool>.Fail("No clock-in record found for today."));
        }

        if (record.ClockOutUtc.HasValue)
        {
            return BadRequest(ApiResponse<bool>.Fail("Employee has already clocked out for today."));
        }

        record.ClockOutUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<bool>.Ok(true, "Clocked out successfully."));
    }

    [HttpPost("attendance/{id}/correct")]
    public async Task<IActionResult> CorrectAttendance(Guid id, [FromBody] AttendanceCorrectionRequest req)
    {
        var userIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(userIdStr, out var managerId)) return Unauthorized();

        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.Id == id);
        if (record == null) return NotFound(ApiResponse<bool>.Fail("Attendance record not found."));

        if (string.IsNullOrWhiteSpace(req.Reason))
        {
            return BadRequest(ApiResponse<bool>.Fail("Manager correction requires an explicit justification reason."));
        }

        record.ClockInUtc = req.ClockInUtc;
        record.ClockOutUtc = req.ClockOutUtc;
        record.Status = req.Status;
        record.ManagerCorrectionReason = req.Reason;
        record.CorrectedByUserId = managerId;
        record.CorrectedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, "Attendance record corrected with audit log."));
    }

    // -------------------------------------------------------------
    // Work Schedules (with Overlap Prevention)
    // -------------------------------------------------------------
    [HttpGet("schedules")]
    public async Task<IActionResult> GetSchedules([FromQuery] Guid? branchId, [FromQuery] DateTime? date)
    {
        var query = _db.WorkSchedules.Include(ws => ws.Employee).AsQueryable();

        if (branchId.HasValue) query = query.Where(ws => ws.BranchId == branchId.Value);
        if (date.HasValue) query = query.Where(ws => ws.ShiftDate.Date == date.Value.Date);

        var list = await query.OrderBy(ws => ws.StartTime).ToListAsync();
        return Ok(ApiResponse<List<WorkSchedule>>.Ok(list));
    }

    [HttpPost("schedules")]
    public async Task<IActionResult> CreateSchedule([FromBody] CreateScheduleRequest req)
    {
        // Overlap Check across ALL branches for this employee on this date
        var overlapping = await _db.WorkSchedules
            .AnyAsync(ws => ws.EmployeeId == req.EmployeeId &&
                            ws.ShiftDate.Date == req.ShiftDate.Date &&
                            req.StartTime < ws.EndTime &&
                            req.EndTime > ws.StartTime);

        if (overlapping)
        {
            return BadRequest(ApiResponse<Guid>.Fail("Cannot schedule: Overlaps with an existing shift for this employee."));
        }

        var schedule = new WorkSchedule
        {
            EmployeeId = req.EmployeeId,
            BranchId = req.BranchId,
            ShiftDate = req.ShiftDate.Date,
            StartTime = req.StartTime,
            EndTime = req.EndTime,
            Notes = req.Notes
        };

        _db.WorkSchedules.Add(schedule);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Guid>.Ok(schedule.Id, "Work schedule created successfully."));
    }

    // -------------------------------------------------------------
    // Leaves & Balance Ledger (Prevents Self-Approval)
    // -------------------------------------------------------------
    [HttpGet("leaves/balances")]
    public async Task<IActionResult> GetLeaveBalances([FromQuery] Guid employeeId)
    {
        int year = DateTime.UtcNow.Year;
        var balances = await _db.LeaveBalanceLedgers
            .Include(lbl => lbl.LeaveType)
            .Where(lbl => lbl.EmployeeId == employeeId && lbl.Year == year)
            .ToListAsync();

        return Ok(ApiResponse<List<LeaveBalanceLedger>>.Ok(balances));
    }

    [HttpPost("leaves/request")]
    public async Task<IActionResult> SubmitLeaveRequest([FromBody] SubmitLeaveRequest req)
    {
        int totalDays = (int)Math.Max(1, (req.EndDate.Date - req.StartDate.Date).TotalDays + 1);

        var leaveRequest = new LeaveRequest
        {
            EmployeeId = req.EmployeeId,
            LeaveTypeId = req.LeaveTypeId,
            StartDate = req.StartDate.Date,
            EndDate = req.EndDate.Date,
            TotalDays = totalDays,
            Reason = req.Reason,
            Status = "Pending"
        };

        // Update pending balance
        int year = req.StartDate.Year;
        var balance = await _db.LeaveBalanceLedgers.FirstOrDefaultAsync(b => b.EmployeeId == req.EmployeeId && b.LeaveTypeId == req.LeaveTypeId && b.Year == year);
        if (balance != null)
        {
            if (balance.RemainingDays < totalDays)
            {
                return BadRequest(ApiResponse<Guid>.Fail($"Insufficient leave balance. Remaining: {balance.RemainingDays} days, Requested: {totalDays} days."));
            }
            balance.PendingDays += totalDays;
        }

        _db.LeaveRequests.Add(leaveRequest);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<Guid>.Ok(leaveRequest.Id, "Leave request submitted successfully."));
    }

    [HttpPost("leaves/{id}/review")]
    public async Task<IActionResult> ReviewLeave(Guid id, [FromBody] ReviewLeaveRequest req)
    {
        var reviewerUserIdStr = User.FindFirstValue(ClaimConstants.UserId);
        if (!Guid.TryParse(reviewerUserIdStr, out var reviewerUserId)) return Unauthorized();

        var leaveRequest = await _db.LeaveRequests.Include(lr => lr.Employee).FirstOrDefaultAsync(lr => lr.Id == id);
        if (leaveRequest == null) return NotFound(ApiResponse<bool>.Fail("Leave request not found."));

        if (leaveRequest.Status != "Pending")
        {
            return BadRequest(ApiResponse<bool>.Fail($"Leave request is already in '{leaveRequest.Status}' status."));
        }

        // PREVENT SELF-APPROVAL RULE
        if (leaveRequest.Employee.IdentityUserId.HasValue && leaveRequest.Employee.IdentityUserId.Value == reviewerUserId)
        {
            return BadRequest(ApiResponse<bool>.Fail("Self-approval of leave requests is strictly prohibited by compliance controls."));
        }

        int year = leaveRequest.StartDate.Year;
        var balance = await _db.LeaveBalanceLedgers.FirstOrDefaultAsync(b => b.EmployeeId == leaveRequest.EmployeeId && b.LeaveTypeId == leaveRequest.LeaveTypeId && b.Year == year);

        if (req.Approved)
        {
            leaveRequest.Status = "Approved";
            if (balance != null)
            {
                balance.PendingDays = Math.Max(0, balance.PendingDays - leaveRequest.TotalDays);
                balance.UsedDays += leaveRequest.TotalDays;
            }
        }
        else
        {
            leaveRequest.Status = "Rejected";
            leaveRequest.RejectionReason = req.RejectionReason ?? "Rejected by manager";
            if (balance != null)
            {
                balance.PendingDays = Math.Max(0, balance.PendingDays - leaveRequest.TotalDays);
            }
        }

        leaveRequest.ReviewedByUserId = reviewerUserId;
        leaveRequest.ReviewedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<bool>.Ok(true, $"Leave request {(req.Approved ? "approved" : "rejected")} successfully."));
    }
}
