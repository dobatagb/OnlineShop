using Shop.Domain.Common;
using Shop.Domain.Coupons;
using Shop.Domain.Products;

namespace Shop.Domain.Orders;

public enum OrderStatus
{
    Draft = 1,

    Placed = 2,

    Expired = 3,
}

public class Order
{
    public const int MaxQuantityPerLine = 100;
    public const int MaxLines = 50;

    private readonly List<OrderItem> _items = [];

    private Order() { }

    public Order(Guid userId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        Status = OrderStatus.Draft;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public OrderStatus Status { get; private set; }
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public Guid? CouponId { get; private set; }
    public Coupon? Coupon { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal Discount { get; private set; }
    public decimal Total { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PlacedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public int Version { get; private set; }

    public void SetItemQuantity(Product product, int quantity, DateTimeOffset now)
    {
        EnsureDraft();

        if (quantity is < 0 or > MaxQuantityPerLine)
        {
            throw new DomainException(
                "order.invalid_quantity",
                $"Quantity for '{product.Sku}' must be between 1 and {MaxQuantityPerLine}.");
        }

        OrderItem? line = _items.Find(i => i.ProductId == product.Id);
        int delta = quantity - (line?.Quantity ?? 0);

        if (delta > 0) product.Reserve(delta, now);
        else if (delta < 0) product.Release(-delta, now);

        if (quantity == 0)
        {
            if (line is not null) _items.Remove(line);
        }
        else if (line is null)
        {
            if (_items.Count >= MaxLines)
            {
                throw new DomainException("order.too_many_lines", $"An order can contain at most {MaxLines} different products.");
            }

            _items.Add(new OrderItem(this, product, quantity));
        }
        else
        {
            line.ChangeQuantity(quantity);
        }
    }

    public void RemoveItem(Guid productId, DateTimeOffset now)
    {
        OrderItem line = _items.Find(i => i.ProductId == productId)
            ?? throw new DomainException("order.item_not_found", $"Product '{productId}' is not on this order.");
        SetItemQuantity(line.Product, 0, now);
    }

    public void ApplyCoupon(Coupon coupon)
    {
        EnsureDraft();
        Coupon = coupon;
        CouponId = coupon.Id;
    }

    public void RemoveCoupon()
    {
        EnsureDraft();
        Coupon = null;
        CouponId = null;
    }

    public void Recalculate(DateTimeOffset now)
    {
        EnsureDraft();

        if (_items.Count == 0)
        {
            throw new DomainException("order.empty", "An order must contain at least one product. Delete the order instead.");
        }

        foreach (OrderItem item in _items) item.RefreshPrice();

        Subtotal = Money.Round(_items.Sum(i => i.LineTotal));

        if (Coupon is not null)
        {
            Coupon.EnsureApplicable(Subtotal, now);
            Discount = Coupon.CalculateDiscount(Subtotal);
        }
        else
        {
            Discount = 0;
        }

        Total = Subtotal - Discount;
        Touch(now);
    }

    public void Place(DateTimeOffset now)
    {
        Recalculate(now);
        Status = OrderStatus.Placed;
        PlacedAt = now;
    }

    public void Expire(DateTimeOffset now)
    {
        EnsureDraft();
        ReleaseStock(now);
        Status = OrderStatus.Expired;
        Touch(now);
    }

    public void Delete(DateTimeOffset now)
    {
        if (DeletedAt is not null) return;
        if (Status is OrderStatus.Draft or OrderStatus.Placed) ReleaseStock(now);
        DeletedAt = now;
        Touch(now);
    }

    private void ReleaseStock(DateTimeOffset now)
    {
        foreach (OrderItem item in _items) item.Product.Release(item.Quantity, now);
    }

    private void EnsureDraft()
    {
        if (Status != OrderStatus.Draft)
        {
            throw new DomainConflictException("order.not_editable", $"Order is {Status} and can no longer be changed.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version++;
    }
}
