using Shop.Application.Abstractions;
using Shop.Application.Common;
using Shop.Domain.Coupons;
using Shop.Domain.Orders;
using Shop.Domain.Products;

namespace Shop.Application.Orders;

internal static class OrderSupport
{
    public static List<OrderLineRequest> ValidateLines(IReadOnlyList<OrderLineRequest>? items)
    {
        if (items is null)
        {
            return [];
        }

        if (items.Any(i => i.ProductId == Guid.Empty))
        {
            throw new RequestValidationException("order.invalid_product", "Every line must reference a productId.");
        }

        IGrouping<Guid, OrderLineRequest>? duplicate = items.GroupBy(i => i.ProductId).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new RequestValidationException("order.duplicate_product", $"Product '{duplicate.Key}' appears more than once; send one line per product.");
        }

        return items.ToList();
    }

    public static async Task<IReadOnlyDictionary<Guid, Product>> LoadProductsAsync(
        IProductRepository products, IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        IReadOnlyDictionary<Guid, Product> found = await products.GetByIdsAsync(productIds, ct);

        List<Guid> missing = productIds.Where(id => !found.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new RequestValidationException("order.product_not_found", $"Unknown product id(s): {string.Join(", ", missing)}.");
        }

        return found;
    }

    public static async Task<Coupon> LoadCouponAsync(ICouponRepository coupons, string code, CancellationToken ct)
    {
        string normalized = Coupon.NormalizeCode(code);
        return await coupons.GetByCodeAsync(normalized, ct)
            ?? throw new RequestValidationException("coupon.not_found", $"Coupon '{normalized}' does not exist.");
    }

    public static async Task<Order> LoadOwnedOrderAsync(IOrderRepository orders, Guid userId, Guid orderId, CancellationToken ct) =>
        await orders.GetForUserAsync(userId, orderId, ct)
            ?? throw new NotFoundException($"Order '{orderId}' was not found.");
}
