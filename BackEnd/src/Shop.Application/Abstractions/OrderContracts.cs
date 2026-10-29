using Shop.Domain.Orders;

namespace Shop.Application.Abstractions;

public sealed record OrderLineRequest(Guid ProductId, int Quantity);

public sealed record CreateOrderRequest(IReadOnlyList<OrderLineRequest>? Items, string? CouponCode);

public enum ItemsUpdateMode
{
    Merge,
    Replace,
}

public sealed record UpdateOrderRequest(
    IReadOnlyList<OrderLineRequest>? Items,
    ItemsUpdateMode ItemsMode = ItemsUpdateMode.Merge,
    string? CouponCode = null,
    bool RemoveCoupon = false);

public sealed record OrderItemResponse(Guid ProductId, string Sku, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record OrderResponse(
    Guid Id,
    OrderStatus Status,
    IReadOnlyList<OrderItemResponse> Items,
    string? CouponCode,
    decimal Subtotal,
    decimal Discount,
    decimal Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PlacedAt)
{
    public static OrderResponse From(Order order) => new(
        order.Id,
        order.Status,
        order.Items
            .OrderBy(i => i.ProductName)
            .Select(i => new OrderItemResponse(i.ProductId, i.Sku, i.ProductName, i.UnitPrice, i.Quantity, i.LineTotal))
            .ToList(),
        order.Coupon?.Code,
        order.Subtotal,
        order.Discount,
        order.Total,
        order.CreatedAt,
        order.UpdatedAt,
        order.PlacedAt);
}

public sealed record OrderSummaryResponse(
    Guid Id,
    OrderStatus Status,
    int ItemCount,
    decimal Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
