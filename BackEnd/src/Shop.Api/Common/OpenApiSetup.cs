using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Shop.Api.Common;

public static class OpenApiSetup
{
    private const string SchemeName = "Bearer";

    public static IServiceCollection AddShopOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Mini Online Shop API";
                document.Info.Description = "Log in with POST /auth/login, then use Authorize with the returned accessToken.";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Paste the accessToken from POST /auth/login.",
                };
                return Task.CompletedTask;
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                IList<object> metadata = context.Description.ActionDescriptor.EndpointMetadata;
                bool requiresAuth = metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();
                if (requiresAuth)
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
                    });
                }

                return Task.CompletedTask;
            });
        });

    public static WebApplication UseShopSwagger(this WebApplication app)
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "Mini Online Shop API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "Mini Online Shop API";
            options.EnablePersistAuthorization();
        });
        return app;
    }
}
