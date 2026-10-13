namespace PharmaFlow.Reporting.Api.Domain;

public class DailySalesProjection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Date { get; set; }
    public Guid BranchId { get; set; }
    public int TotalTransactions { get; set; }
    public decimal GrossSales { get; set; }
    public decimal DiscountsGiven { get; set; }
    public decimal NetSales => GrossSales - DiscountsGiven;
    public decimal TaxCollected { get; set; }
    public decimal CostOfGoodsSold { get; set; }
    public decimal GrossProfit => NetSales - CostOfGoodsSold; // Explicit cost basis
    public decimal CashCollected { get; set; }
    public decimal CashRefunds { get; set; }
    public DateTime LastUpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ShiftReconciliationProjection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShiftId { get; set; }
    public Guid BranchId { get; set; }
    public Guid CashierUserId { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public decimal OpeningFloat { get; set; }
    public decimal TotalCashSales { get; set; }
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
    public decimal CashRefunds { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? ActualCountedCash { get; set; }
    public decimal? Variance { get; set; }
    public string Status { get; set; } = "Open";
}

public class StockValuationProjection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime SnapshotDate { get; set; }
    public Guid BranchId { get; set; }
    public int TotalProductCount { get; set; }
    public int TotalUnitsOnHand { get; set; }
    public decimal TotalCostValuation { get; set; }
    public decimal TotalRetailValuation { get; set; }
    public int NearExpiryBatchCount { get; set; }
    public int ExpiredBatchCount { get; set; }
    public DateTime ComputedAtUtc { get; set; } = DateTime.UtcNow;
}

public class ReportingInboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime? ProcessedAtUtc { get; set; }
}
