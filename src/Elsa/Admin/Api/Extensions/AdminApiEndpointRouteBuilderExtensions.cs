using Elsa.Admin.Api.Contracts;
using Elsa.Admin.Api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Admin.Api.Extensions;

public static class AdminApiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapElsaAdminApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/_elsa/admin/modules", async (HttpContext httpContext) =>
        {
            var response = await httpContext.RequestServices
                .GetRequiredService<IAdminModuleManifestProvider>()
                .GetModules(httpContext.RequestAborted);

            return Results.Ok(response);
        });

        return endpoints;
    }
}
