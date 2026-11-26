namespace Kernel.Native;

/// <summary>What one segment of a route template is.</summary>
public enum HttpRouteSegmentKind { Literal, Parameter, Optional, CatchAll }

/// <summary>A segment of a route template: a literal in lower case, or a parameter's name in lower case.</summary>
public sealed record HttpRouteSegment(HttpRouteSegmentKind Kind, string Text);

/// <summary>
/// An HttpEndpoint's path as a route template: literal segments and whole-segment parameters, <c>{name}</c>,
/// <c>{name?}</c> as the last segment and <c>{*rest}</c> as the last segment. Inline constraints and defaults
/// (<c>{id:int}</c>, <c>{id=1}</c>), parameters inside a segment and percent-escapes in a literal are refused. Literals
/// and names are kept in lower case, so the template's <see cref="Text"/> is its normalized key text; a resolved path
/// keeps its values' case. The model only parses templates and builds
/// keys from them; matching a request against them is the HTTP edge's work.
/// </summary>
public sealed class HttpRouteTemplate
{
    private HttpRouteTemplate(IReadOnlyList<HttpRouteSegment> segments) => Segments = segments;

    public IReadOnlyList<HttpRouteSegment> Segments { get; }

    /// <summary>The normalized template, for example <c>orders/{id}/cancel</c>.</summary>
    public string Text => string.Join('/', Segments.Select(segment => segment.Kind switch
    {
        HttpRouteSegmentKind.Literal => segment.Text,
        HttpRouteSegmentKind.Parameter => "{" + segment.Text + "}",
        HttpRouteSegmentKind.Optional => "{" + segment.Text + "?}",
        _ => "{*" + segment.Text + "}",
    }));

    /// <summary>The template without its parameter names: <c>orders/{id}</c> and <c>orders/{key}</c> have one shape.</summary>
    public string Shape => string.Join('/', Segments.Select(segment => segment.Kind switch
    {
        HttpRouteSegmentKind.Literal => segment.Text,
        HttpRouteSegmentKind.Parameter => "{}",
        HttpRouteSegmentKind.Optional => "{?}",
        _ => "{*}",
    }));

    /// <summary>True when every segment is a literal or a required parameter, so that values resolve it to one path.</summary>
    public bool FullyBound => Segments.All(segment => segment.Kind is HttpRouteSegmentKind.Literal or HttpRouteSegmentKind.Parameter);

    public static HttpRouteTemplate Parse(string template)
    {
        var trimmed = template.Trim().Trim('/');
        if (trimmed.Length == 0) return new([]);
        var parts = trimmed.Split('/');
        var segments = new List<HttpRouteSegment>(parts.Length);
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0) throw new HttpRouteTemplateException($"The route '{template}' has an empty segment.");
            if (part is not ['{', .., '}'])
            {
                if (part.AsSpan().IndexOfAny('{', '}') >= 0)
                    throw new HttpRouteTemplateException($"The route '{template}' has a parameter inside the segment '{part}'; a parameter is a whole segment.");
                if (part is "." or "..")
                    throw new HttpRouteTemplateException($"The route '{template}' has the segment '{part}', which a client removes from its path.");
                if (part.Contains('%'))
                    throw new HttpRouteTemplateException($"The route '{template}' has a percent-escape in '{part}'; write a literal as it reads.");
                segments.Add(new(HttpRouteSegmentKind.Literal, part.ToLowerInvariant()));
                continue;
            }
            var inner = part[1..^1];
            var kind = inner.StartsWith('*') ? HttpRouteSegmentKind.CatchAll : inner.EndsWith('?') ? HttpRouteSegmentKind.Optional : HttpRouteSegmentKind.Parameter;
            var name = kind switch { HttpRouteSegmentKind.CatchAll => inner[1..], HttpRouteSegmentKind.Optional => inner[..^1], _ => inner };
            if (name.Length == 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                throw new HttpRouteTemplateException($"The route '{template}' has the parameter '{part}'; a name is letters, digits and '_', without constraints or defaults.");
            if (kind != HttpRouteSegmentKind.Parameter && i != parts.Length - 1)
                throw new HttpRouteTemplateException($"The route '{template}' has '{part}' before its last segment; an optional or catch-all parameter comes last.");
            if (!names.Add(name.ToLowerInvariant())) throw new HttpRouteTemplateException($"The route '{template}' names the parameter '{name}' twice.");
            segments.Add(new(kind, name.ToLowerInvariant()));
        }
        return new(segments);
    }

    /// <summary>
    /// The path a fully bound template names with these values, each escaped as one segment. A wait subscribes on it, and
    /// the HTTP edge resolves a matched request the same way, so that the two keys meet.
    /// </summary>
    public string Resolve(IReadOnlyDictionary<string, string>? values)
    {
        var parts = new List<string>(Segments.Count);
        foreach (var segment in Segments)
        {
            if (segment.Kind == HttpRouteSegmentKind.Literal) { parts.Add(segment.Text); continue; }
            if (segment.Kind != HttpRouteSegmentKind.Parameter)
                throw new HttpRouteTemplateException($"The route '{Text}' has an optional or catch-all parameter; a wait's route is fully bound.");
            if (values?.FirstOrDefault(value => string.Equals(value.Key, segment.Text, StringComparison.OrdinalIgnoreCase)).Value is not { Length: > 0 } value)
                throw new HttpRouteTemplateException($"The route '{Text}' has no value for '{segment.Text}'.");
            if (value is "." or "..")
                throw new HttpRouteTemplateException($"The route '{Text}' has '{value}' for '{segment.Text}', which a client removes from its path.");
            parts.Add(Uri.EscapeDataString(value));
        }
        return string.Join('/', parts);
    }

    /// <summary>
    /// Which of two templates a request that matches both goes to: the one with a literal where the other has a
    /// parameter, a parameter where the other has an optional one, an optional one where the other has a catch-all,
    /// compared from the first segment; a template that ends where the other goes on is the more specific. Negative when
    /// <paramref name="a"/> is the more specific.
    /// </summary>
    public static int Compare(HttpRouteTemplate a, HttpRouteTemplate b)
    {
        for (var i = 0; i < Math.Min(a.Segments.Count, b.Segments.Count); i++)
            if (Rank(a.Segments[i].Kind).CompareTo(Rank(b.Segments[i].Kind)) is var order and not 0) return order;
        return a.Segments.Count.CompareTo(b.Segments.Count);
    }

    private static int Rank(HttpRouteSegmentKind kind) => kind switch
    {
        HttpRouteSegmentKind.Literal => 0,
        HttpRouteSegmentKind.Parameter => 1,
        HttpRouteSegmentKind.Optional => 2,
        _ => 3,
    };
}

/// <summary>A route template is not one the model accepts, or a wait's route has no value for one of its parameters.</summary>
public sealed class HttpRouteTemplateException(string message) : ArgumentException(message);
