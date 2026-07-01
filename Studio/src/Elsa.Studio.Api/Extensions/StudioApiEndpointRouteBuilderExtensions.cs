using Elsa.Studio.Api.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Api.Extensions;

public static class StudioApiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapElsaStudioApi(this IEndpointRouteBuilder endpoints)
    {
        // Lightweight, unauthenticated reachability probe for the Studio SPA's "Backend API" health tile.
        endpoints.MapGet("/_elsa/health", () => Results.Ok(new { status = "healthy" }));

        endpoints.MapGet("/_elsa/studio/modules", async (HttpContext httpContext) =>
        {
            var response = await httpContext.RequestServices
                .GetRequiredService<IStudioModuleManifestProvider>()
                .GetModules(httpContext.RequestAborted);

            return Results.Ok(response);
        });

        endpoints.MapGet("/_elsa/studio/module-registry", async (HttpContext httpContext) =>
        {
            var response = await httpContext.RequestServices
                .GetRequiredService<IStudioModuleManifestProvider>()
                .GetModuleRegistry(httpContext.RequestAborted);

            return Results.Ok(response);
        });

        return endpoints;
    }
}
