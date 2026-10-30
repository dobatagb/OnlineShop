using Microsoft.Extensions.Logging;
using Shop.Application.Abstractions;
using Shop.Application.Common;
using Shop.Domain.Orders;
using Shop.Domain.Products;

namespace Shop.Application.Orders;

public sealed class GetOrderUseCase(IOrderRepository orders) : IGetOrderUseCase
{
    public async Task<OrderResponse> ExecuteAsync(Guid userId, Guid orderId, CancellationToken ct) =>
        OrderResponse.From(await OrderSupport.LoadOwnedOrderAsync(orders, userId, orderId, ct));
}

public sealed class ListOrdersUseCase(IOrderQueries queries) : IListOrdersUseCase
{
    public Task<PagedResponse<OrderSummaryResponse>> ExecuteAsync(Guid userId, int? page, int? pageSize, CancellationToken ct)
    {
        (int p, int size) = Paging.Normalize(page, pageSize);
        return queries.ListForUserAsync(userId, p, size, ct);
    }
}

public sealed class CreateOrderUseCase(
    IOrderRepository orders,
    IProductRepository products,
    ICouponRepository coupons,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<CreateOrderUseCase> logger) : ICreateOrderUseCase
{
    public Task<OrderResponse> ExecuteAsync(Guid userId, CreateOrderRequest request, CancellationToken ct) =>
        ConcurrencyRetry.ExecuteAsync(unitOfWork, logger, async () =>
        {
            List<OrderLineRequest> lines = OrderSupport.ValidateLines(request.Items);
            if (lines.Count == 0)
            {
                throw new RequestValidationException("order.empty", "An order must contain at least one product.");
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            IReadOnlyDictionary<Guid, Product> catalogue = await OrderSupport.LoadProductsAsync(products, lines.Select(l => l.ProductId).ToList(), ct);

            Order order = new Order(userId, now);
            foreach (OrderLineRequest line in lines)
            {
                order.SetItemQuantity(catalogue[line.ProductId], line.Quantity, now);
            }

            if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                order.ApplyCoupon(await OrderSupport.LoadCouponAsync(coupons, request.CouponCode, ct));
            }

            order.Recalculate(now);

            orders.Add(order);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation("User {UserId} created order {OrderId} with {Lines} line(s), total {Total}", userId, order.Id, order.Items.Count, order.Total);
            return OrderResponse.From(order);
        });
}

public sealed class UpdateOrderUseCase(
    IOrderRepository orders,
    IProductRepository products,
    ICouponRepository coupons,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<UpdateOrderUseCase> logger) : IUpdateOrderUseCase
{
    public Task<OrderResponse> ExecuteAsync(Guid userId, Guid orderId, UpdateOrderRequest request, CancellationToken ct) =>
        ConcurrencyRetry.ExecuteAsync(unitOfWork, logger, async () =>
        {
            if (request.RemoveCoupon && !string.IsNullOrWhiteSpace(request.CouponCode))
            {
                throw new RequestValidationException("order.coupon_conflict", "Specify either couponCode or removeCoupon, not both.");
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            Order order = await OrderSupport.LoadOwnedOrderAsync(orders, userId, orderId, ct);
            List<OrderLineRequest> lines = OrderSupport.ValidateLines(request.Items);

            if (request.Items is not null)
            {
                HashSet<Guid> requestedIds = lines.Select(l => l.ProductId).ToHashSet();

                if (request.ItemsMode == ItemsUpdateMode.Replace)
                {
                    foreach (OrderItem? existing in order.Items.Where(i => !requestedIds.Contains(i.ProductId)).ToList())
                    {
                        order.RemoveItem(existing.ProductId, now);
                    }
                }

                IReadOnlyDictionary<Guid, Product> catalogue = await OrderSupport.LoadProductsAsync(products, requestedIds, ct);
                foreach (OrderLineRequest line in lines)
                {
                    order.SetItemQuantity(catalogue[line.ProductId], line.Quantity, now);
                }
            }

            if (request.RemoveCoupon)
            {
                order.RemoveCoupon();
            }
            else if (!string.IsNullOrWhiteSpace(request.CouponCode))
            {
                order.ApplyCoupon(await OrderSupport.LoadCouponAsync(coupons, request.CouponCode, ct));
            }

            order.Recalculate(now);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation("User {UserId} updated order {OrderId}, total {Total}", userId, order.Id, order.Total);
            return OrderResponse.From(order);
        });
}

public sealed class PlaceOrderUseCase(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<PlaceOrderUseCase> logger) : IPlaceOrderUseCase
{
    public Task<OrderResponse> ExecuteAsync(Guid userId, Guid orderId, CancellationToken ct) =>
        ConcurrencyRetry.ExecuteAsync(unitOfWork, logger, async () =>
        {
            Order order = await OrderSupport.LoadOwnedOrderAsync(orders, userId, orderId, ct);
            order.Place(timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation("User {UserId} placed order {OrderId}, total {Total}", userId, order.Id, order.Total);
            return OrderResponse.From(order);
        });
}

public sealed class DeleteOrderUseCase(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<DeleteOrderUseCase> logger) : IDeleteOrderUseCase
{
    public Task ExecuteAsync(Guid userId, Guid orderId, CancellationToken ct) =>
        ConcurrencyRetry.ExecuteAsync(unitOfWork, logger, async () =>
        {
            Order order = await OrderSupport.LoadOwnedOrderAsync(orders, userId, orderId, ct);
            order.Delete(timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation("User {UserId} deleted order {OrderId}", userId, order.Id);
            return true;
        });
}

public sealed class ExpireAbandonedDraftsUseCase(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<ExpireAbandonedDraftsUseCase> logger) : IExpireAbandonedDraftsUseCase
{
    public async Task<int> ExecuteAsync(TimeSpan draftTimeout, int batchSize, CancellationToken ct)
    {
        DateTimeOffset cutoff = timeProvider.GetUtcNow() - draftTimeout;
        IReadOnlyList<Guid> candidateIds = await orders.GetAbandonedDraftIdsAsync(cutoff, batchSize, ct);
        int expired = 0;

        foreach (Guid id in candidateIds)
        {
            unitOfWork.DiscardChanges();

            Order? order = await orders.GetByIdAsync(id, ct);
            if (order is null || order.Status != OrderStatus.Draft || order.UpdatedAt >= cutoff)
            {
                continue;
            }

            DateTimeOffset lastUpdated = order.UpdatedAt;
            order.Expire(timeProvider.GetUtcNow());

            try
            {
                await unitOfWork.SaveChangesAsync(ct);
                expired++;
                logger.LogInformation(
                    "Expired abandoned draft {OrderId} (last updated {UpdatedAt:o}); released {Units} unit(s) of stock",
                    order.Id, lastUpdated, order.Items.Sum(i => i.Quantity));
            }
            catch (ConcurrencyException)
            {
                logger.LogInformation("Draft {OrderId} changed while being expired; it will be re-evaluated on the next run", id);
            }
        }

        return expired;
    }
}
