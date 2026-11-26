using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Net.Http.Headers;

namespace Elsa.Studio.Hosting;

/// <summary>Admits file-like SPA document routes while leaving static assets to their existing middleware.</summary>
internal sealed class StudioDocumentNavigationConstraint(IWebHostEnvironment environment) : IRouteConstraint
{
    private static readonly NonFileNameRouteConstraint NonFile = new();

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
    {
        if (httpContext is null || routeDirection != RouteDirection.IncomingRequest || NonFile.Match(httpContext, route, routeKey, values, routeDirection))
            return false;
        // Even a browser's direct navigation to a real asset must keep serving that asset.
        if (environment.WebRootFileProvider.GetFileInfo(httpContext.Request.Path.Value!).Exists)
            return false;
        return MediaTypeHeaderValue.TryParseList(httpContext.Request.Headers.Accept.OfType<string>().ToArray(), out var accepted) &&
            accepted.Any(value => string.Equals(value.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase) && (value.Quality ?? 1) > 0);
    }
}
