namespace PharmaFlow.Commerce.Api.Domain;

public class CustomerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IdentityUserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;
    public Guid? PreferredBranchId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<CustomerOrder> Orders { get; set; } = new List<CustomerOrder>();
}

public class ProductCatalogProjection
{
    public Guid Id { get; set; } // ProductId from Operations
    public string Sku { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public string GenericName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal SellingPrice { get; set; }
    public bool IsPrescriptionRequired { get; set; }
    public bool IsPublishedOnline { get; set; }
    public bool IsOrderEligible { get; set; }
    public string BaseUnit { get; set; } = "Tablet";
    public DateTime LastSynchronizedAtUtc { get; set; } = DateTime.UtcNow;
}

public class CustomerOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OrderReference { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public CustomerProfile Customer { get; set; } = null!;
    public Guid BranchId { get; set; }
    public string Status { get; set; } = "PendingStaffReview"; // "PendingStaffReview", "Accepted", "Reserved", "Preparing", "ReadyForCollection", "Collected", "Cancelled"
    public decimal TotalAmount { get; set; }
    public Guid? PrescriptionDocumentId { get; set; }
    public string? Notes { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? CollectedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public ICollection<CustomerOrderItem> Items { get; set; } = new List<CustomerOrderItem>();
}

public class CustomerOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public CustomerOrder Order { get; set; } = null!;
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;
}

public class CommerceOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime? ProcessedAtUtc { get; set; }
    public string? Error { get; set; }
}
