namespace Shop.Application.Common;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class Paging
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize)
    {
        if (page is < 1) throw new RequestValidationException("paging.invalid_page", "page must be 1 or greater.");
        if (pageSize is < 1 or > MaxPageSize)
        {
            throw new RequestValidationException("paging.invalid_page_size", $"pageSize must be between 1 and {MaxPageSize}.");
        }

        return (page ?? 1, pageSize ?? DefaultPageSize);
    }
}
