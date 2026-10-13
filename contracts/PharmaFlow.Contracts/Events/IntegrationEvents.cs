namespace PharmaFlow.Contracts.Events;

public interface IIntegrationEvent
{
    Guid EventId { get; }
    string EventType { get; }
    DateTime OccurredAtUtc { get; }
    string CorrelationId { get; }
    string? CausationId { get; }
    string AggregateId { get; }
    int EventVersion { get; }
}

public abstract record IntegrationEventBase(
    Guid EventId,
    string EventType,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string AggregateId,
    int EventVersion) : IIntegrationEvent;

public record ProductCatalogUpdatedEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string AggregateId,
    int EventVersion,
    Guid ProductId,
    string Sku,
    string BrandName,
    string GenericName,
    string CategoryName,
    decimal SellingPrice,
    bool IsPrescriptionRequired,
    bool IsPublishedOnline,
    bool IsOrderEligible,
    string BaseUnit) : IntegrationEventBase(EventId, nameof(ProductCatalogUpdatedEvent), OccurredAtUtc, CorrelationId, CausationId, AggregateId, EventVersion);

public record CustomerOrderSubmittedEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string AggregateId,
    int EventVersion,
    Guid OrderId,
    Guid CustomerId,
    Guid BranchId,
    string OrderReference,
    Guid? PrescriptionDocumentId,
    List<CustomerOrderItemDto> Items) : IntegrationEventBase(EventId, nameof(CustomerOrderSubmittedEvent), OccurredAtUtc, CorrelationId, CausationId, AggregateId, EventVersion);

public record CustomerOrderItemDto(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);

public record CustomerOrderAcceptedEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string AggregateId,
    int EventVersion,
    Guid OrderId,
    Guid BranchId,
    Guid StaffUserId) : IntegrationEventBase(EventId, nameof(CustomerOrderAcceptedEvent), OccurredAtUtc, CorrelationId, CausationId, AggregateId, EventVersion);

public record CustomerOrderReservedEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string AggregateId,
    int EventVersion,
    Guid OrderId,
    Guid BranchId) : IntegrationEventBase(EventId, nameof(CustomerOrderReservedEvent), OccurredAtUtc, CorrelationId, CausationId, AggregateId, EventVersion);

public record CustomerOrderPreparedEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string AggregateId,
    int EventVersion,
    Guid OrderId,
    Guid BranchId) : IntegrationEventBase(EventId, nameof(CustomerOrderPreparedEvent), OccurredAtUtc, CorrelationId, CausationId, AggregateId, EventVersion);

public record CustomerOrderCollectedEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string AggregateId,
    int EventVersion,
    Guid OrderId,
    Guid BranchId,
    Guid SaleId) : IntegrationEventBase(EventId, nameof(CustomerOrderCollectedEvent), OccurredAtUtc, CorrelationId, CausationId, AggregateId, EventVersion);

public record CustomerOrderCancelledEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string AggregateId,
    int EventVersion,
    Guid OrderId,
    Guid BranchId,
    string Reason) : IntegrationEventBase(EventId, nameof(CustomerOrderCancelledEvent), OccurredAtUtc, CorrelationId, CausationId, AggregateId, EventVersion);

public record PrescriptionReviewedEvent(
    Guid EventId,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string AggregateId,
    int EventVersion,
    Guid DocumentId,
    string ReviewStatus,
    Guid PharmacistUserId,
    List<PrescriptionApprovedItemDto> ApprovedItems) : IntegrationEventBase(EventId, nameof(PrescriptionReviewedEvent), OccurredAtUtc, CorrelationId, CausationId, AggregateId, EventVersion);

public record PrescriptionApprovedItemDto(
    Guid ProductId,
    int MaxDispenseQuantity);
