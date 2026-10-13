using Microsoft.EntityFrameworkCore;
using PharmaFlow.Operations.Api.Data;
using PharmaFlow.Operations.Api.Domain;
using PharmaFlow.Operations.Api.Services;
using Xunit;

namespace PharmaFlow.UnitTests;

public class FefoAllocationTests
{
    private OperationsDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new OperationsDbContext(options);
    }

    [Fact]
    public async Task AllocateFefo_AllocatesFromEarliestExpiringBatchFirst()
    {
        var db = CreateDbContext(nameof(AllocateFefo_AllocatesFromEarliestExpiringBatchFirst));
        var branchId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        // Batch 1: Expiring in 60 days, 10 units
        var batch1 = new StockBatch
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ProductId = productId,
            BatchNumber = "BATCH-EARLY",
            ExpiryDate = DateTime.UtcNow.Date.AddDays(60),
            QuantityOnHand = 10,
            QuantityReserved = 0,
            Status = "Active"
        };

        // Batch 2: Expiring in 120 days, 20 units
        var batch2 = new StockBatch
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ProductId = productId,
            BatchNumber = "BATCH-LATE",
            ExpiryDate = DateTime.UtcNow.Date.AddDays(120),
            QuantityOnHand = 20,
            QuantityReserved = 0,
            Status = "Active"
        };

        db.StockBatches.AddRange(batch1, batch2);
        await db.SaveChangesAsync();

        var inventoryService = new InventoryService(db);

        // Request 15 units -> Should take all 10 from BATCH-EARLY, and 5 from BATCH-LATE
        var allocations = await inventoryService.AllocateFefoAsync(branchId, productId, 15);

        Assert.Equal(2, allocations.Count);
        Assert.Equal("BATCH-EARLY", allocations[0].Batch.BatchNumber);
        Assert.Equal(10, allocations[0].AllocatedQuantity);

        Assert.Equal("BATCH-LATE", allocations[1].Batch.BatchNumber);
        Assert.Equal(5, allocations[1].AllocatedQuantity);

        // Batch 1 should now have 0 remaining, Batch 2 should have 15 remaining
        Assert.Equal(0, batch1.QuantityOnHand);
        Assert.Equal(15, batch2.QuantityOnHand);
    }

    [Fact]
    public async Task AllocateFefo_BlocksExpiredAndQuarantinedStock()
    {
        var db = CreateDbContext(nameof(AllocateFefo_BlocksExpiredAndQuarantinedStock));
        var branchId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        // Expired batch (yesterday)
        var expiredBatch = new StockBatch
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ProductId = productId,
            BatchNumber = "BATCH-EXPIRED",
            ExpiryDate = DateTime.UtcNow.Date.AddDays(-1),
            QuantityOnHand = 50,
            QuantityReserved = 0,
            Status = "Active"
        };

        // Quarantined batch (future date, but quarantined)
        var quarantinedBatch = new StockBatch
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            ProductId = productId,
            BatchNumber = "BATCH-QUARANTINED",
            ExpiryDate = DateTime.UtcNow.Date.AddDays(90),
            QuantityOnHand = 50,
            QuantityReserved = 0,
            Status = "Quarantined"
        };

        db.StockBatches.AddRange(expiredBatch, quarantinedBatch);
        await db.SaveChangesAsync();

        var inventoryService = new InventoryService(db);

        // Requesting 5 units should fail because no saleable stock is available
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inventoryService.AllocateFefoAsync(branchId, productId, 5));

        Assert.Contains("Insufficient saleable stock", ex.Message);
    }
}
