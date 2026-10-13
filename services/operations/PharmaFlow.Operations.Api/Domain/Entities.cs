namespace PharmaFlow.Operations.Api.Domain;

// -------------------------------------------------------------
// 1. Organization, Branch & Register Hierarchy
// -------------------------------------------------------------
public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "PharmaFlow Healthcare";
    public string DefaultCurrency { get; set; } = "LKR";
    public string DefaultTimezone { get; set; } = "Asia/Colombo";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
}

public class Branch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Timezone { get; set; } = "Asia/Colombo";
    public string Currency { get; set; } = "LKR";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Register> Registers { get; set; } = new List<Register>();
    public ICollection<StockBatch> StockBatches { get; set; } = new List<StockBatch>();
}

public class Register
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? HardwareIdentifier { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<CashierShift> CashierShifts { get; set; } = new List<CashierShift>();
}

// -------------------------------------------------------------
// 2. Cashier Shifts & Cash Ledger
// -------------------------------------------------------------
public class CashierShift
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public Guid RegisterId { get; set; }
    public Register Register { get; set; } = null!;
    public Guid CashierUserId { get; set; }
    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
    public decimal OpeningFloat { get; set; }
    public decimal CashSalesTotal { get; set; }
    public decimal CashRefundsTotal { get; set; }
    public decimal CashInTotal { get; set; }
    public decimal CashOutTotal { get; set; }
    public decimal ExpectedCash => OpeningFloat + CashSalesTotal + CashInTotal - CashRefundsTotal - CashOutTotal;
    public decimal? ClosingCountedCash { get; set; }
    public decimal? Variance => ClosingCountedCash.HasValue ? ClosingCountedCash.Value - ExpectedCash : null;
    public string Status { get; set; } = "Open"; // "Open", "Closed"
    public string? Notes { get; set; }

    public ICollection<CashMovement> CashMovements { get; set; } = new List<CashMovement>();
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
}

public class CashMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShiftId { get; set; }
    public CashierShift Shift { get; set; } = null!;
    public string MovementType { get; set; } = "CashIn"; // "CashIn", "CashOut", "FloatAdjustment"
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid PerformedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// -------------------------------------------------------------
// 3. Catalogue, Pricing & Pack Conversions
// -------------------------------------------------------------
public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Sku { get; set; } = string.Empty;
    public string BrandName { get; set; } = string.Empty;
    public string GenericName { get; set; } = string.Empty;
    public string Strength { get; set; } = string.Empty;
    public string DosageForm { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string StorageCondition { get; set; } = "RoomTemperature"; // "RoomTemperature", "Refrigerated2to8C", "Frozen"
    public bool IsPrescriptionRequired { get; set; } = false;
    public bool IsPublishedOnline { get; set; } = true;
    public bool IsOrderEligible { get; set; } = true;
    public string BaseUnit { get; set; } = "Tablet"; // "Tablet", "Capsule", "Bottle", "Vial", "Ampoule", "Tube"
    public decimal SellingPrice { get; set; }
    public decimal CostPrice { get; set; }
    public decimal TaxRatePercentage { get; set; } = 0.0m;
    public int ReorderThreshold { get; set; } = 10;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public ICollection<ProductBarcode> Barcodes { get; set; } = new List<ProductBarcode>();
    public ICollection<UnitConversion> UnitConversions { get; set; } = new List<UnitConversion>();
    public ICollection<PriceHistory> PriceHistories { get; set; } = new List<PriceHistory>();
    public ICollection<StockBatch> StockBatches { get; set; } = new List<StockBatch>();
}

public class ProductBarcode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Barcode { get; set; } = string.Empty;
    public bool IsPrimary { get; set; } = false;
}

public class UnitConversion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string PackUnitName { get; set; } = "Box"; // e.g. "Strip", "Box", "Pack"
    public int MultiplierToBaseUnit { get; set; } = 10; // e.g. 1 Strip = 10 Tablets
    public decimal? PackSellingPrice { get; set; }
}

public class PriceHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal TaxRatePercentage { get; set; }
    public DateTime EffectiveFromUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EffectiveToUtc { get; set; }
    public Guid ChangedByUserId { get; set; }
}

// -------------------------------------------------------------
// 4. Suppliers & Purchasing Lifecycle
// -------------------------------------------------------------
public class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ContactPerson { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? TaxNumber { get; set; }
    public int PaymentTermsDays { get; set; } = 30;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
}

public class PurchaseOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PoNumber { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public string Status { get; set; } = "Draft"; // "Draft", "Submitted", "Approved", "Ordered", "PartiallyReceived", "Received", "Cancelled"
    public decimal TotalCost { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? OrderedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
    public ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();
}

public class PurchaseOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int OrderedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TaxRate { get; set; }
    public int ReceivedQuantity { get; set; }
    public int BonusQuantity { get; set; }
}

public class GoodsReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GrnNumber { get; set; } = string.Empty;
    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public string SupplierInvoiceNumber { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid ReceivedByUserId { get; set; }
    public decimal TotalAcquisitionCost { get; set; }
    public string? Notes { get; set; }

    public ICollection<GoodsReceiptItem> Items { get; set; } = new List<GoodsReceiptItem>();
}

public class GoodsReceiptItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GoodsReceiptId { get; set; }
    public GoodsReceipt GoodsReceipt { get; set; } = null!;
    public Guid PurchaseOrderItemId { get; set; }
    public PurchaseOrderItem PurchaseOrderItem { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public int ReceivedQuantity { get; set; }
    public int BonusQuantity { get; set; }
    public int MultiplierToBaseUnit { get; set; } = 1;
    public decimal UnitAcquisitionCost { get; set; }
}

// -------------------------------------------------------------
// 5. Batch Inventory (FEFO Allocation & Movement Ledger)
// -------------------------------------------------------------
public class StockBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public Branch Branch { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public int QuantityOnHand { get; set; } // in base unit
    public int QuantityReserved { get; set; } // in base unit
    public int QuantityAvailable => Math.Max(0, QuantityOnHand - QuantityReserved);
    public decimal UnitCost { get; set; }
    public decimal SellingPrice { get; set; }
    public string Status { get; set; } = "Active"; // "Active", "Quarantined", "Recalled", "Expired"
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsSaleable => Status == "Active" && ExpiryDate > DateTime.UtcNow.Date && QuantityAvailable > 0;
}

public class StockMovementLedger
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public Guid StockBatchId { get; set; }
    public StockBatch StockBatch { get; set; } = null!;
    public string MovementType { get; set; } = "Sale"; // "GoodsReceipt", "Sale", "SaleReturn", "Adjustment", "TransferIn", "TransferOut", "Quarantine"
    public int QuantityDelta { get; set; } // + or - in base units
    public int RemainingQuantity { get; set; }
    public string ReferenceDocumentType { get; set; } = string.Empty; // "Sale", "GoodsReceipt", "CustomerReturn", "Adjustment"
    public Guid ReferenceDocumentId { get; set; }
    public string? Reason { get; set; }
    public Guid PerformedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class StockAdjustment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public Guid StockBatchId { get; set; }
    public StockBatch StockBatch { get; set; } = null!;
    public string AdjustmentType { get; set; } = "QuantityIncrease"; // "QuantityIncrease", "QuantityDecrease", "Scrap", "Damage"
    public int QuantityDelta { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid ApprovedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class StockTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TransferNumber { get; set; } = string.Empty;
    public Guid SourceBranchId { get; set; }
    public Guid TargetBranchId { get; set; }
    public string Status { get; set; } = "Pending"; // "Pending", "InTransit", "Completed", "Cancelled"
    public DateTime? DispatchedAtUtc { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public Guid DispatchedByUserId { get; set; }
    public Guid? ReceivedByUserId { get; set; }

    public ICollection<StockTransferItem> Items { get; set; } = new List<StockTransferItem>();
}

public class StockTransferItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StockTransferId { get; set; }
    public StockTransfer StockTransfer { get; set; } = null!;
    public Guid ProductId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public DateTime ExpiryDate { get; set; }
    public int QuantityDispatched { get; set; }
    public int QuantityReceived { get; set; }
}

// -------------------------------------------------------------
// 6. POS Sales, Receipts, and Returns
// -------------------------------------------------------------
public class Sale
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public Guid RegisterId { get; set; }
    public Guid ShiftId { get; set; }
    public CashierShift Shift { get; set; } = null!;
    public Guid CashierUserId { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal TenderedAmount { get; set; }
    public decimal ChangeGiven { get; set; }
    public string PaymentMethod { get; set; } = "Cash"; // "Cash", "CardManual", "Split"
    public string Status { get; set; } = "Posted"; // "Posted", "Reversed"
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public string? IdempotencyKey { get; set; }

    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    public ICollection<CustomerReturn> Returns { get; set; } = new List<CustomerReturn>();
}

public class SaleItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleId { get; set; }
    public Sale Sale { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid StockBatchId { get; set; }
    public StockBatch StockBatch { get; set; } = null!;
    public string BatchNumber { get; set; } = string.Empty;
    public int Quantity { get; set; } // in base unit
    public decimal UnitPrice { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public int BaseUnitMultiplier { get; set; } = 1;
    public int ReturnedQuantity { get; set; } = 0;

    public int RemainingReturnableQuantity => Math.Max(0, Quantity - ReturnedQuantity);
}

public class CustomerReturn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ReturnNumber { get; set; } = string.Empty;
    public Guid OriginalSaleId { get; set; }
    public Sale OriginalSale { get; set; } = null!;
    public Guid BranchId { get; set; }
    public Guid ShiftId { get; set; }
    public Guid CashierUserId { get; set; }
    public string RefundMethod { get; set; } = "Cash"; // "Cash", "CreditNote"
    public decimal RefundTotal { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<CustomerReturnItem> Items { get; set; } = new List<CustomerReturnItem>();
}

public class CustomerReturnItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerReturnId { get; set; }
    public CustomerReturn CustomerReturn { get; set; } = null!;
    public Guid OriginalSaleItemId { get; set; }
    public SaleItem OriginalSaleItem { get; set; } = null!;
    public Guid ProductId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;
    public int ReturnedQuantity { get; set; }
    public decimal RefundUnitPrice { get; set; }
    public decimal LineRefundTotal { get; set; }
    public string DispositionStatus { get; set; } = "Quarantined"; // "Quarantined", "RestockedToSaleable", "Scrapped"
    public string? DispositionNotes { get; set; }
    public Guid? DispositionByUserId { get; set; }
    public DateTime? DispositionAtUtc { get; set; }
}

// -------------------------------------------------------------
// 7. Prescription Review & Dispensing Authorizations
// -------------------------------------------------------------
public class DispensingAuthorization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PrescriptionDocumentId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int MaxAllowedQuantity { get; set; }
    public int DispensedQuantity { get; set; } = 0;
    public string Status { get; set; } = "Active"; // "Active", "FullyDispensed", "Revoked"
    public Guid AuthorizedByPharmacistId { get; set; }
    public DateTime AuthorizedAtUtc { get; set; } = DateTime.UtcNow;

    public int RemainingDispenseQuantity => Math.Max(0, MaxAllowedQuantity - DispensedQuantity);

    public ICollection<DispensingRecord> DispensingRecords { get; set; } = new List<DispensingRecord>();
}

public class DispensingRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuthorizationId { get; set; }
    public DispensingAuthorization Authorization { get; set; } = null!;
    public Guid SaleId { get; set; }
    public Guid SaleItemId { get; set; }
    public int QuantityDispensed { get; set; }
    public DateTime DispensedAtUtc { get; set; } = DateTime.UtcNow;
    public Guid PharmacistId { get; set; }
}

// -------------------------------------------------------------
// 8. Outbox & Inbox Messaging Infrastructure
// -------------------------------------------------------------
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime? ProcessedAtUtc { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; } = 0;
}

public class InboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime? ProcessedAtUtc { get; set; }
    public string? Error { get; set; }
}
