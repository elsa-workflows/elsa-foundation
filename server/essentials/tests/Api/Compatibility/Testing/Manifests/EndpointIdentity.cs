namespace Elsa.Api.Compatibility.Testing.Manifests;

public readonly record struct NormalizedRoute
{
    public NormalizedRoute(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = Normalize(value);
    }

    public string Value { get; }
    public override string ToString() => Value;

    private static string Normalize(string value)
    {
        var segments = value.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeSegment);
        var route = string.Join('/', segments);
        return route.Length == 0 ? "/" : "/" + route;
    }

    private static string NormalizeSegment(string segment)
    {
        if (segment.StartsWith('{') && segment.EndsWith('}'))
        {
            var content = segment[1..^1];
            var prefix = content.StartsWith("**", StringComparison.Ordinal) ? "**"
                : content.StartsWith('*') ? "*"
                : string.Empty;
            var constraint = content.IndexOf(':');
            var suffix = constraint < 0 ? string.Empty : ":" + content[(constraint + 1)..].Trim().ToLowerInvariant();
            return "{" + prefix + "param" + suffix + "}";
        }

        return segment.ToLowerInvariant();
    }
}

public readonly record struct NormalizedHttpMethod
{
    public NormalizedHttpMethod(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToUpperInvariant();
    }

    public string Value { get; }
    public override string ToString() => Value;
}

public readonly record struct EndpointIdentity(NormalizedRoute Route, NormalizedHttpMethod Method)
{
    public EndpointIdentity(string route, string method)
        : this(new NormalizedRoute(route), new NormalizedHttpMethod(method)) { }

    public override string ToString() => $"{Method} {Route}";
}
