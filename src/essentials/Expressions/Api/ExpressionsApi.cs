using NativeEndpoints;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Expressions.Api;

/// <summary>Maps the expression descriptor surfaces using ordinary ASP.NET Core endpoints.</summary>
public static class ExpressionsApi
{
    internal const string OwnerId = "Elsa.Expressions.Api";

    public static void MapExpressionsApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The published documents tag this surface with the host application name, resolved at
        // composition time exactly as the hand-written mapper did.
        var applicationName = endpoints.ServiceProvider.GetService<IHostEnvironment>()?.ApplicationName;
        var api = endpoints.MapEndpointGroup(
            OwnerId,
            ExpressionsJsonContext.Default,
            jsonContentType: "application/json",
            tag: string.IsNullOrWhiteSpace(applicationName) ? null : applicationName);

        api.MapEndpointsFrom(typeof(ExpressionsApi).Assembly);
    }
}
