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
/// Guards against open redirects: only same-origin, root-relative paths (e.g. <c>/studio</c>) are honoured.
/// Absolute URLs, protocol-relative URLs (<c>//evil.com</c>), and back-slash tricks are rejected.
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

    public static string Sanitize(string? url) => IsLocal(url) ? url! : "/";
}
