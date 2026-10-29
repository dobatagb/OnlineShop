using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Shop.Domain.Coupons;
using Shop.Domain.Orders;
using Shop.Domain.Products;
using Shop.Domain.Users;

namespace Shop.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasKey(u => u.Id);
        b.Property(u => u.Id).ValueGeneratedNever();
        b.HasIndex(u => u.Email).IsUnique();
        b.Property(u => u.Email).IsRequired();
        b.Property(u => u.DisplayName).IsRequired().HasMaxLength(100);
        b.Property(u => u.PasswordHash).IsRequired().HasMaxLength(512);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Products_PriceNonNegative", "[Price] >= 0");
            t.HasCheckConstraint("CK_Products_StockNonNegative", "[StockQuantity] >= 0");
        });

        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.HasIndex(p => p.Sku).IsUnique();
        b.HasIndex(p => p.Category);
        b.HasIndex(p => p.Name);
        b.Property(p => p.Sku).IsRequired().HasMaxLength(32);
        b.Property(p => p.Name).IsRequired().HasMaxLength(200);
        b.Property(p => p.Description).IsRequired().HasMaxLength(2000);
        b.Property(p => p.Category).IsRequired().HasMaxLength(100);
        b.Property(p => p.Version).IsConcurrencyToken();
    }
}

internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> b)
    {
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Coupons_ValuePositive", "[Value] > 0");
            t.HasCheckConstraint("CK_Coupons_PercentageMax", "[Type] <> 1 OR [Value] <= 100");
        });

        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.HasIndex(c => c.Code).IsUnique();
        b.Property(c => c.Code).IsRequired().HasMaxLength(50);
        b.Property(c => c.Description).IsRequired().HasMaxLength(500);
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Orders_TotalsNonNegative", "[Subtotal] >= 0 AND [Discount] >= 0 AND [Total] >= 0");
            t.HasCheckConstraint("CK_Orders_TotalConsistent", "[Total] = [Subtotal] - [Discount]");
        });

        b.HasKey(o => o.Id);
        b.Property(o => o.Id).ValueGeneratedNever();
        b.HasIndex(o => new { o.UserId, o.CreatedAt });
        b.HasIndex(o => new { o.Status, o.UpdatedAt });

        b.Property(o => o.Version).IsConcurrencyToken();

        b.HasOne<User>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(o => o.Coupon).WithMany().HasForeignKey(o => o.CouponId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasQueryFilter(o => o.DeletedAt == null);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("OrderItems", t =>
        {
            t.HasCheckConstraint("CK_OrderItems_QuantityPositive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_OrderItems_UnitPriceNonNegative", "[UnitPrice] >= 0");
            t.HasCheckConstraint("CK_OrderItems_LineTotal", "[LineTotal] = ROUND([UnitPrice] * [Quantity], 2)");
        });

        b.HasKey(i => i.Id);
        b.Property(i => i.Id).ValueGeneratedNever();
        b.HasIndex(i => new { i.OrderId, i.ProductId }).IsUnique();
        b.Property(i => i.Sku).IsRequired().HasMaxLength(32);
        b.Property(i => i.ProductName).IsRequired().HasMaxLength(200);

        b.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}
