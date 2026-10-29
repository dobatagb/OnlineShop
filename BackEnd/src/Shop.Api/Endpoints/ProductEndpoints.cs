using Shop.Application.Common;
using Shop.Application.Products;

namespace Shop.Api.Endpoints;

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/products", (
                ListProductsUseCase listProducts,
                int? page,
                int? pageSize,
                string? sortBy,
                string? sortDirection,
                string? category,
                CancellationToken ct) => listProducts.ExecuteAsync(page, pageSize, sortBy, sortDirection, category, ct))
            .WithTags("Products")
            .AllowAnonymous()
            .Produces<PagedResponse<ProductResponse>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }
}
