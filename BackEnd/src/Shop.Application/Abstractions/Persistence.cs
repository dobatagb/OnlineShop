using Shop.Application.Common;
using Shop.Application.Products;
using Shop.Domain.Coupons;
using Shop.Domain.Orders;
using Shop.Domain.Products;
using Shop.Domain.Users;

namespace Shop.Application.Abstractions;

public interface IOrderRepository
{
    Task<Order?> GetForUserAsync(Guid userId, Guid orderId, CancellationToken ct);
    Task<Order?> GetByIdAsync(Guid orderId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetAbandonedDraftIdsAsync(DateTimeOffset untouchedSince, int maxCount, CancellationToken ct);
    void Add(Order order);
}

public interface IProductRepository
{
    Task<IReadOnlyDictionary<Guid, Product>> GetByIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct);
}

public interface ICouponRepository
{
    Task<Coupon?> GetByCodeAsync(string normalizedCode, CancellationToken ct);
}

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct);
}

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
    void DiscardChanges();
}

public interface IProductQueries
{
    Task<PagedResponse<ProductResponse>> ListAsync(ProductListCriteria criteria, CancellationToken ct);
}

public interface IOrderQueries
{
    Task<PagedResponse<OrderSummaryResponse>> ListForUserAsync(Guid userId, int page, int pageSize, CancellationToken ct);
}
