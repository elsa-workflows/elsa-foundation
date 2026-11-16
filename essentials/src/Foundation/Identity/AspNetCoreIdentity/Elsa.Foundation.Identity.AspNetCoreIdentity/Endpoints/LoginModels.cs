using System.Linq;

namespace Elsa.Foundation.Identity.AspNetCoreIdentity.Endpoints;

/// <summary>Request body for <c>POST /_elsa/identity/login</c>.</summary>
public sealed record LoginRequest
{
    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string? TenantId { get; init; }

    /// <summary>
    /// Optional local return URL. When present (and local) after a successful HTML-form post, the endpoint
    /// issues a 302 redirect to it instead of returning JSON. Non-local values are rejected to prevent open
    /// redirects.
    /// </summary>
    public string? ReturnUrl { get; init; }
}

/// <summary>
/// Guards against open redirects: same-origin, root-relative paths (e.g. <c>/studio</c>) are always
/// honoured. Absolute URLs are honoured only when their origin is on an explicit trust list (the cross-origin
/// Studio host); everything else — foreign absolute URLs, protocol-relative URLs (<c>//evil.com</c>), and
/// back-slash tricks — is rejected.
/// </summary>
public static class LocalUrl
{
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        // Must start with '/' but not '//' or '/\' (protocol-relative / host-relative escapes).
        if (url[0] != '/')
            return false;

        if (url.Length == 1)
            return true;

        return url[1] != '/' && url[1] != '\\';
    }

    /// <summary>
    /// True when <paramref name="url"/> is an absolute http(s) URL whose origin (scheme + host + port) is on
    /// <paramref name="allowedOrigins"/>. This is how the separate-origin Studio host is trusted as a
    /// post-login return target without opening a general redirect hole (mirrors an OAuth redirect_uri allow
    /// list). Origins are compared case-insensitively; default ports are normalized by <see cref="Uri"/>.
    /// </summary>
    public static bool IsTrustedAbsolute(string? url, ICollection<string>? allowedOrigins)
    {
        if (string.IsNullOrEmpty(url) || allowedOrigins is null || allowedOrigins.Count == 0)
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var origin = $"{uri.Scheme}://{uri.Authority}";
        return allowedOrigins.Any(o => string.Equals(o?.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase));
    }

    public static string Sanitize(string? url) => Sanitize(url, null);

    public static string Sanitize(string? url, ICollection<string>? allowedReturnOrigins) =>
        IsLocal(url) || IsTrustedAbsolute(url, allowedReturnOrigins) ? url! : "/";
}
