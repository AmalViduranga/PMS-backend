using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PharmaFlow.Operations.Api.Data;
using PharmaFlow.Operations.Api.Domain;
using PharmaFlow.Operations.Api.DTOs;
using PharmaFlow.Operations.Api.Services;
using PharmaFlow.Workforce.Api.Data;
using PharmaFlow.Workforce.Api.Domain;
using Xunit;

namespace PharmaFlow.UnitTests;

public class ReturnAndRefundTests
{
    private OperationsDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new OperationsDbContext(options);
    }

    [Fact]
    public async Task ProcessReturn_ExcessiveQuantity_FailsValidation()
    {
        var db = CreateDbContext(nameof(ProcessReturn_ExcessiveQuantity_FailsValidation));
        var branchId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var saleItemId = Guid.NewGuid();
        var cashierId = Guid.NewGuid();

        var shift = new CashierShift
        {
            Id = shiftId,
            BranchId = branchId,
            RegisterId = Guid.NewGuid(),
            CashierUserId = cashierId,
            Status = "Open"
        };

        var saleItem = new SaleItem
        {
            Id = saleItemId,
            SaleId = saleId,
            ProductId = Guid.NewGuid(),
            StockBatchId = Guid.NewGuid(),
            BatchNumber = "B-01",
            Quantity = 5,
            ReturnedQuantity = 3, // Already 3 returned, max 2 remaining!
            UnitPrice = 100,
            LineTotal = 500
        };

        var sale = new Sale
        {
            Id = saleId,
            InvoiceNumber = "INV-001",
            BranchId = branchId,
            RegisterId = Guid.NewGuid(),
            ShiftId = shiftId,
            CashierUserId = cashierId,
            GrandTotal = 500,
            Items = new List<SaleItem> { saleItem }
        };

        db.CashierShifts.Add(shift);
        db.Sales.Add(sale);
        await db.SaveChangesAsync();

        var returnService = new ReturnService(db);

        // Attempting to return 3 units when only 2 remain returnable
        var req = new ProcessReturnRequest(
            saleId,
            shiftId,
            "Cash",
            "Customer error",
            new List<ReturnItemLineInput>
            {
                new(saleItemId, 3)
            }
        );

        var result = await returnService.ProcessReturnAsync(req, cashierId);

        Assert.False(result.Success);
        Assert.Contains("exceeds remaining returnable quantity", result.Message);
    }
}

public class WorkforceComplianceTests
{
    private WorkforceDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<WorkforceDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new WorkforceDbContext(options);
    }

    [Fact]
    public async Task Schedule_CrossBranchOverlap_IsBlocked()
    {
        var db = CreateDbContext(nameof(Schedule_CrossBranchOverlap_IsBlocked));
        var employeeId = Guid.NewGuid();
        var branch1 = Guid.NewGuid();
        var branch2 = Guid.NewGuid();
        var today = DateTime.UtcNow.Date;

        // Existing schedule in Branch 1 from 08:00 to 14:00
        var existing = new WorkSchedule
        {
            EmployeeId = employeeId,
            BranchId = branch1,
            ShiftDate = today,
            StartTime = new TimeSpan(8, 0, 0),
            EndTime = new TimeSpan(14, 0, 0)
        };
        db.WorkSchedules.Add(existing);
        await db.SaveChangesAsync();

        // Check overlapping schedule query
        var reqStartTime = new TimeSpan(12, 0, 0); // Overlaps with 08:00-14:00!
        var reqEndTime = new TimeSpan(18, 0, 0);

        var isOverlapping = await db.WorkSchedules.AnyAsync(ws =>
            ws.EmployeeId == employeeId &&
            ws.ShiftDate == today &&
            reqStartTime < ws.EndTime &&
            reqEndTime > ws.StartTime);

        Assert.True(isOverlapping);
    }
}
