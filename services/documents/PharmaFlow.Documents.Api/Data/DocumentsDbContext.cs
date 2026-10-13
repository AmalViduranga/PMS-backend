using Microsoft.EntityFrameworkCore;
using PharmaFlow.Documents.Api.Domain;

namespace PharmaFlow.Documents.Api.Data;

public class DocumentsDbContext : DbContext
{
    public DocumentsDbContext(DbContextOptions<DocumentsDbContext> options) : base(options) { }

    public DbSet<DocumentMetadata> Documents => Set<DocumentMetadata>();
    public DbSet<PrescriptionApprovedItem> PrescriptionApprovedItems => Set<PrescriptionApprovedItem>();
    public DbSet<DocumentAccessAudit> DocumentAccessAudits => Set<DocumentAccessAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DocumentMetadata>(b =>
        {
            b.ToTable("documents");
            b.HasKey(d => d.Id);
            b.Property(d => d.OriginalFileName).HasMaxLength(256).IsRequired();
            b.Property(d => d.StoredBlobName).HasMaxLength(256).IsRequired();
            b.Property(d => d.Sha256Hash).HasMaxLength(128).IsRequired();
            b.Property(d => d.ScanStatus).HasMaxLength(32).IsRequired();
            b.HasIndex(d => d.OwnerUserId);
        });

        modelBuilder.Entity<PrescriptionApprovedItem>(b =>
        {
            b.ToTable("prescription_approved_items");
            b.HasKey(p => p.Id);
            b.HasOne(p => p.Document).WithMany(d => d.ApprovedItems).HasForeignKey(p => p.DocumentId);
        });

        modelBuilder.Entity<DocumentAccessAudit>(b =>
        {
            b.ToTable("document_access_audits");
            b.HasKey(a => a.Id);
            b.HasOne(a => a.Document).WithMany(d => d.AccessAudits).HasForeignKey(a => a.DocumentId);
        });
    }
}
