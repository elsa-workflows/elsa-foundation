using Elsa.Api.Endpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Modularity.Api.Endpoints;

/// <summary>Maps the module-management surface using ordinary ASP.NET Core endpoints.</summary>
public static class ModularityApi
{
    internal const string OwnerId = "Elsa.Modularity.Api";

    public static void MapModularityApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The published documents tag this surface with the host application name, resolved at
        // composition time exactly as the hand-written mapper did.
        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName
                              ?? typeof(ModularityApi).Assembly.GetName().Name!;
        var api = endpoints.MapModuleEndpoints(
            OwnerId,
            ModularityJsonContext.Default,
            jsonContentType: "application/json; charset=utf-8",
            tag: applicationName);

        api.MapEndpointsFrom(typeof(ModularityApi).Assembly);
    }
}

internal sealed record ModularityError(
    Dictionary<string, string[]> Errors,
    string Message,
    int StatusCode);
