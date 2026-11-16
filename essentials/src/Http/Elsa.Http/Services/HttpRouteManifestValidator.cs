using Elsa.Http.Core.Exceptions;
using Elsa.Http.Core.Models;

namespace Elsa.Http.Services;

/// <summary>
/// Validates complete owner-aware route manifests before publication. It deliberately accepts host, module, and
/// dynamic-shell entries so hosts that combine static ASP.NET endpoints with workflow routes can apply one conflict
/// rule at the composition boundary.
/// </summary>
public static class HttpRouteManifestValidator
{
    public static void Validate(IEnumerable<HttpRouteData> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var entries = routes
            .Select(route => new Entry(route, Canonicalize(route.Route), Methods(route)))
            .ToArray();

        for (var i = 0; i < entries.Length; i++)
        {
            for (var j = i + 1; j < entries.Length; j++)
            {
                if (!StringComparer.Ordinal.Equals(entries[i].CanonicalRoute, entries[j].CanonicalRoute))
                    continue;

                var overlap = FindOverlappingMethod(entries[i].Methods, entries[j].Methods);
                if (overlap is null)
                    continue;

                throw new HttpRouteConflictException(
                    entries[i].Route.Route,
                    entries[j].Route.Route,
                    overlap,
                    DescribeOwner(entries[i].Route),
                    DescribeOwner(entries[j].Route));
            }
        }
    }

    /// <summary>
    /// Canonicalizes route templates for collision checks while preserving constraint/default bodies. Parameter
    /// names are not routing identities, but parameter shape, catch-all markers, constraints, and defaults are.
    /// </summary>
    public static string Canonicalize(string route) => Elsa.Http.Core.RouteTemplateIdentity.Canonicalize(route);

    private static IReadOnlyCollection<string> Methods(HttpRouteData route)
    {
        var methods = route.Methods
            .Where(method => !string.IsNullOrWhiteSpace(method))
            .Select(method => method.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Missing method metadata is the pre-1366 compatibility shape: the route is a wildcard for collision
        // purposes because the durable claimant lookup still decides whether the request method is actionable.
        return methods.Length == 0 ? WildcardMethod : methods;
    }

    private static string? FindOverlappingMethod(IReadOnlyCollection<string> first, IReadOnlyCollection<string> second)
    {
        if (first.Contains(WildcardToken, StringComparer.Ordinal))
            return second.Contains(WildcardToken, StringComparer.Ordinal)
                ? WildcardToken
                : second.OrderBy(method => method, StringComparer.Ordinal).FirstOrDefault();

        if (second.Contains(WildcardToken, StringComparer.Ordinal))
            return first.OrderBy(method => method, StringComparer.Ordinal).FirstOrDefault();

        return first
            .Where(method => second.Contains(method, StringComparer.Ordinal))
            .OrderBy(method => method, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string DescribeOwner(HttpRouteData route)
    {
        var owner = route.Metadata.OfType<HttpRouteOwnershipMetadata>().SingleOrDefault();
        return owner?.ToString() ?? "unknown owner";
    }

    private const string WildcardToken = "*";
    private static readonly string[] WildcardMethod = [WildcardToken];

    private sealed record Entry(HttpRouteData Route, string CanonicalRoute, IReadOnlyCollection<string> Methods);
}
