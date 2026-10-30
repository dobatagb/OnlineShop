using Shop.Application.Auth;

namespace Shop.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", async (LoginRequest request, ILoginUseCase login, CancellationToken ct) =>
                await login.ExecuteAsync(request, ct) is { } response
                    ? Results.Ok(response)
                    : Results.Problem(
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Invalid credentials",
                        detail: "The email or password is incorrect.",
                        extensions: new Dictionary<string, object?> { ["code"] = "auth.invalid_credentials" }))
            .WithTags("Auth")
            .AllowAnonymous()
            .Produces<LoginResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }
}
