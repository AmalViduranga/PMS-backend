using Microsoft.EntityFrameworkCore;
using PharmaFlow.Commerce.Api.Domain;

namespace PharmaFlow.Commerce.Api.Data;

public class CommerceDbContext : DbContext
{
    public CommerceDbContext(DbContextOptions<CommerceDbContext> options) : base(options) { }

    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<ProductCatalogProjection> ProductCatalogProjections => Set<ProductCatalogProjection>();
    public DbSet<CustomerOrder> CustomerOrders => Set<CustomerOrder>();
    public DbSet<CustomerOrderItem> CustomerOrderItems => Set<CustomerOrderItem>();
    public DbSet<CommerceOutboxMessage> OutboxMessages => Set<CommerceOutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CustomerProfile>(b =>
        {
            b.ToTable("customer_profiles");
            b.HasKey(c => c.Id);
            b.HasIndex(c => c.IdentityUserId).IsUnique();
            b.Property(c => c.Email).HasMaxLength(256).IsRequired();
            b.Property(c => c.FullName).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<ProductCatalogProjection>(b =>
        {
            b.ToTable("product_catalog_projections");
            b.HasKey(p => p.Id);
            b.Property(p => p.Sku).HasMaxLength(64).IsRequired();
            b.Property(p => p.BrandName).HasMaxLength(128).IsRequired();
        });

        modelBuilder.Entity<CustomerOrder>(b =>
        {
            b.ToTable("customer_orders");
            b.HasKey(o => o.Id);
            b.Property(o => o.OrderReference).HasMaxLength(64).IsRequired();
            b.HasIndex(o => o.OrderReference).IsUnique();
            b.HasOne(o => o.Customer).WithMany(c => c.Orders).HasForeignKey(o => o.CustomerId);
            b.Property(o => o.Status).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<CustomerOrderItem>(b =>
        {
            b.ToTable("customer_order_items");
            b.HasKey(oi => oi.Id);
            b.HasOne(oi => oi.Order).WithMany(o => o.Items).HasForeignKey(oi => oi.OrderId);
        });

        modelBuilder.Entity<CommerceOutboxMessage>(b =>
        {
            b.ToTable("commerce_outbox_messages");
            b.HasKey(m => m.Id);
            b.HasIndex(m => m.ProcessedAtUtc);
        });
    }
}
