using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Shop.Application.Abstractions;
using Shop.Application.Common;
using Shop.Application.Products;
using Shop.Domain.Orders;
using Shop.Domain.Products;

namespace Shop.Infrastructure.Persistence;

internal sealed class ProductQueries(ShopDbContext db) : IProductQueries
{
    private static readonly Dictionary<ProductSort, Expression<Func<Product, object>>> SortKeys = new()
    {
        [ProductSort.Name] = p => p.Name,
        [ProductSort.Price] = p => p.Price,
        [ProductSort.Sku] = p => p.Sku,
        [ProductSort.Category] = p => p.Category,
        [ProductSort.StockQuantity] = p => p.StockQuantity,
        [ProductSort.CreatedAt] = p => p.CreatedAt,
    };

    public async Task<PagedResponse<ProductResponse>> ListAsync(ProductListCriteria criteria, CancellationToken ct)
    {
        IQueryable<Product> query = db.Products.AsNoTracking();
        if (criteria.Category is not null) query = query.Where(p => p.Category == criteria.Category);

        int total = await query.CountAsync(ct);

        Expression<Func<Product, object>> sortKey = SortKeys[criteria.SortBy];
        IOrderedQueryable<Product> ordered = criteria.Descending
            ? query.OrderByDescending(sortKey).ThenBy(p => p.Id)
            : query.OrderBy(sortKey).ThenBy(p => p.Id);

        List<ProductResponse> items = await ordered
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(p => new ProductResponse(p.Id, p.Sku, p.Name, p.Description, p.Price, p.StockQuantity, p.Category, p.CreatedAt, p.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResponse<ProductResponse>(items, criteria.Page, criteria.PageSize, total);
    }
}

internal sealed class OrderQueries(ShopDbContext db) : IOrderQueries
{
    public async Task<PagedResponse<OrderSummaryResponse>> ListForUserAsync(Guid userId, int page, int pageSize, CancellationToken ct)
    {
        IQueryable<Order> query = db.Orders.AsNoTracking().Where(o => o.UserId == userId);
        int total = await query.CountAsync(ct);
        List<OrderSummaryResponse> items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryResponse(o.Id, o.Status, o.Items.Sum(i => i.Quantity), o.Total, o.CreatedAt, o.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResponse<OrderSummaryResponse>(items, page, pageSize, total);
    }
}
