using Elsa.Http.Core.Contracts;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Http.Core.Models;

/// <summary>
/// An immutable, ordered description of workflow HTTP routes published as one generation. Inspection returns
/// defensive route copies so the legacy mutable <see cref="HttpRouteData"/> input shape cannot mutate authority.
/// Production request matching uses the generation resolver held by <see cref="HttpRouteTableSnapshotLease"/>.
/// </summary>
public sealed class HttpRouteTableSnapshot
{
    private readonly IReadOnlyList<HttpRouteData> _routes;

    public HttpRouteTableSnapshot(long generation, IEnumerable<HttpRouteData> routes)
    {
        if (generation < 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        ArgumentNullException.ThrowIfNull(routes);

        Generation = generation;
        _routes = Array.AsReadOnly(routes.Select(Clone).ToArray());
    }

    public long Generation { get; }

    /// <summary>
    /// Returns defensive route copies for diagnostics and compatibility enumeration. Mutating a returned route or
    /// one of its dictionaries cannot alter this snapshot or the authoritative generation used for request routing.
    /// </summary>
    public IReadOnlyList<HttpRouteData> Routes => Array.AsReadOnly(_routes.Select(Clone).ToArray());

    private static HttpRouteData Clone(HttpRouteData route) => new(
        route.Route,
        new RouteValueDictionary(route.DataTokens),
        new RouteValueDictionary(route.RouteValues))
    {
        Methods = Array.AsReadOnly(route.Methods.ToArray()),
        Metadata = Array.AsReadOnly(route.Metadata.ToArray()),
        CompiledMatcher = route.CompiledMatcher
    };
}

/// <summary>An immutable result from matching a request against one exact route generation.</summary>
public sealed record HttpRouteMatch(string Template, IReadOnlyDictionary<string, string> RouteValues);

/// <summary>
/// A request-owned lease over an exact route-table generation. The lease's drain task completes once the snapshot
/// has been replaced and this request has released its reference.
/// </summary>
public sealed class HttpRouteTableSnapshotLease : IDisposable
{
    private Action? _release;
    private Func<string, string, IRouteMatcher, HttpRouteMatch?>? _routeResolver;

    public HttpRouteTableSnapshotLease(HttpRouteTableSnapshot snapshot, Task drained, Action release)
        : this(snapshot, drained, release, null)
    {
    }

    public HttpRouteTableSnapshotLease(
        HttpRouteTableSnapshot snapshot,
        Task drained,
        Action release,
        Func<string, string, IRouteMatcher, HttpRouteMatch?>? routeResolver)
    {
        Snapshot = snapshot;
        Drained = drained;
        _release = release;
        _routeResolver = routeResolver;
    }

    public HttpRouteTableSnapshot Snapshot { get; }
    public Task Drained { get; }

    /// <summary>
    /// True when this lease can resolve requests directly against its private authoritative generation. Custom
    /// snapshot providers using the legacy constructor retain the enumerable compatibility path.
    /// </summary>
    public bool SupportsRouteResolution => Volatile.Read(ref _routeResolver) is not null;

    /// <summary>Resolves a request against the exact generation retained by this lease.</summary>
    public HttpRouteMatch? ResolveRoute(string endpointPath, string method, IRouteMatcher routeMatcher)
    {
        var routeResolver = Volatile.Read(ref _routeResolver);
        if (routeResolver is null)
            throw new InvalidOperationException("This snapshot lease does not provide authoritative route resolution.");

        return routeResolver(endpointPath, method, routeMatcher);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _routeResolver, null);
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
