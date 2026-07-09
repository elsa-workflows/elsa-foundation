using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Http.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections;

namespace Elsa.Http.Services;

/// <inheritdoc />
// Public for direct unit-test construction: InternalsVisibleTo is guard-forbidden outside the documented
// allow-list, matching the precedent of the other public default services on this surface.
public sealed class RouteTable : IRouteTable
{
    // Namespace the cache key per shell (issue #592 item 5): isolation no longer relies on IMemoryCache being
    // per-shell — a shell discriminator makes the key unique so a future root-promoted, shell-shared cache can't
    // alias two shells' route tables onto one entry.
    private readonly string _key;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RouteTable> _logger;

    public RouteTable(IMemoryCache cache, ILogger<RouteTable> logger, IOptions<RouteTableOptions>? options = null)
    {
        _cache = cache;
        _logger = logger;
        _key = $"Elsa.Http.RouteTable:{options?.Value.ShellDiscriminator ?? string.Empty}";
    }

    // The table is held as an immutable, specificity-ordered snapshot (issue #592 items 1 + 6): entries are ordered
    // most-specific-first so the middleware's "first match wins" is deterministic, and each carries a precompiled
    // TemplateMatcher so request-time matching is lookup + match, never parse. Mutations rebuild and re-publish the
    // snapshot in a single cache.Set — the same atomic-swap the B6 review fix introduced (a reader observes either
    // the old snapshot or the fully-built new one, never an empty/partial intermediate).
    private IReadOnlyList<HttpRouteData> Snapshot => _cache.GetOrCreate(_key, _ => (IReadOnlyList<HttpRouteData>)[])!;

    /// <inheritdoc />
    public ValueTask Add(string route) => Add(new HttpRouteData(route));

    /// <inheritdoc />
    public ValueTask Add(HttpRouteData httpRouteData)
    {
        if (!IsValidRoute(httpRouteData.Route))
            return ValueTask.CompletedTask;

        var normalizedRoute = NormalizeRoute(httpRouteData.Route);
        var current = Snapshot;

        if (current.Any(existing => RouteKey(existing) == normalizedRoute))
            throw new InvalidOperationException($"Route '{httpRouteData.Route}' is already added");

        Publish(BuildOrdered(current.Append(Compile(httpRouteData))));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask Remove(string route)
    {
        var normalizedRoute = NormalizeRoute(route);
        Publish(Snapshot.Where(existing => RouteKey(existing) != normalizedRoute).ToArray());
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask AddRange(IEnumerable<string> routes)
    {
        foreach (var route in routes)
            await Add(route);
    }

    /// <inheritdoc />
    public async ValueTask RemoveRange(IEnumerable<string> routes)
    {
        foreach (var route in routes)
            await Remove(route);
    }

    /// <inheritdoc />
    public IEnumerator<HttpRouteData> GetEnumerator() => Snapshot.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public ValueTask Refresh(IEnumerable<string> routes) => Refresh(routes.Select(route => new HttpRouteData(route)));

    public ValueTask Refresh(IEnumerable<HttpRouteData> routes)
    {
        // Build the complete NEW ordered+compiled snapshot off to the side, then publish it in a single cache.Set.
        // Build-time duplicates abort the swap (throwing below) leaving the live table intact rather than
        // half-destroyed. Ordering + precompilation happen here so per-request matching pays neither cost.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var compiled = new List<HttpRouteData>();

        foreach (var httpRouteData in routes)
        {
            if (!IsValidRoute(httpRouteData.Route))
                continue;

            if (!seen.Add(NormalizeRoute(httpRouteData.Route)))
                throw new InvalidOperationException($"Route '{httpRouteData.Route}' is already added");

            compiled.Add(Compile(httpRouteData));
        }

        Publish(BuildOrdered(compiled));
        return ValueTask.CompletedTask;
    }

    private void Publish(IReadOnlyList<HttpRouteData> snapshot) => _cache.Set(_key, snapshot);

    private bool IsValidRoute(string route)
    {
        if (!route.Contains("//"))
            return true;

        _logger.LogWarning("Path cannot contain double slashes. Ignoring path: {Path}", route);
        return false;
    }

    /// <summary>Precompiles the entry's <see cref="TemplateMatcher"/> so request-time matching skips the parse (item 6).</summary>
    private static HttpRouteData Compile(HttpRouteData routeData)
    {
        routeData.CompiledMatcher = RouteMatcher.Compile(NormalizeRoute(routeData.Route));
        return routeData;
    }

    /// <summary>Orders entries most-specific-first so the middleware's first-match-wins is deterministic (item 1).</summary>
    private static IReadOnlyList<HttpRouteData> BuildOrdered(IEnumerable<HttpRouteData> routes) =>
        routes.OrderBy(routeData => NormalizeRoute(routeData.Route), RouteTemplateSpecificity.StringComparer).ToArray();

    private static string NormalizeRoute(string path) => $"/{path.Trim('/')}";
    private static string RouteKey(HttpRouteData routeData) => NormalizeRoute(routeData.Route);
}
