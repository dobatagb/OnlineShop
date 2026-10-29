using Microsoft.Extensions.Logging.Abstractions;
using Shop.Application.Abstractions;
using Shop.Application.Common;
using Shop.Application.Orders;
using Shop.Domain.Coupons;
using Shop.Domain.Orders;
using Shop.Domain.Products;

namespace Shop.Tests.Application;

public class OrderUseCaseTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly FakeOrderRepository _orders = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly Product _keyboard = new("KB-1", "Keyboard", "desc", 80m, 10, "Peripherals", DateTimeOffset.UtcNow);

    private CreateOrderUseCase CreateOrder(params Coupon[] coupons) => new(
        _orders,
        new FakeProductRepository(_keyboard),
        new FakeCouponRepository(coupons),
        _unitOfWork,
        TimeProvider.System,
        NullLogger<CreateOrderUseCase>.Instance);

    [Fact]
    public async Task Create_prices_the_order_from_the_catalogue_and_applies_the_coupon()
    {
        CreateOrderUseCase useCase = CreateOrder(new Coupon("WELCOME10", "10%", DiscountType.Percentage, 10));

        OrderResponse order = await useCase.ExecuteAsync(UserId, new CreateOrderRequest([new(_keyboard.Id, 2)], "welcome10"), default);

        Assert.Equal(160m, order.Subtotal);
        Assert.Equal(16m, order.Discount);
        Assert.Equal(144m, order.Total);
        Assert.Single(_orders.Orders);
        Assert.Equal(1, _unitOfWork.SaveAttempts);
    }

    [Fact]
    public async Task Create_retries_after_a_concurrency_conflict()
    {
        _unitOfWork.FailNextSaves(true);

        OrderResponse order = await CreateOrder().ExecuteAsync(UserId, new CreateOrderRequest([new(_keyboard.Id, 1)], null), default);

        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Equal(2, _unitOfWork.SaveAttempts);
        Assert.Equal(1, _unitOfWork.Discards);
    }

    [Fact]
    public async Task Create_gives_up_after_repeated_conflicts()
    {
        _unitOfWork.FailNextSaves(true, true, true);

        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            CreateOrder().ExecuteAsync(UserId, new CreateOrderRequest([new(_keyboard.Id, 1)], null), default));

        Assert.Equal(3, _unitOfWork.SaveAttempts);
    }

    [Fact]
    public async Task Create_rejects_unknown_products_and_coupons()
    {
        RequestValidationException unknownProduct = await Assert.ThrowsAsync<RequestValidationException>(() =>
            CreateOrder().ExecuteAsync(UserId, new CreateOrderRequest([new(Guid.NewGuid(), 1)], null), default));
        RequestValidationException unknownCoupon = await Assert.ThrowsAsync<RequestValidationException>(() =>
            CreateOrder().ExecuteAsync(UserId, new CreateOrderRequest([new(_keyboard.Id, 1)], "NOPE"), default));

        Assert.Equal("order.product_not_found", unknownProduct.Code);
        Assert.Equal("coupon.not_found", unknownCoupon.Code);
    }

    [Fact]
    public async Task Another_users_order_is_not_found()
    {
        OrderResponse order = await CreateOrder().ExecuteAsync(UserId, new CreateOrderRequest([new(_keyboard.Id, 1)], null), default);
        GetOrderUseCase getOrder = new GetOrderUseCase(_orders);

        await Assert.ThrowsAsync<NotFoundException>(() => getOrder.ExecuteAsync(Guid.NewGuid(), order.Id, default));
    }

    [Fact]
    public async Task Expire_releases_stock_of_stale_drafts_and_skips_conflicts()
    {
        DateTimeOffset longAgo = DateTimeOffset.UtcNow.AddHours(-2);
        Order stale = NewDraft(longAgo, 3);
        Order conflicting = NewDraft(longAgo.AddMinutes(1), 2);
        Order fresh = NewDraft(DateTimeOffset.UtcNow, 1);
        _orders.Orders.AddRange([stale, conflicting, fresh]);
        _unitOfWork.FailNextSaves(false, true);

        ExpireAbandonedDraftsUseCase useCase = new ExpireAbandonedDraftsUseCase(_orders, _unitOfWork, TimeProvider.System, NullLogger<ExpireAbandonedDraftsUseCase>.Instance);
        int expired = await useCase.ExecuteAsync(TimeSpan.FromMinutes(30), 100, default);

        Assert.Equal(1, expired);
        Assert.Equal(OrderStatus.Expired, stale.Status);
        Assert.Equal(OrderStatus.Draft, fresh.Status);
        Assert.Equal(2, _unitOfWork.SaveAttempts);
    }

    private Order NewDraft(DateTimeOffset at, int quantity)
    {
        Order order = new Order(UserId, at);
        order.SetItemQuantity(_keyboard, quantity, at);
        order.Recalculate(at);
        return order;
    }
}
