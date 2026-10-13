using Microsoft.EntityFrameworkCore;
using PharmaFlow.Operations.Api.Domain;

namespace PharmaFlow.Operations.Api.Data;

public class OperationsDbContext : DbContext
{
    public OperationsDbContext(DbContextOptions<OperationsDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Register> Registers => Set<Register>();
    public DbSet<CashierShift> CashierShifts => Set<CashierShift>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductBarcode> ProductBarcodes => Set<ProductBarcode>();
    public DbSet<UnitConversion> UnitConversions => Set<UnitConversion>();
    public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptItem> GoodsReceiptItems => Set<GoodsReceiptItem>();

    public DbSet<StockBatch> StockBatches => Set<StockBatch>();
    public DbSet<StockMovementLedger> StockMovementLedgers => Set<StockMovementLedger>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferItem> StockTransferItems => Set<StockTransferItem>();

    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<CustomerReturn> CustomerReturns => Set<CustomerReturn>();
    public DbSet<CustomerReturnItem> CustomerReturnItems => Set<CustomerReturnItem>();

    public DbSet<DispensingAuthorization> DispensingAuthorizations => Set<DispensingAuthorization>();
    public DbSet<DispensingRecord> DispensingRecords => Set<DispensingRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Branch & Register
        modelBuilder.Entity<Organization>(b =>
        {
            b.ToTable("organizations");
            b.HasKey(o => o.Id);
        });

        modelBuilder.Entity<Branch>(b =>
        {
            b.ToTable("branches");
            b.HasKey(br => br.Id);
            b.Property(br => br.Code).HasMaxLength(32).IsRequired();
            b.HasIndex(br => br.Code).IsUnique();
            b.Property(br => br.Name).HasMaxLength(128).IsRequired();
            b.HasOne(br => br.Organization).WithMany(o => o.Branches).HasForeignKey(br => br.OrganizationId);
        });

        modelBuilder.Entity<Register>(b =>
        {
            b.ToTable("registers");
            b.HasKey(r => r.Id);
            b.Property(r => r.Code).HasMaxLength(32).IsRequired();
            b.HasIndex(r => new { r.BranchId, r.Code }).IsUnique();
            b.HasOne(r => r.Branch).WithMany(b => b.Registers).HasForeignKey(r => r.BranchId);
        });

        // Cashier Shift & Cash Movement
        modelBuilder.Entity<CashierShift>(b =>
        {
            b.ToTable("cashier_shifts");
            b.HasKey(cs => cs.Id);
            b.HasOne(cs => cs.Branch).WithMany().HasForeignKey(cs => cs.BranchId);
            b.HasOne(cs => cs.Register).WithMany(r => r.CashierShifts).HasForeignKey(cs => cs.RegisterId);
            b.Property(cs => cs.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<CashMovement>(b =>
        {
            b.ToTable("cash_movements");
            b.HasKey(cm => cm.Id);
            b.HasOne(cm => cm.Shift).WithMany(s => s.CashMovements).HasForeignKey(cm => cm.ShiftId);
            b.Property(cm => cm.MovementType).HasMaxLength(32).IsRequired();
        });

        // Category & Product
        modelBuilder.Entity<Category>(b =>
        {
            b.ToTable("categories");
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(128).IsRequired();
            b.HasOne(c => c.ParentCategory).WithMany().HasForeignKey(c => c.ParentCategoryId);
        });

        modelBuilder.Entity<Product>(b =>
        {
            b.ToTable("products");
            b.HasKey(p => p.Id);
            b.Property(p => p.Sku).HasMaxLength(64).IsRequired();
            b.HasIndex(p => p.Sku).IsUnique();
            b.Property(p => p.BrandName).HasMaxLength(128).IsRequired();
            b.Property(p => p.GenericName).HasMaxLength(128).IsRequired();
            b.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId);
        });

        modelBuilder.Entity<ProductBarcode>(b =>
        {
            b.ToTable("product_barcodes");
            b.HasKey(pb => pb.Id);
            b.Property(pb => pb.Barcode).HasMaxLength(64).IsRequired();
            b.HasIndex(pb => pb.Barcode).IsUnique();
            b.HasOne(pb => pb.Product).WithMany(p => p.Barcodes).HasForeignKey(pb => pb.ProductId);
        });

        modelBuilder.Entity<UnitConversion>(b =>
        {
            b.ToTable("unit_conversions");
            b.HasKey(uc => uc.Id);
            b.Property(uc => uc.PackUnitName).HasMaxLength(64).IsRequired();
            b.HasOne(uc => uc.Product).WithMany(p => p.UnitConversions).HasForeignKey(uc => uc.ProductId);
        });

        modelBuilder.Entity<PriceHistory>(b =>
        {
            b.ToTable("price_histories");
            b.HasKey(ph => ph.Id);
            b.HasOne(ph => ph.Product).WithMany(p => p.PriceHistories).HasForeignKey(ph => ph.ProductId);
        });

        // Suppliers & Purchasing
        modelBuilder.Entity<Supplier>(b =>
        {
            b.ToTable("suppliers");
            b.HasKey(s => s.Id);
            b.Property(s => s.Code).HasMaxLength(32).IsRequired();
            b.HasIndex(s => s.Code).IsUnique();
            b.Property(s => s.Name).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<PurchaseOrder>(b =>
        {
            b.ToTable("purchase_orders");
            b.HasKey(po => po.Id);
            b.Property(po => po.PoNumber).HasMaxLength(64).IsRequired();
            b.HasIndex(po => po.PoNumber).IsUnique();
            b.HasOne(po => po.Supplier).WithMany(s => s.PurchaseOrders).HasForeignKey(po => po.SupplierId);
            b.HasOne(po => po.Branch).WithMany().HasForeignKey(po => po.BranchId);
        });

        modelBuilder.Entity<PurchaseOrderItem>(b =>
        {
            b.ToTable("purchase_order_items");
            b.HasKey(poi => poi.Id);
            b.HasOne(poi => poi.PurchaseOrder).WithMany(po => po.Items).HasForeignKey(poi => poi.PurchaseOrderId);
            b.HasOne(poi => poi.Product).WithMany().HasForeignKey(poi => poi.ProductId);
        });

        modelBuilder.Entity<GoodsReceipt>(b =>
        {
            b.ToTable("goods_receipts");
            b.HasKey(gr => gr.Id);
            b.Property(gr => gr.GrnNumber).HasMaxLength(64).IsRequired();
            b.HasIndex(gr => gr.GrnNumber).IsUnique();
            b.HasOne(gr => gr.PurchaseOrder).WithMany(po => po.GoodsReceipts).HasForeignKey(gr => gr.PurchaseOrderId);
        });

        modelBuilder.Entity<GoodsReceiptItem>(b =>
        {
            b.ToTable("goods_receipt_items");
            b.HasKey(gri => gri.Id);
            b.HasOne(gri => gri.GoodsReceipt).WithMany(gr => gr.Items).HasForeignKey(gri => gri.GoodsReceiptId);
            b.HasOne(gri => gri.PurchaseOrderItem).WithMany().HasForeignKey(gri => gri.PurchaseOrderItemId);
            b.HasOne(gri => gri.Product).WithMany().HasForeignKey(gri => gri.ProductId);
        });

        // Stock Batch & Movements
        modelBuilder.Entity<StockBatch>(b =>
        {
            b.ToTable("stock_batches");
            b.HasKey(sb => sb.Id);
            b.HasOne(sb => sb.Branch).WithMany(br => br.StockBatches).HasForeignKey(sb => sb.BranchId);
            b.HasOne(sb => sb.Product).WithMany(p => p.StockBatches).HasForeignKey(sb => sb.ProductId);
            b.HasIndex(sb => new { sb.BranchId, sb.ProductId, sb.BatchNumber });
            b.Property(sb => sb.BatchNumber).HasMaxLength(64).IsRequired();
            b.Property(sb => sb.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<StockMovementLedger>(b =>
        {
            b.ToTable("stock_movement_ledgers");
            b.HasKey(sml => sml.Id);
            b.HasOne(sml => sml.StockBatch).WithMany().HasForeignKey(sml => sml.StockBatchId);
            b.HasIndex(sml => new { sml.BranchId, sml.ProductId });
        });

        modelBuilder.Entity<StockAdjustment>(b =>
        {
            b.ToTable("stock_adjustments");
            b.HasKey(sa => sa.Id);
            b.HasOne(sa => sa.StockBatch).WithMany().HasForeignKey(sa => sa.StockBatchId);
        });

        modelBuilder.Entity<StockTransfer>(b =>
        {
            b.ToTable("stock_transfers");
            b.HasKey(st => st.Id);
            b.Property(st => st.TransferNumber).HasMaxLength(64).IsRequired();
            b.HasIndex(st => st.TransferNumber).IsUnique();
        });

        modelBuilder.Entity<StockTransferItem>(b =>
        {
            b.ToTable("stock_transfer_items");
            b.HasKey(sti => sti.Id);
            b.HasOne(sti => sti.StockTransfer).WithMany(st => st.Items).HasForeignKey(sti => sti.StockTransferId);
        });

        // Sales & Returns
        modelBuilder.Entity<Sale>(b =>
        {
            b.ToTable("sales");
            b.HasKey(s => s.Id);
            b.Property(s => s.InvoiceNumber).HasMaxLength(64).IsRequired();
            b.HasIndex(s => s.InvoiceNumber).IsUnique();
            b.HasIndex(s => s.IdempotencyKey).IsUnique();
            b.HasOne(s => s.Shift).WithMany(cs => cs.Sales).HasForeignKey(s => s.ShiftId);
        });

        modelBuilder.Entity<SaleItem>(b =>
        {
            b.ToTable("sale_items");
            b.HasKey(si => si.Id);
            b.HasOne(si => si.Sale).WithMany(s => s.Items).HasForeignKey(si => si.SaleId);
            b.HasOne(si => si.Product).WithMany().HasForeignKey(si => si.ProductId);
            b.HasOne(si => si.StockBatch).WithMany().HasForeignKey(si => si.StockBatchId);
        });

        modelBuilder.Entity<CustomerReturn>(b =>
        {
            b.ToTable("customer_returns");
            b.HasKey(cr => cr.Id);
            b.Property(cr => cr.ReturnNumber).HasMaxLength(64).IsRequired();
            b.HasIndex(cr => cr.ReturnNumber).IsUnique();
            b.HasOne(cr => cr.OriginalSale).WithMany(s => s.Returns).HasForeignKey(cr => cr.OriginalSaleId);
        });

        modelBuilder.Entity<CustomerReturnItem>(b =>
        {
            b.ToTable("customer_return_items");
            b.HasKey(cri => cri.Id);
            b.HasOne(cri => cri.CustomerReturn).WithMany(cr => cr.Items).HasForeignKey(cri => cri.CustomerReturnId);
            b.HasOne(cri => cri.OriginalSaleItem).WithMany().HasForeignKey(cri => cri.OriginalSaleItemId);
        });

        // Prescriptions & Dispensing
        modelBuilder.Entity<DispensingAuthorization>(b =>
        {
            b.ToTable("dispensing_authorizations");
            b.HasKey(da => da.Id);
            b.HasOne(da => da.Product).WithMany().HasForeignKey(da => da.ProductId);
        });

        modelBuilder.Entity<DispensingRecord>(b =>
        {
            b.ToTable("dispensing_records");
            b.HasKey(dr => dr.Id);
            b.HasOne(dr => dr.Authorization).WithMany(da => da.DispensingRecords).HasForeignKey(dr => dr.AuthorizationId);
        });

        // Outbox & Inbox
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(m => m.Id);
            b.HasIndex(m => m.ProcessedAtUtc);
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox_messages");
            b.HasKey(m => m.Id);
            b.HasIndex(m => m.ProcessedAtUtc);
        });
    }
}
