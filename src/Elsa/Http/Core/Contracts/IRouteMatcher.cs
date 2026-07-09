using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Http.Core.Contracts;

/// <summary>
/// Matches a given request path against the specified route template.
/// </summary>
public interface IRouteMatcher
{
    /// <summary>
    /// Matches a given request path against the specified route template. Parses the template on every call —
    /// prefer <see cref="Match(HttpRouteData,string)"/> on the request path so the route table's precompiled
    /// matcher is reused.
    /// </summary>
    RouteValueDictionary? Match(string routeTemplate, string route);

    /// <summary>
    /// Matches a request path against a route table entry, reusing the entry's precompiled matcher
    /// (<see cref="HttpRouteData.CompiledMatcher"/>) when present so no per-request parse/allocation occurs
    /// (spec 089 follow-up, issue #592 item 6). Falls back to parsing <see cref="HttpRouteData.Route"/> for a
    /// bare entry with no precompiled matcher.
    /// </summary>
    RouteValueDictionary? Match(HttpRouteData routeData, string route);
}