using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shop.Application.Abstractions;
using Shop.Application.Common;
using Shop.Application.Orders;
using Shop.Application.Products;
using Shop.Domain.Orders;
using Shop.Domain.Users;
using Shop.Infrastructure.Persistence;

namespace Shop.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ApiTests(ShopApiFixture api)
{
    private static readonly JsonSerializerOptions Json = ShopApiFixture.Json;

    [Fact]
    public async Task Login_with_seeded_user_returns_a_token()
    {
        HttpResponseMessage response = await api.CreateClient().PostAsJsonAsync("/auth/login",
            new { email = "DEMO@shop.test", password = DatabaseSeeder.DemoPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        HttpResponseMessage response = await api.CreateClient().PostAsJsonAsync("/auth/login",
            new { email = DatabaseSeeder.DemoEmail, password = "nope" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Orders_require_authentication()
    {
        HttpResponseMessage response = await api.CreateClient().GetAsync("/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Products_are_paged_and_sorted()
    {
        HttpClient client = api.CreateClient();

        PagedResponse<ProductResponse>? page1 = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/products?page=1&pageSize=5&sortBy=price&sortDirection=desc", Json);
        PagedResponse<ProductResponse>? page2 = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/products?page=2&pageSize=5&sortBy=price&sortDirection=desc", Json);

        Assert.Equal(13, page1!.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(5, page1.Items.Count);
        List<decimal> prices = page1.Items.Concat(page2!.Items).Select(p => p.Price).ToList();
        Assert.Equal(prices.OrderByDescending(p => p), prices);
        Assert.Empty(page1.Items.Select(p => p.Id).Intersect(page2.Items.Select(p => p.Id)));
    }

    [Theory]
    [InlineData("/products?sortBy=password")]
    [InlineData("/products?sortDirection=sideways")]
    [InlineData("/products?pageSize=1000")]
    [InlineData("/products?page=0")]
    public async Task Invalid_product_queries_return_400(string url)
    {
        HttpResponseMessage response = await api.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_order_prices_lines_from_the_catalogue_and_reserves_stock()
    {
        HttpClient client = await api.CreateAuthenticatedClientAsync();
        (ProductResponse? a, ProductResponse? b) = await TwoProductsAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync("/orders", new
        {
            items = new[] { new { productId = a.Id, quantity = 2 }, new { productId = b.Id, quantity = 1 } },
            couponCode = "welcome10",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        OrderResponse? order = await response.Content.ReadFromJsonAsync<OrderResponse>(Json);

        decimal expectedSubtotal = a.Price * 2 + b.Price;
        decimal expectedDiscount = Math.Round(expectedSubtotal * 0.10m, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(OrderStatus.Draft, order!.Status);
        Assert.Equal("WELCOME10", order.CouponCode);
        Assert.Equal(expectedSubtotal, order.Subtotal);
        Assert.Equal(expectedDiscount, order.Discount);
        Assert.Equal(expectedSubtotal - expectedDiscount, order.Total);
        Assert.Equal(response.Headers.Location!.AbsolutePath, $"/orders/{order.Id}");

        Assert.Equal(a.StockQuantity - 2, await StockAsync(a.Id));

        await client.DeleteAsync($"/orders/{order.Id}");
    }

    [Fact]
    public async Task Client_supplied_prices_are_ignored()
    {
        HttpClient client = await api.CreateAuthenticatedClientAsync();
        (ProductResponse? a, ProductResponse _) = await TwoProductsAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync("/orders", new
        {
            items = new[] { new { productId = a.Id, quantity = 1, unitPrice = 0.01m } },
            total = 0.01m,
        });

        OrderResponse? order = await response.Content.ReadFromJsonAsync<OrderResponse>(Json);
        Assert.Equal(a.Price, order!.Total);

        await client.DeleteAsync($"/orders/{order.Id}");
    }

    [Theory]
    [InlineData("""{ "items": [] }""", "order.empty")]
    [InlineData("""{ "items": [ { "productId": "00000000-0000-0000-0000-000000000001", "quantity": 1 } ] }""", "order.product_not_found")]
    [InlineData("""{ "items": [ { "productId": "", "quantity": 1 } ] }""", "request.malformed")]
    [InlineData("""{ "items": """, "request.malformed")]
    public async Task Invalid_orders_are_rejected(string json, string expectedCode)
    {
        HttpClient client = await api.CreateAuthenticatedClientAsync();
        HttpResponseMessage response = await client.PostAsync("/orders", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(expectedCode, await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Ordering_more_than_stock_returns_409()
    {
        HttpClient client = await api.CreateAuthenticatedClientAsync();
        PagedResponse<ProductResponse>? products = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/products?pageSize=100", Json);
        ProductResponse outOfStock = products!.Items.Single(p => p.StockQuantity == 0);

        HttpResponseMessage response = await client.PostAsJsonAsync("/orders", new { items = new[] { new { productId = outOfStock.Id, quantity = 1 } } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("product.insufficient_stock", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Update_merges_replaces_and_handles_coupons()
    {
        HttpClient client = await api.CreateAuthenticatedClientAsync();
        (ProductResponse? a, ProductResponse? b) = await TwoProductsAsync(client);
        OrderResponse order = await CreateOrderAsync(client, (a.Id, 1));

        OrderResponse merged = await PatchAsync(client, order.Id, new { items = new[] { new { productId = a.Id, quantity = 3 }, new { productId = b.Id, quantity = 1 } } });
        Assert.Equal(2, merged.Items.Count);
        Assert.Equal(a.Price * 3 + b.Price, merged.Subtotal);

        OrderResponse withCoupon = await PatchAsync(client, order.Id, new { couponCode = "SAVE20" });
        Assert.Equal(20m, withCoupon.Discount);

        OrderResponse replaced = await PatchAsync(client, order.Id, new { itemsMode = "Replace", items = new[] { new { productId = b.Id, quantity = 1 } }, removeCoupon = true });
        Assert.Single(replaced.Items);
        Assert.Null(replaced.CouponCode);
        Assert.Equal(b.Price, replaced.Total);

        Assert.Equal(a.StockQuantity, await StockAsync(a.Id));

        await client.DeleteAsync($"/orders/{order.Id}");
    }

    [Fact]
    public async Task Expired_coupon_is_rejected_and_order_is_unchanged()
    {
        HttpClient client = await api.CreateAuthenticatedClientAsync();
        (ProductResponse? a, ProductResponse _) = await TwoProductsAsync(client);
        OrderResponse order = await CreateOrderAsync(client, (a.Id, 1));

        HttpResponseMessage response = await client.PatchAsJsonAsync($"/orders/{order.Id}", new { couponCode = "SPRING2024" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("coupon.not_valid", await ErrorCodeAsync(response));
        OrderResponse? reloaded = await client.GetFromJsonAsync<OrderResponse>($"/orders/{order.Id}", Json);
        Assert.Null(reloaded!.CouponCode);

        await client.DeleteAsync($"/orders/{order.Id}");
    }

    [Fact]
    public async Task Placed_orders_cannot_be_changed()
    {
        HttpClient client = await api.CreateAuthenticatedClientAsync();
        (ProductResponse? a, ProductResponse _) = await TwoProductsAsync(client);
        OrderResponse order = await CreateOrderAsync(client, (a.Id, 1));

        OrderResponse? placed = await (await client.PostAsync($"/orders/{order.Id}/place", null)).Content.ReadFromJsonAsync<OrderResponse>(Json);
        Assert.Equal(OrderStatus.Placed, placed!.Status);

        HttpResponseMessage response = await client.PatchAsJsonAsync($"/orders/{order.Id}", new { items = new[] { new { productId = a.Id, quantity = 2 } } });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await client.DeleteAsync($"/orders/{order.Id}");
    }

    [Fact]
    public async Task Delete_soft_deletes_and_releases_stock()
    {
        HttpClient client = await api.CreateAuthenticatedClientAsync();
        (ProductResponse? a, ProductResponse _) = await TwoProductsAsync(client);
        OrderResponse order = await CreateOrderAsync(client, (a.Id, 4));

        HttpResponseMessage delete = await client.DeleteAsync($"/orders/{order.Id}");

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/orders/{order.Id}")).StatusCode);
        Assert.Equal(a.StockQuantity, await StockAsync(a.Id));

        DateTimeOffset? deletedAt = await api.WithDbAsync(db => db.Orders.IgnoreQueryFilters().Where(o => o.Id == order.Id).Select(o => o.DeletedAt).SingleAsync());
        Assert.NotNull(deletedAt);
    }

    [Fact]
    public async Task Users_cannot_see_or_touch_each_others_orders()
    {
        HttpClient owner = await api.CreateAuthenticatedClientAsync();
        (ProductResponse? a, ProductResponse _) = await TwoProductsAsync(owner);
        OrderResponse order = await CreateOrderAsync(owner, (a.Id, 1));

        string otherEmail = $"other-{Guid.NewGuid():N}@shop.test";
        await api.WithDbAsync(async db =>
        {
            IPasswordHasher hasher = api.Services.GetRequiredService<IPasswordHasher>();
            User user = new User(otherEmail, "Other", DateTimeOffset.UtcNow);
            user.SetPasswordHash(hasher.Hash(user, "Other123!"));
            db.Users.Add(user);
            return await db.SaveChangesAsync();
        });
        HttpClient intruder = await api.CreateAuthenticatedClientAsync(otherEmail, "Other123!");

        Assert.Equal(HttpStatusCode.NotFound, (await intruder.GetAsync($"/orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.PatchAsJsonAsync($"/orders/{order.Id}", new { removeCoupon = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await intruder.DeleteAsync($"/orders/{order.Id}")).StatusCode);
        PagedResponse<OrderSummaryResponse>? list = await intruder.GetFromJsonAsync<PagedResponse<OrderSummaryResponse>>("/orders", Json);
        Assert.Empty(list!.Items);

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/orders/{order.Id}")).StatusCode);
        await owner.DeleteAsync($"/orders/{order.Id}");
    }

    [Fact]
    public async Task Sweeper_expires_stale_drafts_and_releases_stock()
    {
        HttpClient client = await api.CreateAuthenticatedClientAsync();
        (ProductResponse? a, ProductResponse? b) = await TwoProductsAsync(client);
        OrderResponse stale = await CreateOrderAsync(client, (a.Id, 2));
        OrderResponse fresh = await CreateOrderAsync(client, (b.Id, 1));

        await api.WithDbAsync(db => db.Orders
            .Where(o => o.Id == stale.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.UpdatedAt, DateTimeOffset.UtcNow.AddHours(-2))));

        int expired = await ExpireAsync();

        Assert.True(expired >= 1);
        Assert.Equal(OrderStatus.Expired, (await client.GetFromJsonAsync<OrderResponse>($"/orders/{stale.Id}", Json))!.Status);
        Assert.Equal(OrderStatus.Draft, (await client.GetFromJsonAsync<OrderResponse>($"/orders/{fresh.Id}", Json))!.Status);
        Assert.Equal(a.StockQuantity, await StockAsync(a.Id));

        await client.DeleteAsync($"/orders/{stale.Id}");
        await client.DeleteAsync($"/orders/{fresh.Id}");
    }

    [Fact]
    public async Task Seeding_is_idempotent()
    {
        (int Users, int Products, int Coupons) before = await CountsAsync();

        await using (AsyncServiceScope scope = api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        }

        Assert.Equal(before, await CountsAsync());
        Assert.Equal(13, before.Products);
        Assert.Equal(3, before.Coupons);
    }

    private async Task<int> ExpireAsync()
    {
        await using AsyncServiceScope scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IExpireAbandonedDraftsUseCase>().ExecuteAsync(TimeSpan.FromMinutes(30), 100, CancellationToken.None);
    }

    private Task<(int Users, int Products, int Coupons)> CountsAsync() =>
        api.WithDbAsync(async db => (
            await db.Users.CountAsync(u => u.Email == DatabaseSeeder.DemoEmail),
            await db.Products.CountAsync(),
            await db.Coupons.CountAsync()));

    private Task<int> StockAsync(Guid productId) =>
        api.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync());

    private static async Task<(ProductResponse A, ProductResponse B)> TwoProductsAsync(HttpClient client)
    {
        PagedResponse<ProductResponse>? page = await client.GetFromJsonAsync<PagedResponse<ProductResponse>>("/products?pageSize=100&sortBy=price", Json);
        List<ProductResponse> candidates = page!.Items.Where(p => p.StockQuantity >= 20 && p.Price >= 30).ToList();
        return (candidates[0], candidates[1]);
    }

    private static async Task<OrderResponse> CreateOrderAsync(HttpClient client, params (Guid ProductId, int Quantity)[] lines)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/orders", new { items = lines.Select(l => new { productId = l.ProductId, quantity = l.Quantity }) });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponse>(Json))!;
    }

    private static async Task<OrderResponse> PatchAsync(HttpClient client, Guid orderId, object body)
    {
        HttpResponseMessage response = await client.PatchAsJsonAsync($"/orders/{orderId}", body);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OrderResponse>(Json))!;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
}
