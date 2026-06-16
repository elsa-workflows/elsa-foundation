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
