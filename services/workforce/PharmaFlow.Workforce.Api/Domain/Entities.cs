namespace PharmaFlow.Workforce.Api.Domain;

public class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? IdentityUserId { get; set; } // Linked login identity (if user has portal login)
    public string EmployeeCode { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string FullName => $"{FirstName} {LastName}".Trim();
    public string Designation { get; set; } = string.Empty; // e.g. "Pharmacist", "Inventory Clerk", "Cashier"
    public string Department { get; set; } = "Pharmacy";
    public Guid PrimaryBranchId { get; set; }
    public string ContactNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? EmergencyContact { get; set; }
    public string EmploymentStatus { get; set; } = "Active"; // "Active", "OnLeave", "Terminated"
    public DateTime JoinedDate { get; set; } = DateTime.UtcNow.Date;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    public ICollection<WorkSchedule> WorkSchedules { get; set; } = new List<WorkSchedule>();
    public ICollection<LeaveRequest> LeaveRequests { get; set; } = new List<LeaveRequest>();
    public ICollection<LeaveBalanceLedger> LeaveBalances { get; set; } = new List<LeaveBalanceLedger>();
}

public class AttendanceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid BranchId { get; set; }
    public DateTime Date { get; set; }
    public DateTime ClockInUtc { get; set; }
    public DateTime? ClockOutUtc { get; set; }
    public int TotalBreakMinutes { get; set; } = 0;
    public string Status { get; set; } = "Present"; // "Present", "Late", "HalfDay"
    public string? ManagerCorrectionReason { get; set; }
    public Guid? CorrectedByUserId { get; set; }
    public DateTime? CorrectedAtUtc { get; set; }
}

public class WorkSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid BranchId { get; set; }
    public DateTime ShiftDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class LeaveType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Annual"; // "Annual", "Casual", "Medical"
    public int AnnualAllowanceDays { get; set; } = 14;
    public bool IsActive { get; set; } = true;
}

public class LeaveBalanceLedger
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;
    public int Year { get; set; }
    public int TotalAllocated { get; set; }
    public int UsedDays { get; set; } = 0;
    public int PendingDays { get; set; } = 0;
    public int RemainingDays => TotalAllocated - UsedDays - PendingDays;
}

public class LeaveRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public Guid LeaveTypeId { get; set; }
    public LeaveType LeaveType { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int TotalDays { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // "Pending", "Approved", "Rejected", "Cancelled"
    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? RejectionReason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
