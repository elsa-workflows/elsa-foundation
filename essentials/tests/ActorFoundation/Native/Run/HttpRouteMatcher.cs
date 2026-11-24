namespace Kernel.Native.Run;

/// <summary>Where a request goes: a start route, a wait route's resolved key, or (no template matched) its literal path's key.</summary>
public sealed record HttpRouteMatch(HttpRoute? Route, string Key, IReadOnlyDictionary<string, string> Values);

/// <summary>
/// The test edges' route matcher: the model keeps templates and keys and no matcher (the HTTP adapter's work). A request
/// path is matched segment by segment against the routes of its method, most specific first; literals ignore case, and a
/// parameter takes one decoded segment, a catch-all the rest.
/// </summary>
public static class HttpRouteMatcher
{
    public static HttpRouteMatch Match(HttpRoutes routes, string method, string path)
    {
        var verb = method.ToUpperInvariant();
        var segments = path.Trim().Trim('/') is { Length: > 0 } trimmed ? trimmed.Split('/').Select(Uri.UnescapeDataString).ToArray() : [];
        foreach (var route in routes.Routes)
        {
            if (route.Method != verb || !TryMatch(route.Template, segments, out var values)) continue;
            return new(route, route.Start ? HttpRoutes.Key(verb, route.Template.Text) : HttpRoutes.WaitKey(verb, route.Template, values), values);
        }
        // A wait whose path an expression computes is known only at run time: its key is its literal path's, decoded as a
        // template match decodes it.
        return new(null, HttpRoutes.Key(verb, string.Join('/', segments)), new Dictionary<string, string>());
    }

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
                case HttpRouteSegmentKind.Parameter when i < segments.Length && segments[i].Length > 0:
                    matched[segment.Text] = segments[i++];
                    break;
                case HttpRouteSegmentKind.Optional:
                    if (i < segments.Length) matched[segment.Text] = segments[i++];
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
