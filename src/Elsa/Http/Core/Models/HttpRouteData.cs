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
    /// The methods claimed by this route. An empty collection is the compatibility wildcard used by route data
    /// authored before method metadata was introduced; it means that request-time method ownership is resolved by
    /// the durable workflow claimant lookup.
    /// </summary>
    public IReadOnlyCollection<string> Methods { get; set; } = [];

    /// <summary>
    /// Immutable route-publication metadata. Dynamic route publication fills this with one ownership record and one
    /// security-disposition record before the route enters a published snapshot. The collection is intentionally
    /// typed as objects so the lower HTTP contract layer does not depend on a higher endpoint-adapter package.
    /// </summary>
    public IReadOnlyCollection<object> Metadata { get; set; } = [];

    /// <summary>
    /// An opaque, precompiled matcher for <see cref="Route"/> (spec 089 follow-up, issue #592 item 6). The route
    /// table populates this at refresh time with the parsed <c>TemplateMatcher</c> so per-request matching is a
    /// lookup, not a per-template parse+allocate. Typed as <see cref="object"/> because the concrete matcher type
    /// lives in <c>Microsoft.AspNetCore.Routing</c> (referenced by <c>Elsa.Http</c>, not this contracts package);
    /// <c>IRouteMatcher</c> casts it back. Null on a bare, hand-built route (falls back to parse-on-match).
    /// </summary>
    public object? CompiledMatcher { get; set; }
}
