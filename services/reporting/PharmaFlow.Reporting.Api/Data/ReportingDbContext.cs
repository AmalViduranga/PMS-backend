using Microsoft.EntityFrameworkCore;
using PharmaFlow.Reporting.Api.Domain;

namespace PharmaFlow.Reporting.Api.Data;

public class ReportingDbContext : DbContext
{
    public ReportingDbContext(DbContextOptions<ReportingDbContext> options) : base(options) { }

    public DbSet<DailySalesProjection> DailySalesProjections => Set<DailySalesProjection>();
    public DbSet<ShiftReconciliationProjection> ShiftReconciliationProjections => Set<ShiftReconciliationProjection>();
    public DbSet<StockValuationProjection> StockValuationProjections => Set<StockValuationProjection>();
    public DbSet<ReportingInboxMessage> InboxMessages => Set<ReportingInboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DailySalesProjection>(b =>
        {
            b.ToTable("daily_sales_projections");
            b.HasKey(s => s.Id);
            b.HasIndex(s => new { s.BranchId, s.Date }).IsUnique();
        });

        modelBuilder.Entity<ShiftReconciliationProjection>(b =>
        {
            b.ToTable("shift_reconciliation_projections");
            b.HasKey(s => s.Id);
            b.HasIndex(s => s.ShiftId).IsUnique();
        });

        modelBuilder.Entity<StockValuationProjection>(b =>
        {
            b.ToTable("stock_valuation_projections");
            b.HasKey(s => s.Id);
            b.HasIndex(s => new { s.BranchId, s.SnapshotDate }).IsUnique();
        });

        modelBuilder.Entity<ReportingInboxMessage>(b =>
        {
            b.ToTable("reporting_inbox_messages");
            b.HasKey(m => m.Id);
        });
    }
}
