namespace Elsa.Etl.Runtime.Bindings;

/// <summary>Host-controlled origin and exact canonical paths; query values never broaden endpoint authority.</summary>
internal sealed class HttpEndpointPolicy
{
    public HttpEndpointPolicy(string endpoint, IReadOnlyList<string> allowedPaths, bool allowHttp)
    {
        AllowHttp = allowHttp;
        Endpoint = Parse(endpoint);
        if (allowedPaths is null || allowedPaths.Count > 1024)
            throw Unavailable();
        var paths = allowedPaths.Count == 0 ? [Endpoint.AbsolutePath] : allowedPaths;
        AllowedPaths = Array.AsReadOnly(paths.Select(CanonicalPath).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray());
        Validate(Endpoint);
    }

    public Uri Endpoint { get; }
    public IReadOnlyList<string> AllowedPaths { get; }
    public bool AllowHttp { get; }

    public void Validate(Uri uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
            throw Unavailable();
        var parsed = Parse(uri.OriginalString);
        if (!string.Equals(parsed.Scheme, Endpoint.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(parsed.IdnHost, Endpoint.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            parsed.Port != Endpoint.Port || !AllowedPaths.Contains(parsed.AbsolutePath, StringComparer.Ordinal))
            throw Unavailable();
    }

    public Uri Resolve(string value, Uri baseUri)
    {
        Validate(baseUri);
        ValidateText(value);
        if (!Uri.TryCreate(baseUri, value, out var resolved))
            throw Unavailable();
        Validate(resolved);
        return resolved;
    }

    public override string ToString() => nameof(HttpEndpointPolicy);

    private string CanonicalPath(string path)
    {
        ValidateText(path);
        if (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('?') || path.Contains('#'))
            throw Unavailable();
        return Parse(Endpoint.GetLeftPart(UriPartial.Authority) + path).AbsolutePath;
    }

    private Uri Parse(string value)
    {
        ValidateText(value);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps && !(AllowHttp && uri.Scheme == Uri.UriSchemeHttp) ||
            string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw Unavailable();
        // Reject ambiguous path encodings before comparing canonical paths. In particular an intermediary
        // must not reinterpret an encoded slash, backslash or a second percent decoding as a new route.
        var path = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
        for (var i = 0; i + 2 < path.Length; i++)
        {
            if (path[i] != '%') continue;
            if (!byte.TryParse(path.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out var code) ||
                code is 0x2f or 0x5c or < 0x20 or 0x7f)
                throw Unavailable();
            i += 2;
        }
        var decodedPath = Uri.UnescapeDataString(path);
        for (var i = 0; i + 2 < decodedPath.Length; i++)
            if (decodedPath[i] == '%' && char.IsAsciiHexDigit(decodedPath[i + 1]) && char.IsAsciiHexDigit(decodedPath[i + 2]))
                throw Unavailable();
        return uri;
    }

    private static void ValidateText(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 * 1024 ||
            value != value.Trim() || value.Any(char.IsControl) || value.Contains('\\'))
            throw Unavailable();
    }

    private static EtlBindingException Unavailable() =>
        new("etl.http-endpoint-unavailable", "The HTTP request is outside its host-approved endpoint policy.");
}
