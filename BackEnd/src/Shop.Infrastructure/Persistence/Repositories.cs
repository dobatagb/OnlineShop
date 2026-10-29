using Microsoft.EntityFrameworkCore;
using Shop.Application.Abstractions;
using Shop.Application.Common;
using Shop.Domain.Coupons;
using Shop.Domain.Orders;
using Shop.Domain.Products;
using Shop.Domain.Users;

namespace Shop.Infrastructure.Persistence;

internal sealed class OrderRepository(ShopDbContext db) : IOrderRepository
{
    public Task<Order?> GetForUserAsync(Guid userId, Guid orderId, CancellationToken ct) =>
        WithDetails().SingleOrDefaultAsync(o => o.Id == orderId && o.UserId == userId, ct);

    public Task<Order?> GetByIdAsync(Guid orderId, CancellationToken ct) =>
        WithDetails().SingleOrDefaultAsync(o => o.Id == orderId, ct);

    public async Task<IReadOnlyList<Guid>> GetAbandonedDraftIdsAsync(DateTimeOffset untouchedSince, int maxCount, CancellationToken ct) =>
        await db.Orders
            .Where(o => o.Status == OrderStatus.Draft && o.UpdatedAt < untouchedSince)
            .OrderBy(o => o.UpdatedAt)
            .Select(o => o.Id)
            .Take(maxCount)
            .ToListAsync(ct);

    public void Add(Order order) => db.Orders.Add(order);

    private IQueryable<Order> WithDetails() => db.Orders
        .Include(o => o.Items).ThenInclude(i => i.Product)
        .Include(o => o.Coupon)
        .AsSplitQuery();
}

internal sealed class ProductRepository(ShopDbContext db) : IProductRepository
{
    public async Task<IReadOnlyDictionary<Guid, Product>> GetByIdsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct) =>
        await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
}

internal sealed class CouponRepository(ShopDbContext db) : ICouponRepository
{
    public Task<Coupon?> GetByCodeAsync(string normalizedCode, CancellationToken ct) =>
        db.Coupons.SingleOrDefaultAsync(c => c.Code == normalizedCode, ct);
}

internal sealed class UserRepository(ShopDbContext db) : IUserRepository
{
    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        db.Users.SingleOrDefaultAsync(u => u.Email == normalizedEmail, ct);
}

internal sealed class EfUnitOfWork(ShopDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException("The data was modified by another request.", ex);
        }
    }

    public void DiscardChanges() => db.ChangeTracker.Clear();
}
