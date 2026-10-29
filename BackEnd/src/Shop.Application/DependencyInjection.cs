using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Auth;
using Shop.Application.Orders;
using Shop.Application.Products;

namespace Shop.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<LoginUseCase>();
        services.AddScoped<ListProductsUseCase>();
        services.AddScoped<ListOrdersUseCase>();
        services.AddScoped<GetOrderUseCase>();
        services.AddScoped<CreateOrderUseCase>();
        services.AddScoped<UpdateOrderUseCase>();
        services.AddScoped<PlaceOrderUseCase>();
        services.AddScoped<DeleteOrderUseCase>();
        services.AddScoped<ExpireAbandonedDraftsUseCase>();
        return services;
    }
}
