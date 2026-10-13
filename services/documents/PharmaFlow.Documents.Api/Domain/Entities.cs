namespace PharmaFlow.Documents.Api.Domain;

public class DocumentMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OriginalFileName { get; set; } = string.Empty;
    public string StoredBlobName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Sha256Hash { get; set; } = string.Empty;
    public string Purpose { get; set; } = "Prescription"; // "Prescription", "SupplierInvoice", "EmployeeRecord"
    public Guid OwnerUserId { get; set; }
    public string ScanStatus { get; set; } = "Clean"; // "Pending", "Clean", "Quarantined", "Failed"
    public string ScanProvider { get; set; } = "DevSimulatedScanner";
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public int Version { get; set; } = 1;

    // Pharmacist prescription review status
    public string ReviewStatus { get; set; } = "PendingReview"; // "PendingReview", "Approved", "ClarificationRequested", "Rejected"
    public Guid? ReviewedByPharmacistId { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? PharmacistNotes { get; set; }

    public ICollection<DocumentAccessAudit> AccessAudits { get; set; } = new List<DocumentAccessAudit>();
    public ICollection<PrescriptionApprovedItem> ApprovedItems { get; set; } = new List<PrescriptionApprovedItem>();
}

public class PrescriptionApprovedItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public DocumentMetadata Document { get; set; } = null!;
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int MaxDispenseQuantity { get; set; }
    public int DispensedQuantity { get; set; } = 0;
}

public class DocumentAccessAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public DocumentMetadata Document { get; set; } = null!;
    public Guid AccessedByUserId { get; set; }
    public string AccessType { get; set; } = "View"; // "View", "Download", "Review"
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
}
