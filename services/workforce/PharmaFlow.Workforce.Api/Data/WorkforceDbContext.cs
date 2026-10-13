using Microsoft.EntityFrameworkCore;
using PharmaFlow.Workforce.Api.Domain;

namespace PharmaFlow.Workforce.Api.Data;

public class WorkforceDbContext : DbContext
{
    public WorkforceDbContext(DbContextOptions<WorkforceDbContext> options) : base(options) { }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<WorkSchedule> WorkSchedules => Set<WorkSchedule>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveBalanceLedger> LeaveBalanceLedgers => Set<LeaveBalanceLedger>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Employee>(b =>
        {
            b.ToTable("employees");
            b.HasKey(e => e.Id);
            b.Property(e => e.EmployeeCode).HasMaxLength(32).IsRequired();
            b.HasIndex(e => e.EmployeeCode).IsUnique();
            b.Property(e => e.FirstName).HasMaxLength(128).IsRequired();
            b.Property(e => e.LastName).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<AttendanceRecord>(b =>
        {
            b.ToTable("attendance_records");
            b.HasKey(a => a.Id);
            b.HasOne(a => a.Employee).WithMany(e => e.AttendanceRecords).HasForeignKey(a => a.EmployeeId);
            b.HasIndex(a => new { a.EmployeeId, a.Date }).IsUnique();
        });

        modelBuilder.Entity<WorkSchedule>(b =>
        {
            b.ToTable("work_schedules");
            b.HasKey(ws => ws.Id);
            b.HasOne(ws => ws.Employee).WithMany(e => e.WorkSchedules).HasForeignKey(ws => ws.EmployeeId);
            b.HasIndex(ws => new { ws.EmployeeId, ws.ShiftDate }).IsUnique();
        });

        modelBuilder.Entity<LeaveType>(b =>
        {
            b.ToTable("leave_types");
            b.HasKey(lt => lt.Id);
            b.Property(lt => lt.Name).HasMaxLength(64).IsRequired();
        });

        modelBuilder.Entity<LeaveBalanceLedger>(b =>
        {
            b.ToTable("leave_balance_ledgers");
            b.HasKey(lbl => lbl.Id);
            b.HasOne(lbl => lbl.Employee).WithMany(e => e.LeaveBalances).HasForeignKey(lbl => lbl.EmployeeId);
            b.HasOne(lbl => lbl.LeaveType).WithMany().HasForeignKey(lbl => lbl.LeaveTypeId);
            b.HasIndex(lbl => new { lbl.EmployeeId, lbl.LeaveTypeId, lbl.Year }).IsUnique();
        });

        modelBuilder.Entity<LeaveRequest>(b =>
        {
            b.ToTable("leave_requests");
            b.HasKey(lr => lr.Id);
            b.HasOne(lr => lr.Employee).WithMany(e => e.LeaveRequests).HasForeignKey(lr => lr.EmployeeId);
            b.HasOne(lr => lr.LeaveType).WithMany().HasForeignKey(lr => lr.LeaveTypeId);
        });
    }
}
