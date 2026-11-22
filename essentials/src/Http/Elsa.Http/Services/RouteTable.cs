using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Http.Core.Options;
using Elsa.Http.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections;

namespace Elsa.Http.Services;

/// <inheritdoc />
/// <remarks>
/// Route refreshes are candidate-first operations. The candidate is cloned, compiled, enriched with dynamic owner
/// and security metadata, validated, and then published as one shell-owned state value. A snapshot lease gives a
/// request a stable generation while a later refresh retires the old snapshot.
/// </remarks>
public sealed class RouteTable : IRouteTable, IRouteTableSnapshotProvider
{
    private readonly string _shellId;
    private readonly string _ownerId;
    private readonly string _publicationBasePath;
    private readonly ILogger<RouteTable> _logger;
    private readonly IHttpRouteManifestProvider _staticManifestProvider;
    private readonly RouteTableState _state;

    public RouteTable(
        IMemoryCache cache,
        ILogger<RouteTable> logger,
        IOptions<RouteTableOptions>? options = null,
        IHttpRouteManifestProvider? staticManifestProvider = null,
        IOptions<HttpRoutePublicationOptions>? publicationOptions = null)
        : this(new RouteTableState(), logger, options, staticManifestProvider, publicationOptions)
    {
        ArgumentNullException.ThrowIfNull(cache);
    }

    internal RouteTable(
        RouteTableState state,
        ILogger<RouteTable> logger,
        IOptions<RouteTableOptions>? options,
        IHttpRouteManifestProvider? staticManifestProvider,
        IOptions<HttpRoutePublicationOptions>? publicationOptions)
    {
        _state = state;
        _logger = logger;
        _staticManifestProvider = staticManifestProvider ?? new EmptyRouteManifestProvider();
        _shellId = NormalizeShellId(options?.Value.ShellDiscriminator);
        var ownerId = options?.Value.OwnerId;
        _ownerId = string.IsNullOrWhiteSpace(ownerId) ? "Elsa.Http" : ownerId.Trim();
        _publicationBasePath = publicationOptions?.Value.BasePath ?? new HttpRoutePublicationOptions().BasePath;
    }

    private object Gate => _state.Gate;

    /// <summary>Returns the current immutable route snapshot, never a mutable cache list.</summary>
    private RouteGenerationState CurrentStateUnsafe() => _state.Current;

    public ValueTask Add(string route) => Add(new HttpRouteData(route));

    public ValueTask Add(HttpRouteData httpRouteData)
    {
        ArgumentNullException.ThrowIfNull(httpRouteData);
        if (!IsValidRoute(httpRouteData.Route))
            return ValueTask.CompletedTask;

        lock (Gate)
        {
            var current = CurrentStateUnsafe();
            PublishUnsafe(current, current.Routes.Append(httpRouteData), preserveLegacyWildcardDuplicateError: true);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask Remove(string route)
    {
        var normalizedRoute = NormalizeRoute(route);
        lock (Gate)
        {
            var current = CurrentStateUnsafe();
            PublishUnsafe(current, current.Routes.Where(existing => RouteKey(existing) != normalizedRoute), preserveLegacyWildcardDuplicateError: false);
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
            PublishUnsafe(current, current.Routes.Concat(additions), preserveLegacyWildcardDuplicateError: true);
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
            PublishUnsafe(current, current.Routes.Where(route => !removals.Contains(RouteKey(route))), preserveLegacyWildcardDuplicateError: false);
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
            PublishUnsafe(current, routes, preserveLegacyWildcardDuplicateError: true);
        }

        return ValueTask.CompletedTask;
    }

    public HttpRouteTableSnapshotLease AcquireSnapshot()
    {
        lock (Gate)
            return CurrentStateUnsafe().AcquireLease();
    }

    private void PublishUnsafe(RouteGenerationState current, IEnumerable<HttpRouteData> routes, bool preserveLegacyWildcardDuplicateError)
    {
        // All work through enrichment and validation happens before the state swap. An exception therefore preserves the
        // previous state exactly, including its generation and metadata.
        var compiled = new List<HttpRouteData>();
        foreach (var routeData in routes)
        {
            if (!IsValidRoute(routeData.Route))
                continue;

            var exactDuplicate = compiled.Any(existing => StringComparer.Ordinal.Equals(NormalizeRoute(existing.Route), NormalizeRoute(routeData.Route)));
            // Preserve the pre-1366 Add/Refresh exception contract for the legacy methodless shape while allowing
            // explicit method-disjoint entries coexist and letting owner-aware validation handle explicit-method
            // collisions. This keeps old callers stable without bypassing the complete manifest rule.
            if (preserveLegacyWildcardDuplicateError && exactDuplicate && routeData.Methods.Count == 0 &&
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

        // Workflow routes stay endpoint-relative in the route table, but collision coordinates are the real
        // external paths under the activity middleware's configured base path.
        if (HttpRoutePublicationAddress.IsEnabled(_publicationBasePath))
        {
            var publishedDynamicRoutes = enriched.Select(ToPublicationManifestRoute).ToArray();
            HttpRouteManifestValidator.Validate(staticRoutes.Concat(publishedDynamicRoutes));
        }
        else
        {
            // An empty/root base path disables workflow HTTP publication. Retain dynamic/static internal
            // validation without inventing an external address that could collide with a live host endpoint.
            HttpRouteManifestValidator.Validate(staticRoutes);
            HttpRouteManifestValidator.Validate(enriched);
        }

        var next = new RouteGenerationState(nextGeneration, BuildOrdered(enriched));

        // The shell-owned state reference is the one publication boundary. Readers obtain either current or next;
        // no clear/add intermediate is observable. Retire after the swap so held leases drain naturally.
        _state.Current = next;
        current.Retire();
    }

    private HttpRouteData ToPublicationManifestRoute(HttpRouteData route)
    {
        if (!HttpRoutePublicationAddress.TryResolve(_publicationBasePath, route.Route, out var address))
            throw new InvalidOperationException("Workflow route publication is disabled and has no external address.");

        return new HttpRouteData(
            address,
            new Microsoft.AspNetCore.Routing.RouteValueDictionary(route.DataTokens),
            new Microsoft.AspNetCore.Routing.RouteValueDictionary(route.RouteValues))
        {
            Methods = route.Methods,
            Metadata = route.Metadata,
            CompiledMatcher = route.CompiledMatcher
        };
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

        if (suppliedOwner?.OwnerKind == HttpRouteOwnerKind.DynamicShell &&
            !StringComparer.Ordinal.Equals(suppliedOwner.ShellId, _shellId))
            throw new InvalidOperationException($"Route '{routeData.Route}' belongs to shell '{suppliedOwner.ShellId}', but candidate shell '{_shellId}' is being published.");

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
            Methods = Array.AsReadOnly(routeData.Methods
                .Where(method => !string.IsNullOrWhiteSpace(method))
                .Select(method => method.Trim().ToUpperInvariant())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(method => method, StringComparer.Ordinal)
                .ToArray()),
            Metadata = Array.AsReadOnly(metadata.ToArray()),
            CompiledMatcher = compiledMatcher
        };
    }

    private static IReadOnlyList<HttpRouteData> BuildOrdered(IEnumerable<HttpRouteData> routes) =>
        routes.OrderBy(routeData => NormalizeRoute(routeData.Route), RouteTemplateSpecificity.StringComparer).ToArray();

    private static string NormalizeRoute(string path) => $"/{path.Trim('/')}";
    private static string RouteKey(HttpRouteData routeData) => NormalizeRoute(routeData.Route);
    private static string NormalizeShellId(string? value) => string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();

    private sealed class EmptyRouteManifestProvider : IHttpRouteManifestProvider
    {
        public IEnumerable<HttpRouteData> GetRoutes() => Array.Empty<HttpRouteData>();
    }

}
