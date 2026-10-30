using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Auth;
using Shop.Application.Orders;
using Shop.Application.Products;

namespace Shop.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ILoginUseCase, LoginUseCase>();
        services.AddScoped<IListProductsUseCase, ListProductsUseCase>();
        services.AddScoped<IListOrdersUseCase, ListOrdersUseCase>();
        services.AddScoped<IGetOrderUseCase, GetOrderUseCase>();
        services.AddScoped<ICreateOrderUseCase, CreateOrderUseCase>();
        services.AddScoped<IUpdateOrderUseCase, UpdateOrderUseCase>();
        services.AddScoped<IPlaceOrderUseCase, PlaceOrderUseCase>();
        services.AddScoped<IDeleteOrderUseCase, DeleteOrderUseCase>();
        services.AddScoped<IExpireAbandonedDraftsUseCase, ExpireAbandonedDraftsUseCase>();
        return services;
    }
}
