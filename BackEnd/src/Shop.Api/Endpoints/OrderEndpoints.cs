using System.Security.Claims;
using Shop.Api.Common;
using Shop.Application.Abstractions;
using Shop.Application.Common;
using Shop.Application.Orders;

namespace Shop.Api.Endpoints;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder orders = app.MapGroup("/orders")
            .WithTags("Orders")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        orders.MapGet("/", (ClaimsPrincipal user, ListOrdersUseCase listOrders, int? page, int? pageSize, CancellationToken ct) =>
                listOrders.ExecuteAsync(user.GetUserId(), page, pageSize, ct))
            .Produces<PagedResponse<OrderSummaryResponse>>();

        orders.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal user, GetOrderUseCase getOrder, CancellationToken ct) =>
                getOrder.ExecuteAsync(user.GetUserId(), id, ct))
            .WithName("GetOrder")
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        orders.MapPost("/", async (CreateOrderRequest request, ClaimsPrincipal user, CreateOrderUseCase createOrder, CancellationToken ct) =>
            {
                OrderResponse order = await createOrder.ExecuteAsync(user.GetUserId(), request, ct);
                return Results.CreatedAtRoute("GetOrder", new { id = order.Id }, order);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders.MapPatch("/{id:guid}", (Guid id, UpdateOrderRequest request, ClaimsPrincipal user, UpdateOrderUseCase updateOrder, CancellationToken ct) =>
                updateOrder.ExecuteAsync(user.GetUserId(), id, request, ct))
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders.MapPost("/{id:guid}/place", (Guid id, ClaimsPrincipal user, PlaceOrderUseCase placeOrder, CancellationToken ct) =>
                placeOrder.ExecuteAsync(user.GetUserId(), id, ct))
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        orders.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, DeleteOrderUseCase deleteOrder, CancellationToken ct) =>
            {
                await deleteOrder.ExecuteAsync(user.GetUserId(), id, ct);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
