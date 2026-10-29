using Shop.Domain.Common;
using Shop.Domain.Coupons;
using Shop.Domain.Orders;
using Shop.Domain.Products;

namespace Shop.Tests.Domain;

public class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    private static Product NewProduct(decimal price, int stock = 100, string sku = "SKU-1") =>
        new(sku, "Product " + sku, "desc", price, stock, "Category", Now);

    [Fact]
    public void Totals_are_computed_from_catalogue_prices()
    {
        Product a = NewProduct(19.99m, sku: "A");
        Product b = NewProduct(5.01m, sku: "B");
        Order order = new Order(UserId, Now);

        order.SetItemQuantity(a, 3, Now);
        order.SetItemQuantity(b, 2, Now);
        order.Recalculate(Now);

        Assert.Equal(59.97m, order.Items.Single(i => i.ProductId == a.Id).LineTotal);
        Assert.Equal(69.99m, order.Subtotal);
        Assert.Equal(0m, order.Discount);
        Assert.Equal(69.99m, order.Total);
    }

    [Fact]
    public void Percentage_coupon_is_rounded_half_away_from_zero()
    {
        Order order = new Order(UserId, Now);
        order.SetItemQuantity(NewProduct(10.05m), 1, Now);
        order.ApplyCoupon(new Coupon("HALF", "50%", DiscountType.Percentage, 50));

        order.Recalculate(Now);

        Assert.Equal(5.03m, order.Discount);
        Assert.Equal(5.02m, order.Total);
    }

    [Fact]
    public void Fixed_coupon_never_exceeds_subtotal()
    {
        Order order = new Order(UserId, Now);
        order.SetItemQuantity(NewProduct(7.50m), 1, Now);
        order.ApplyCoupon(new Coupon("BIG", "50 off", DiscountType.FixedAmount, 50));

        order.Recalculate(Now);

        Assert.Equal(7.50m, order.Discount);
        Assert.Equal(0m, order.Total);
    }

    [Fact]
    public void Coupon_minimum_subtotal_is_enforced()
    {
        Order order = new Order(UserId, Now);
        order.SetItemQuantity(NewProduct(40m), 2, Now);
        order.ApplyCoupon(new Coupon("SAVE20", "20 off 100", DiscountType.FixedAmount, 20, minimumSubtotal: 100));

        DomainException ex = Assert.Throws<DomainException>(() => order.Recalculate(Now));
        Assert.Equal("coupon.minimum_not_met", ex.Code);
    }

    [Fact]
    public void Expired_coupon_is_rejected()
    {
        Order order = new Order(UserId, Now);
        order.SetItemQuantity(NewProduct(40m), 1, Now);
        order.ApplyCoupon(new Coupon("OLD", "old", DiscountType.Percentage, 10, validUntil: Now.AddDays(-1)));

        DomainException ex = Assert.Throws<DomainException>(() => order.Recalculate(Now));
        Assert.Equal("coupon.not_valid", ex.Code);
    }

    [Fact]
    public void Prices_are_refreshed_from_the_catalogue_on_recalculate()
    {
        Product product = NewProduct(10m);
        Order order = new Order(UserId, Now);
        order.SetItemQuantity(product, 2, Now);
        order.Recalculate(Now);

        typeof(Product).GetProperty(nameof(Product.Price))!.SetValue(product, 12.5m);
        order.Recalculate(Now);

        Assert.Equal(12.5m, order.Items.Single().UnitPrice);
        Assert.Equal(25m, order.Total);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(Order.MaxQuantityPerLine + 1)]
    public void Invalid_quantities_are_rejected(int quantity)
    {
        Order order = new Order(UserId, Now);
        DomainException ex = Assert.Throws<DomainException>(() => order.SetItemQuantity(NewProduct(1m), quantity, Now));
        Assert.Equal("order.invalid_quantity", ex.Code);
    }

    [Fact]
    public void Stock_is_reserved_and_released_by_quantity_changes()
    {
        Product product = NewProduct(1m, stock: 10);
        Order order = new Order(UserId, Now);

        order.SetItemQuantity(product, 4, Now);
        Assert.Equal(6, product.StockQuantity);

        order.SetItemQuantity(product, 1, Now);
        Assert.Equal(9, product.StockQuantity);

        order.SetItemQuantity(product, 0, Now);
        Assert.Equal(10, product.StockQuantity);
        Assert.Empty(order.Items);
    }

    [Fact]
    public void Cannot_reserve_more_than_available_stock()
    {
        Product product = NewProduct(1m, stock: 2);
        Order order = new Order(UserId, Now);

        DomainConflictException ex = Assert.Throws<DomainConflictException>(() => order.SetItemQuantity(product, 3, Now));
        Assert.Equal("product.insufficient_stock", ex.Code);
        Assert.Equal(2, product.StockQuantity);
    }

    [Fact]
    public void Empty_order_is_rejected()
    {
        Order order = new Order(UserId, Now);
        DomainException ex = Assert.Throws<DomainException>(() => order.Recalculate(Now));
        Assert.Equal("order.empty", ex.Code);
    }

    [Fact]
    public void Placed_order_cannot_be_modified()
    {
        Product product = NewProduct(1m);
        Order order = new Order(UserId, Now);
        order.SetItemQuantity(product, 1, Now);
        order.Place(Now);

        DomainConflictException ex = Assert.Throws<DomainConflictException>(() => order.SetItemQuantity(product, 2, Now));
        Assert.Equal("order.not_editable", ex.Code);
    }

    [Fact]
    public void Expiring_a_draft_releases_its_stock()
    {
        Product product = NewProduct(1m, stock: 10);
        Order order = new Order(UserId, Now);
        order.SetItemQuantity(product, 7, Now);
        order.Recalculate(Now);

        order.Expire(Now.AddHours(1));

        Assert.Equal(OrderStatus.Expired, order.Status);
        Assert.Equal(10, product.StockQuantity);
    }

    [Fact]
    public void Deleting_an_expired_order_does_not_release_stock_twice()
    {
        Product product = NewProduct(1m, stock: 10);
        Order order = new Order(UserId, Now);
        order.SetItemQuantity(product, 7, Now);
        order.Recalculate(Now);
        order.Expire(Now);

        order.Delete(Now);

        Assert.NotNull(order.DeletedAt);
        Assert.Equal(10, product.StockQuantity);
    }
}
