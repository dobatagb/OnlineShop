using Shop.Application.Abstractions;
using Shop.Application.Common;
using Shop.Domain.Coupons;
using Shop.Domain.Orders;
using Shop.Domain.Products;

namespace Shop.Tests.Application;

internal sealed class FakeOrderRepository : IOrderRepository
{
    public List<Order> Orders { get; } = [];

    public Task<Order?> GetForUserAsync(Guid userId, Guid orderId, CancellationToken ct) =>
        Task.FromResult(Orders.SingleOrDefault(o => o.Id == orderId && o.UserId == userId && o.DeletedAt is null));

    public Task<Order?> GetByIdAsync(Guid orderId, CancellationToken ct) =>
        Task.FromResult(Orders.SingleOrDefault(o => o.Id == orderId && o.DeletedAt is null));

    public Task<IReadOnlyList<Guid>> GetAbandonedDraftIdsAsync(DateTimeOffset untouchedSince, int maxCount, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>(Orders
            .Where(o => o.Status == OrderStatus.Draft && o.DeletedAt is null && o.UpdatedAt < untouchedSince)
            .OrderBy(o => o.UpdatedAt)
            .Take(maxCount)
            .Select(o => o.Id)
            .ToList());

    public void Add(Order order) => Orders.Add(order);
}

internal sealed class FakeProductRepository(params Product[] products) : IProductRepository
{
    public Task<IReadOnlyDictionary<Guid, Product>> GetByIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Product>>(products.Where(p => productIds.Contains(p.Id)).ToDictionary(p => p.Id));
}

internal sealed class FakeCouponRepository(params Coupon[] coupons) : ICouponRepository
{
    public Task<Coupon?> GetByCodeAsync(string normalizedCode, CancellationToken ct) =>
        Task.FromResult(coupons.SingleOrDefault(c => c.Code == normalizedCode));
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly Queue<bool> _conflicts = new();

    public int SaveAttempts { get; private set; }
    public int Discards { get; private set; }

    public FakeUnitOfWork FailNextSaves(params bool[] conflicts)
    {
        foreach (bool conflict in conflicts)
        {
            _conflicts.Enqueue(conflict);
        }

        return this;
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        SaveAttempts++;
        if (_conflicts.TryDequeue(out bool conflict) && conflict)
        {
            throw new ConcurrencyException("Simulated conflict.");
        }

        return Task.CompletedTask;
    }

    public void DiscardChanges() => Discards++;
}
