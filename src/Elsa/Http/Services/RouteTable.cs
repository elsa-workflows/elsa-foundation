using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Concurrent;

namespace Elsa.Http.Services;

/// <inheritdoc />
// Public for direct unit-test construction: InternalsVisibleTo is guard-forbidden outside the documented
// allow-list, matching the precedent of the other public default services on this surface.
public sealed class RouteTable(IMemoryCache cache, ILogger<RouteTable> logger) : IRouteTable
{
    private static readonly object Key = new();

    private ConcurrentDictionary<string, HttpRouteData> Routes => cache.GetOrCreate(Key, _ => new ConcurrentDictionary<string, HttpRouteData>())!;

    /// <inheritdoc />
    public ValueTask Add(string route)
    {
        return Add(new HttpRouteData(route));
    }

    /// <inheritdoc />
    public ValueTask Add(HttpRouteData httpRouteData)
    {
        var route = httpRouteData.Route;
        var normalizedRoute = NormalizeRoute(route);

        if (route.Contains("//"))
        {
            logger.LogWarning("Path cannot contain double slashes. Ignoring path: {Path}", route);
            return ValueTask.CompletedTask;
        }

        if (Routes.ContainsKey(normalizedRoute))
        {
            throw new InvalidOperationException($"Route '{route}' is already added");
        }

        Routes.TryAdd(normalizedRoute, httpRouteData);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask Remove(string route)
    {
        var normalizedRoute = NormalizeRoute(route);
        Routes.TryRemove(normalizedRoute, out _);

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
    public IEnumerator<HttpRouteData> GetEnumerator() => Routes.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static string NormalizeRoute(string path) => $"/{path.Trim('/')}";

    public ValueTask Refresh(IEnumerable<string> routes)
    {
        return Refresh(routes.Select(route => new HttpRouteData(route)));
    }

    public ValueTask Refresh(IEnumerable<HttpRouteData> routes)
    {
        // Build a complete NEW table off to the side, then publish it in a single cache.Set. Readers go through
        // the Routes getter on every access, so they observe either the old table or the fully-built new one —
        // never the empty/partial intermediate a Clear()+Add loop would expose (which caused transient 404s during
        // any publish). Build-time duplicates still surface (below) but abort the swap, leaving the live table
        // intact rather than half-destroyed.
        var newRoutes = new ConcurrentDictionary<string, HttpRouteData>();

        foreach (var httpRouteData in routes)
        {
            var route = httpRouteData.Route;

            if (route.Contains("//"))
            {
                logger.LogWarning("Path cannot contain double slashes. Ignoring path: {Path}", route);
                continue;
            }

            var normalizedRoute = NormalizeRoute(route);

            if (!newRoutes.TryAdd(normalizedRoute, httpRouteData))
                throw new InvalidOperationException($"Route '{route}' is already added");
        }

        cache.Set(Key, newRoutes);
        return ValueTask.CompletedTask;
    }
}