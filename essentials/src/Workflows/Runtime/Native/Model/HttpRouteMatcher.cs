namespace Kernel.Native;

/// <summary>Where a request goes: a start route, a wait route's resolved key, or (no template matched) its literal path's key.</summary>
public sealed record NativeHttpRouteMatch(HttpRoute? Route, string Key, IReadOnlyDictionary<string, string> Values);

/// <summary>
/// An HTTP edge's route matcher over <see cref="HttpRoutes"/>, which keeps templates and keys: a request path is matched
/// segment by segment against the routes of its method, most specific first; literals ignore case, and a parameter takes
/// one decoded segment, a catch-all the rest.
/// </summary>
public static class HttpRouteMatcher
{
    public static NativeHttpRouteMatch Match(HttpRoutes routes, string method, string path)
    {
        var verb = method.ToUpperInvariant();
        var segments = path.Trim().Trim('/') is { Length: > 0 } trimmed ? trimmed.Split('/').Select(Uri.UnescapeDataString).ToArray() : [];
        foreach (var route in routes.Routes)
        {
            if (route.Method != verb || !TryMatch(route.Template, segments, out var values)) continue;
            return new(route, route.Start ? HttpRoutes.Key(verb, route.Template.Text) : HttpRoutes.WaitKey(verb, route.Template, values), values);
        }
        return Literal(verb, segments);
    }

    /// <summary>
    /// The request as a literal route: a wait whose path an expression computes is known only at run time, so its key is
    /// its literal path's, decoded as a template match decodes it. An edge tries it when no template matched, and when a
    /// wait template matched but nothing waits on its key (the computed wait may share that template's shape).
    /// </summary>
    public static NativeHttpRouteMatch Literal(string method, string path) =>
        Literal(method.ToUpperInvariant(), path.Trim().Trim('/') is { Length: > 0 } trimmed ? trimmed.Split('/').Select(Uri.UnescapeDataString).ToArray() : []);

    private static NativeHttpRouteMatch Literal(string verb, string[] segments) =>
        new(null, HttpRoutes.Key(verb, string.Join('/', segments)), new Dictionary<string, string>());

    // A parameter takes a segment a client keeps: '.' and '..' are path steps, not values (HttpRouteTemplate.Resolve
    // refuses them, so a wait cannot be waiting on one).
    private static bool IsValue(string segment) => segment.Length > 0 && segment is not ("." or "..");

    private static bool TryMatch(HttpRouteTemplate template, string[] segments, out IReadOnlyDictionary<string, string> values)
    {
        var matched = new Dictionary<string, string>(StringComparer.Ordinal);
        values = matched;
        var i = 0;
        foreach (var segment in template.Segments)
        {
            switch (segment.Kind)
            {
                case HttpRouteSegmentKind.Literal when i < segments.Length && string.Equals(segments[i], segment.Text, StringComparison.OrdinalIgnoreCase):
                    i++;
                    break;
                case HttpRouteSegmentKind.Parameter when i < segments.Length && IsValue(segments[i]):
                    matched[segment.Text] = segments[i++];
                    break;
                case HttpRouteSegmentKind.Optional when i >= segments.Length:
                    break;
                case HttpRouteSegmentKind.Optional when IsValue(segments[i]):
                    matched[segment.Text] = segments[i++];
                    break;
                case HttpRouteSegmentKind.CatchAll:
                    matched[segment.Text] = string.Join('/', segments[i..]);
                    i = segments.Length;
                    break;
                default:
                    return false;
            }
        }
        return i == segments.Length;
    }
}
