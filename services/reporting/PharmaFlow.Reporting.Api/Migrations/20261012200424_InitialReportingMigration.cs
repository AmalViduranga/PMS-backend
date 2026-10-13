using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaFlow.Reporting.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialReportingMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_sales_projections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalTransactions = table.Column<int>(type: "integer", nullable: false),
                    GrossSales = table.Column<decimal>(type: "numeric", nullable: false),
                    DiscountsGiven = table.Column<decimal>(type: "numeric", nullable: false),
                    TaxCollected = table.Column<decimal>(type: "numeric", nullable: false),
                    CostOfGoodsSold = table.Column<decimal>(type: "numeric", nullable: false),
                    CashCollected = table.Column<decimal>(type: "numeric", nullable: false),
                    CashRefunds = table.Column<decimal>(type: "numeric", nullable: false),
                    LastUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_sales_projections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reporting_inbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reporting_inbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "shift_reconciliation_projections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpeningFloat = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalCashSales = table.Column<decimal>(type: "numeric", nullable: false),
                    CashIn = table.Column<decimal>(type: "numeric", nullable: false),
                    CashOut = table.Column<decimal>(type: "numeric", nullable: false),
                    CashRefunds = table.Column<decimal>(type: "numeric", nullable: false),
                    ExpectedCash = table.Column<decimal>(type: "numeric", nullable: false),
                    ActualCountedCash = table.Column<decimal>(type: "numeric", nullable: true),
                    Variance = table.Column<decimal>(type: "numeric", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_reconciliation_projections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stock_valuation_projections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalProductCount = table.Column<int>(type: "integer", nullable: false),
                    TotalUnitsOnHand = table.Column<int>(type: "integer", nullable: false),
                    TotalCostValuation = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalRetailValuation = table.Column<decimal>(type: "numeric", nullable: false),
                    NearExpiryBatchCount = table.Column<int>(type: "integer", nullable: false),
                    ExpiredBatchCount = table.Column<int>(type: "integer", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_valuation_projections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_sales_projections_BranchId_Date",
                table: "daily_sales_projections",
                columns: new[] { "BranchId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shift_reconciliation_projections_ShiftId",
                table: "shift_reconciliation_projections",
                column: "ShiftId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_valuation_projections_BranchId_SnapshotDate",
                table: "stock_valuation_projections",
                columns: new[] { "BranchId", "SnapshotDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_sales_projections");

            migrationBuilder.DropTable(
                name: "reporting_inbox_messages");

            migrationBuilder.DropTable(
                name: "shift_reconciliation_projections");

            migrationBuilder.DropTable(
                name: "stock_valuation_projections");
        }
    }
}
