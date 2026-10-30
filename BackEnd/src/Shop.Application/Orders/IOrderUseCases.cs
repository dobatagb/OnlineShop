using Shop.Application.Abstractions;
using Shop.Application.Common;

namespace Shop.Application.Orders;

public interface IGetOrderUseCase
{
    Task<OrderResponse> ExecuteAsync(Guid userId, Guid orderId, CancellationToken ct);
}

public interface IListOrdersUseCase
{
    Task<PagedResponse<OrderSummaryResponse>> ExecuteAsync(Guid userId, int? page, int? pageSize, CancellationToken ct);
}

public interface ICreateOrderUseCase
{
    Task<OrderResponse> ExecuteAsync(Guid userId, CreateOrderRequest request, CancellationToken ct);
}

public interface IUpdateOrderUseCase
{
    Task<OrderResponse> ExecuteAsync(Guid userId, Guid orderId, UpdateOrderRequest request, CancellationToken ct);
}

public interface IPlaceOrderUseCase
{
    Task<OrderResponse> ExecuteAsync(Guid userId, Guid orderId, CancellationToken ct);
}

public interface IDeleteOrderUseCase
{
    Task ExecuteAsync(Guid userId, Guid orderId, CancellationToken ct);
}

public interface IExpireAbandonedDraftsUseCase
{
    Task<int> ExecuteAsync(TimeSpan draftTimeout, int batchSize, CancellationToken ct);
}
