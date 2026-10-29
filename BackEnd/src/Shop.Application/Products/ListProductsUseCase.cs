using System.Text.Json;
using Shop.Application.Abstractions;
using Shop.Application.Common;

namespace Shop.Application.Products;

public sealed record ProductResponse(
    Guid Id,
    string Sku,
    string Name,
    string Description,
    decimal Price,
    int StockQuantity,
    string Category,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum ProductSort
{
    Name,
    Price,
    Sku,
    Category,
    StockQuantity,
    CreatedAt,
}

public sealed record ProductListCriteria(int Page, int PageSize, ProductSort SortBy, bool Descending, string? Category);

public sealed class ListProductsUseCase(IProductQueries queries)
{
    private static readonly Dictionary<string, ProductSort> SortFields = Enum.GetValues<ProductSort>()
        .ToDictionary(s => JsonNamingPolicy.CamelCase.ConvertName(s.ToString()), StringComparer.OrdinalIgnoreCase);

    public Task<PagedResponse<ProductResponse>> ExecuteAsync(
        int? page, int? pageSize, string? sortBy, string? sortDirection, string? category, CancellationToken ct)
    {
        (int p, int size) = Paging.Normalize(page, pageSize);

        ProductSort sort = ProductSort.Name;
        if (!string.IsNullOrWhiteSpace(sortBy) && !SortFields.TryGetValue(sortBy, out sort))
        {
            throw new RequestValidationException("products.invalid_sort", $"sortBy must be one of: {string.Join(", ", SortFields.Keys)}.");
        }

        bool descending = sortDirection?.ToLowerInvariant() switch
        {
            null or "" or "asc" => false,
            "desc" => true,
            _ => throw new RequestValidationException("products.invalid_sort_direction", "sortDirection must be 'asc' or 'desc'."),
        };

        ProductListCriteria criteria = new ProductListCriteria(p, size, sort, descending, string.IsNullOrWhiteSpace(category) ? null : category);
        return queries.ListAsync(criteria, ct);
    }
}
