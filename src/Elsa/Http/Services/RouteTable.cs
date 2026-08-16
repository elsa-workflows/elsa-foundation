using System.Collections;
using System.Collections.Concurrent;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Http.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Http.Services;

/// <inheritdoc />
/// <remarks>
/// Route refreshes are candidate-first operations. The candidate is cloned, compiled, enriched with dynamic owner
/// and security metadata, validated, and then published as one cache value. A snapshot lease gives a request a stable
/// generation while a later refresh retires the old snapshot.
/// </remarks>
public sealed class RouteTable : IRouteTable, IRouteTableSnapshotProvider
{
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.Ordinal);

    private readonly string _key;
    private readonly string _shellId;
    private readonly string _ownerId;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RouteTable> _logger;
    private readonly IHttpRouteManifestProvider _staticManifestProvider;

    public RouteTable(
        IMemoryCache cache,
        ILogger<RouteTable> logger,
        IOptions<RouteTableOptions>? options = null,
        IHttpRouteManifestProvider? staticManifestProvider = null)
    {
        _cache = cache;
        _logger = logger;
        _staticManifestProvider = staticManifestProvider ?? new EmptyRouteManifestProvider();
        _shellId = NormalizeShellId(options?.Value.ShellDiscriminator);
        _ownerId = string.IsNullOrWhiteSpace(options?.Value.OwnerId) ? "Elsa.Http" : options.Value.OwnerId.Trim();
        _key = $"Elsa.Http.RouteTable:{_shellId}";
    }

    private object Gate => Gates.GetOrAdd(_key, static _ => new object());

    /// <summary>Returns the current immutable route snapshot, never a mutable cache list.</summary>
    private SnapshotState CurrentStateUnsafe()
    {
        if (_cache.TryGetValue(_key, out SnapshotState? state) && state is not null)
            return state;

        state = new SnapshotState(new HttpRouteTableSnapshot(0, Array.Empty<HttpRouteData>()));
        _cache.Set(_key, state);
        return state;
    }

    public ValueTask Add(string route) => Add(new HttpRouteData(route));

    public ValueTask Add(HttpRouteData httpRouteData)
    {
        ArgumentNullException.ThrowIfNull(httpRouteData);
        if (!IsValidRoute(httpRouteData.Route))
            return ValueTask.CompletedTask;

        lock (Gate)
        {
            var current = CurrentStateUnsafe();
            var normalizedRoute = NormalizeRoute(httpRouteData.Route);
            if (current.Snapshot.Routes.Any(existing => RouteKey(existing) == normalizedRoute))
                throw new InvalidOperationException($"Route '{httpRouteData.Route}' is already added");

            PublishUnsafe(current, current.Snapshot.Routes.Append(httpRouteData), rejectExactDuplicates: true);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask Remove(string route)
    {
        var normalizedRoute = NormalizeRoute(route);
        lock (Gate)
        {
            var current = CurrentStateUnsafe();
            PublishUnsafe(current, current.Snapshot.Routes.Where(existing => RouteKey(existing) != normalizedRoute), rejectExactDuplicates: false);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask AddRange(IEnumerable<string> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var additions = routes
            .Select(route => new HttpRouteData(route))
            .Where(route => IsValidRoute(route.Route))
            .ToArray();
        if (additions.Length == 0)
            return ValueTask.CompletedTask;

        lock (Gate)
        {
            var current = CurrentStateUnsafe();
            PublishUnsafe(current, current.Snapshot.Routes.Concat(additions), rejectExactDuplicates: true);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveRange(IEnumerable<string> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var removals = routes.Select(NormalizeRoute).ToHashSet(StringComparer.Ordinal);
        if (removals.Count == 0)
            return ValueTask.CompletedTask;

        lock (Gate)
        {
            var current = CurrentStateUnsafe();
            PublishUnsafe(current, current.Snapshot.Routes.Where(route => !removals.Contains(RouteKey(route))), rejectExactDuplicates: false);
        }

        return ValueTask.CompletedTask;
    }

    public IEnumerator<HttpRouteData> GetEnumerator()
    {
        lock (Gate)
            return CurrentStateUnsafe().Snapshot.Routes.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public ValueTask Refresh(IEnumerable<string> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        return Refresh(routes.Select(route => new HttpRouteData(route)));
    }

    public ValueTask Refresh(IEnumerable<HttpRouteData> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        lock (Gate)
        {
            var current = CurrentStateUnsafe();
            PublishUnsafe(current, routes, rejectExactDuplicates: false);
        }

        return ValueTask.CompletedTask;
    }

    public HttpRouteTableSnapshotLease AcquireSnapshot()
    {
        lock (Gate)
            return CurrentStateUnsafe().AcquireLease();
    }

    private void PublishUnsafe(SnapshotState current, IEnumerable<HttpRouteData> routes, bool rejectExactDuplicates)
    {
        // All work through enrichment and validation happens before cache.Set. An exception therefore preserves the
        // previous state exactly, including its generation and metadata.
        var compiled = new List<HttpRouteData>();
        foreach (var routeData in routes)
        {
            if (!IsValidRoute(routeData.Route))
                continue;

            var exactDuplicate = compiled.Any(existing => StringComparer.Ordinal.Equals(NormalizeRoute(existing.Route), NormalizeRoute(routeData.Route)));
            if (rejectExactDuplicates && exactDuplicate)
                throw new InvalidOperationException($"Route '{routeData.Route}' is already added");

            // Preserve the pre-1366 Refresh exception contract for the legacy shape while allowing explicit
            // method-disjoint entries to coexist and letting owner-aware validation handle all metadata-bearing
            // collisions. This keeps old callers stable without bypassing the complete manifest rule.
            if (!rejectExactDuplicates && exactDuplicate && routeData.Methods.Count == 0 &&
                compiled.Last(existing => StringComparer.Ordinal.Equals(NormalizeRoute(existing.Route), NormalizeRoute(routeData.Route))).Methods.Count == 0)
                throw new InvalidOperationException($"Route '{routeData.Route}' is already added");

            compiled.Add(Compile(routeData));
        }

        var nextGeneration = checked(current.Snapshot.Generation + 1);
        var enriched = compiled.Select(route => Enrich(route, nextGeneration)).ToArray();
        var staticRoutes = _staticManifestProvider.GetRoutes()
            .Where(route => IsValidRoute(route.Route))
            .Select(Compile)
            .ToArray();

        // Validate the complete composition manifest before touching the cache. The static routes are only a
        // validation input; the dynamic table publishes the enriched workflow routes below.
        HttpRouteManifestValidator.Validate(staticRoutes.Concat(enriched));
        var next = new SnapshotState(new HttpRouteTableSnapshot(nextGeneration, BuildOrdered(enriched)));

        // The cache value is the one publication boundary. Readers obtain either current or next; no clear/add
        // intermediate is observable. Retire after the swap so an already-held lease can drain naturally.
        _cache.Set(_key, next);
        current.Retire();
    }

    private HttpRouteData Enrich(HttpRouteData routeData, long generation)
    {
        var metadata = routeData.Metadata.ToArray();
        var ownership = metadata.OfType<HttpRouteOwnershipMetadata>().ToArray();
        if (ownership.Length > 1)
            throw new InvalidOperationException($"Route '{routeData.Route}' has more than one ownership metadata record.");

        var suppliedOwner = ownership.SingleOrDefault();
        if (suppliedOwner is not null && suppliedOwner.OwnerKind != HttpRouteOwnerKind.DynamicShell)
            throw new InvalidOperationException($"Workflow route '{routeData.Route}' cannot claim static owner '{suppliedOwner}'.");

        if (suppliedOwner?.OwnerKind == HttpRouteOwnerKind.DynamicShell)
        {
            if (!StringComparer.Ordinal.Equals(suppliedOwner.ShellId, _shellId))
                throw new InvalidOperationException($"Route '{routeData.Route}' belongs to shell '{suppliedOwner.ShellId}', but candidate shell '{_shellId}' is being published.");
        }

        // The route table is the trust boundary for workflow-authored ownership. Even a dynamic ownership record
        // supplied by a caller is rewritten to this table's configured owner/shell/generation, preventing a
        // workflow publisher from impersonating another feature or a previous generation.
        var owner = HttpRouteOwnershipMetadata.DynamicShell(_ownerId, _shellId, generation);

        var dispositions = metadata.OfType<HttpRouteSecurityDispositionMetadata>().ToArray();
        if (dispositions.Length > 1)
            throw new InvalidOperationException($"Route '{routeData.Route}' has more than one security disposition.");

        var security = dispositions.SingleOrDefault() ??
                       HttpRouteSecurityDispositionMetadata.Public("compatibility", "Legacy workflow-authored route without explicit disposition.");

        var otherMetadata = metadata
            .Where(value => value is not HttpRouteOwnershipMetadata && value is not HttpRouteSecurityDispositionMetadata)
            .Concat([owner, security])
            .ToArray();

        return Clone(routeData, otherMetadata, routeData.CompiledMatcher);
    }

    private bool IsValidRoute(string route)
    {
        if (!route.Contains("//"))
            return true;

        _logger.LogWarning("Path cannot contain double slashes. Ignoring path: {Path}", route);
        return false;
    }

    private static HttpRouteData Compile(HttpRouteData routeData)
    {
        var normalized = NormalizeRoute(routeData.Route);
        return Clone(routeData, routeData.Metadata, RouteMatcher.Compile(normalized));
    }

    private static HttpRouteData Clone(HttpRouteData routeData, IEnumerable<object> metadata, object? compiledMatcher)
    {
        return new HttpRouteData(
            routeData.Route,
            new Microsoft.AspNetCore.Routing.RouteValueDictionary(routeData.DataTokens),
            new Microsoft.AspNetCore.Routing.RouteValueDictionary(routeData.RouteValues))
        {
            Methods = routeData.Methods
                .Where(method => !string.IsNullOrWhiteSpace(method))
                .Select(method => method.Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(method => method, StringComparer.Ordinal)
                .ToArray(),
            Metadata = metadata.ToArray(),
            CompiledMatcher = compiledMatcher
        };
    }

    private static IReadOnlyList<HttpRouteData> BuildOrdered(IEnumerable<HttpRouteData> routes) =>
        routes.OrderBy(routeData => NormalizeRoute(routeData.Route), RouteTemplateSpecificity.StringComparer).ToArray();

    private static string NormalizeRoute(string path) => $"/{path.Trim('/')}";
    private static string RouteKey(HttpRouteData routeData) => NormalizeRoute(routeData.Route);
    private static string NormalizeShellId(string? value) => string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();

    private sealed class SnapshotState(HttpRouteTableSnapshot snapshot)
    {
        private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _leaseCount;
        private bool _retired;

        public HttpRouteTableSnapshot Snapshot { get; } = snapshot;

        public HttpRouteTableSnapshotLease AcquireLease()
        {
            lock (this)
            {
                _leaseCount++;
                return new HttpRouteTableSnapshotLease(Snapshot, _drained.Task, ReleaseLease);
            }
        }

        public void Retire()
        {
            lock (this)
            {
                _retired = true;
                CompleteIfDrained();
            }
        }

        private void ReleaseLease()
        {
            lock (this)
            {
                if (_leaseCount > 0)
                    _leaseCount--;
                CompleteIfDrained();
            }
        }

        private void CompleteIfDrained()
        {
            if (_retired && _leaseCount == 0)
                _drained.TrySetResult();
        }
    }

    private sealed class EmptyRouteManifestProvider : IHttpRouteManifestProvider
    {
        public IEnumerable<HttpRouteData> GetRoutes() => Array.Empty<HttpRouteData>();
    }

}
