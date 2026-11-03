using NativeEndpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Api.Capabilities;

/// <summary>Maps the API capabilities surface using ordinary ASP.NET Core endpoints.</summary>
public static class ApiCapabilitiesApi
{
    internal const string OwnerId = "Elsa.Api.Capabilities";

    public static void MapApiCapabilitiesApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The published document tags this surface with the host application name, resolved at
        // composition time exactly as the hand-written mapper did.
        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName;
        var api = endpoints.MapEndpointGroup(
            OwnerId,
            ApiCapabilitiesJsonContext.Default,
            jsonContentType: "application/json",
            tag: string.IsNullOrWhiteSpace(applicationName) ? null : applicationName);

        api.MapEndpointsFrom(typeof(ApiCapabilitiesApi).Assembly);
    }
}
