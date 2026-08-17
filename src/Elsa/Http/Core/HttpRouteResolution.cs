using System.Collections.ObjectModel;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;

namespace Elsa.Http.Core;

/// <summary>Resolves a request against an ordered workflow HTTP route generation.</summary>
public static class HttpRouteResolution
{
    /// <summary>
    /// Returns the first method-compatible template match. Callers supply a generation already ordered by route
    /// specificity, preserving the established deterministic first-match behavior.
    /// </summary>
    public static HttpRouteMatch? Resolve(
        IEnumerable<HttpRouteData> routes,
        string endpointPath,
        string method,
        IRouteMatcher routeMatcher)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(routeMatcher);

        var rootedPath = "/" + endpointPath;
        foreach (var route in routes)
        {
            if (string.IsNullOrWhiteSpace(route.Route))
                continue;

            if (route.Methods.Count > 0 && !route.Methods.Contains(method, StringComparer.OrdinalIgnoreCase))
                continue;

            var values = routeMatcher.Match(route, rootedPath);
            if (values is null)
                continue;

            return new HttpRouteMatch(
                route.Route.Trim('/'),
                new ReadOnlyDictionary<string, string>(values.ToDictionary(
                    item => item.Key,
                    item => item.Value?.ToString() ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)));
        }

        return null;
    }
}
