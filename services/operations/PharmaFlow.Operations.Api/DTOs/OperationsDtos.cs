namespace PharmaFlow.Operations.Api.DTOs;

public record BranchDto(
    Guid Id,
    string Code,
    string Name,
    string Address,
    string Phone,
    string Email,
    string Timezone,
    string Currency,
    bool IsActive);

public record CreateBranchRequest(
    string Code,
    string Name,
    string Address,
    string Phone,
    string Email,
    string? Timezone,
    string? Currency);

public record RegisterDto(
    Guid Id,
    Guid BranchId,
    string Code,
    string Name,
    string? HardwareIdentifier,
    bool IsActive);

public record CreateRegisterRequest(
    Guid BranchId,
    string Code,
    string Name,
    string? HardwareIdentifier);

public record CategoryDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? ParentCategoryId,
    bool IsActive);

public record CreateCategoryRequest(
    string Name,
    string? Description,
    Guid? ParentCategoryId);

public record ProductDto(
    Guid Id,
    string Sku,
    string BrandName,
    string GenericName,
    string Strength,
    string DosageForm,
    Guid CategoryId,
    string CategoryName,
    string StorageCondition,
    bool IsPrescriptionRequired,
    bool IsPublishedOnline,
    bool IsOrderEligible,
    string BaseUnit,
    decimal SellingPrice,
    decimal CostPrice,
    decimal TaxRatePercentage,
    int ReorderThreshold,
    bool IsActive);

public record ProductDetailDto(
    Guid Id,
    string Sku,
    string BrandName,
    string GenericName,
    string Strength,
    string DosageForm,
    Guid CategoryId,
    string CategoryName,
    string StorageCondition,
    bool IsPrescriptionRequired,
    bool IsPublishedOnline,
    bool IsOrderEligible,
    string BaseUnit,
    decimal SellingPrice,
    decimal CostPrice,
    decimal TaxRatePercentage,
    int ReorderThreshold,
    bool IsActive,
    List<string> Barcodes,
    List<UnitConversionDto> UnitConversions,
    int TotalStockOnHand);

public record UnitConversionDto(
    Guid Id,
    string PackUnitName,
    int MultiplierToBaseUnit,
    decimal? PackSellingPrice);

public record CreateProductRequest(
    string Sku,
    string BrandName,
    string GenericName,
    string Strength,
    string DosageForm,
    Guid CategoryId,
    string StorageCondition,
    bool IsPrescriptionRequired,
    bool IsPublishedOnline,
    bool IsOrderEligible,
    string BaseUnit,
    decimal SellingPrice,
    decimal CostPrice,
    decimal TaxRatePercentage,
    int ReorderThreshold,
    List<string>? Barcodes,
    List<UnitConversionInput>? UnitConversions);

public record UnitConversionInput(
    string PackUnitName,
    int MultiplierToBaseUnit,
    decimal? PackSellingPrice);

public record UpdateProductPriceRequest(
    decimal NewCostPrice,
    decimal NewSellingPrice,
    decimal TaxRatePercentage);

// Shift DTOs
public record CashierShiftDto(
    Guid Id,
    Guid BranchId,
    string BranchName,
    Guid RegisterId,
    string RegisterName,
    Guid CashierUserId,
    DateTime OpenedAtUtc,
    DateTime? ClosedAtUtc,
    decimal OpeningFloat,
    decimal CashSalesTotal,
    decimal CashRefundsTotal,
    decimal CashInTotal,
    decimal CashOutTotal,
    decimal ExpectedCash,
    decimal? ClosingCountedCash,
    decimal? Variance,
    string Status,
    string? Notes);

public record OpenShiftRequest(
    Guid BranchId,
    Guid RegisterId,
    decimal OpeningFloat,
    string? Notes);

public record CloseShiftRequest(
    decimal ClosingCountedCash,
    string? Notes);

public record CashMovementRequest(
    string MovementType,
    decimal Amount,
    string Reason);

// POS & Sales DTOs
public record CheckoutSaleRequest(
    Guid BranchId,
    Guid RegisterId,
    Guid ShiftId,
    string? CustomerName,
    List<CheckoutLineItemRequest> Items,
    decimal TenderedAmount,
    string PaymentMethod,
    string? Notes,
    string? IdempotencyKey);

public record CheckoutLineItemRequest(
    Guid ProductId,
    int Quantity, // in base units or pack
    string? PackUnitName,
    decimal UnitPrice,
    decimal DiscountAmount);

public record SaleDto(
    Guid Id,
    string InvoiceNumber,
    Guid BranchId,
    Guid RegisterId,
    Guid ShiftId,
    DateTime CreatedAtUtc,
    string? CustomerName,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal TaxTotal,
    decimal GrandTotal,
    decimal TenderedAmount,
    decimal ChangeGiven,
    string PaymentMethod,
    string Status,
    List<SaleItemDto> Items);

public record SaleItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string BatchNumber,
    DateTime ExpiryDate,
    int Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal LineTotal,
    int RemainingReturnableQuantity);

// Supplier & Purchasing DTOs
public record SupplierDto(
    Guid Id,
    string Code,
    string Name,
    string ContactPerson,
    string Email,
    string Phone,
    string Address,
    int PaymentTermsDays,
    bool IsActive);

public record CreateSupplierRequest(
    string Code,
    string Name,
    string ContactPerson,
    string Email,
    string Phone,
    string Address,
    int PaymentTermsDays);

public record PurchaseOrderDto(
    Guid Id,
    string PoNumber,
    Guid SupplierId,
    string SupplierName,
    Guid BranchId,
    string BranchName,
    string Status,
    decimal TotalCost,
    DateTime CreatedAtUtc,
    List<PurchaseOrderItemDto> Items);

public record PurchaseOrderItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    int OrderedQuantity,
    decimal UnitCost,
    int ReceivedQuantity,
    int BonusQuantity);

public record CreatePurchaseOrderRequest(
    Guid SupplierId,
    Guid BranchId,
    List<PoItemInput> Items);

public record PoItemInput(
    Guid ProductId,
    int OrderedQuantity,
    decimal UnitCost,
    decimal TaxRate);

public record ReceiveGoodsReceiptRequest(
    string SupplierInvoiceNumber,
    List<ReceiveGrnItemInput> Items,
    string? Notes);

public record ReceiveGrnItemInput(
    Guid PurchaseOrderItemId,
    Guid ProductId,
    string BatchNumber,
    DateTime ExpiryDate,
    int ReceivedQuantity,
    int BonusQuantity,
    int MultiplierToBaseUnit,
    decimal UnitAcquisitionCost);

// Inventory & Batches DTOs
public record StockBatchDto(
    Guid Id,
    Guid BranchId,
    Guid ProductId,
    string ProductName,
    string Sku,
    string BatchNumber,
    DateTime ExpiryDate,
    int QuantityOnHand,
    int QuantityReserved,
    int QuantityAvailable,
    decimal UnitCost,
    decimal SellingPrice,
    string Status,
    bool IsSaleable);

public record StockAdjustmentRequest(
    Guid BranchId,
    Guid ProductId,
    Guid StockBatchId,
    string AdjustmentType,
    int QuantityDelta,
    string Reason);

// Return DTOs
public record ProcessReturnRequest(
    Guid OriginalSaleId,
    Guid ShiftId,
    string RefundMethod,
    string Reason,
    List<ReturnItemLineInput> Items);

public record ReturnItemLineInput(
    Guid OriginalSaleItemId,
    int ReturnedQuantity);

public record CustomerReturnDto(
    Guid Id,
    string ReturnNumber,
    Guid OriginalSaleId,
    string InvoiceNumber,
    decimal RefundTotal,
    string RefundMethod,
    string Reason,
    DateTime CreatedAtUtc);
