using Microsoft.AspNetCore.Routing;

namespace Elsa.Http.Core.Models;

public class HttpRouteData
{
    public HttpRouteData()
    {
    }

    public HttpRouteData(string route) : this()
    {
        Route = route;
    }

    public HttpRouteData(string route, RouteValueDictionary dataTokens) : this(route)
    {
        DataTokens = dataTokens;
    }

    public HttpRouteData(string route, RouteValueDictionary dataTokens, RouteValueDictionary routeValues) : this(route, dataTokens)
    {
        RouteValues = routeValues;
    }

    public string Route { get; set; } = default!;
    public RouteValueDictionary DataTokens { get; set; } = [];
    public RouteValueDictionary RouteValues { get; set; } = [];

    /// <summary>
    /// An opaque, precompiled matcher for <see cref="Route"/> (spec 089 follow-up, issue #592 item 6). The route
    /// table populates this at refresh time with the parsed <c>TemplateMatcher</c> so per-request matching is a
    /// lookup, not a per-template parse+allocate. Typed as <see cref="object"/> because the concrete matcher type
    /// lives in <c>Microsoft.AspNetCore.Routing</c> (referenced by <c>Elsa.Http</c>, not this contracts package);
    /// <c>IRouteMatcher</c> casts it back. Null on a bare, hand-built route (falls back to parse-on-match).
    /// </summary>
    public object? CompiledMatcher { get; set; }
}