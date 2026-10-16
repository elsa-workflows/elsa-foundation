using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Http;

/// <summary>
/// Follows redirects after the primary handler has auto-redirect disabled. Connection-applied
/// credentials are kept only for hops the connection's host policy allows, and are stripped when
/// the next hop is a different origin (scheme, host, or port).
/// </summary>
internal sealed class HttpConnectionRedirectHandler : DelegatingHandler
{
    public const int DefaultMaxRedirects = 8;

    private readonly bool _allowAutoRedirect;
    private readonly int _maxAutomaticRedirections;

    public HttpConnectionRedirectHandler()
        : this(allowAutoRedirect: true, maxAutomaticRedirections: DefaultMaxRedirects)
    {
    }

    public HttpConnectionRedirectHandler(bool allowAutoRedirect, int maxAutomaticRedirections)
    {
        _allowAutoRedirect = allowAutoRedirect;
        _maxAutomaticRedirections = Math.Max(0, maxAutomaticRedirections);
    }

    public HttpConnectionRedirectHandler(
        HttpMessageHandler innerHandler,
        bool allowAutoRedirect = true,
        int maxAutomaticRedirections = DefaultMaxRedirects)
        : this(allowAutoRedirect, maxAutomaticRedirections)
    {
        InnerHandler = innerHandler;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.TryGetValue(HttpConnectionRequestOptionKeys.Applied, out var applied);

        if (request.Content is not null)
            await request.Content.LoadIntoBufferAsync(cancellationToken).ConfigureAwait(false);

        var remaining = _maxAutomaticRedirections;
        while (true)
        {
            EnsureHopAllowed(request, applied);
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!_allowAutoRedirect || !IsRedirect(response) || remaining-- <= 0)
                return response;

            var status = response.StatusCode;
            var location = response.Headers.Location!;
            response.Dispose();

            var next = ToAbsolute(request.RequestUri, location);
            PrepareRedirect(request, next, applied, status);
        }
    }

    private static void EnsureHopAllowed(HttpRequestMessage request, HttpConnectionApplication? applied)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("The HTTP request has no URI.");
        if (applied is null)
            return;

        if (!applied.Hosts.Allows(uri))
        {
            throw new ConnectionUnavailableException(
                applied.ConnectionName,
                ConnectionFaultCodes.HostNotAllowed,
                $"Connection '{applied.ConnectionName}' refuses host '{uri.IdnHost}'.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !applied.AllowInsecureHttp &&
            HasAppliedCredentials(request, applied))
        {
            throw new ConnectionUnavailableException(
                applied.ConnectionName,
                ConnectionFaultCodes.InsecureHttp,
                $"Connection '{applied.ConnectionName}' requires HTTPS before applying credentials.");
        }
    }

    private static void PrepareRedirect(
        HttpRequestMessage request,
        Uri next,
        HttpConnectionApplication? applied,
        System.Net.HttpStatusCode status)
    {
        var previous = request.RequestUri;
        var crossOrigin = IsCrossOrigin(previous, next);

        if (applied is null &&
            previous is not null &&
            string.Equals(previous.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(next.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("HTTPS to HTTP redirects are not followed.");
        }

        if (applied is not null && !crossOrigin && applied.QueryName is { Length: > 0 } && previous is not null)
            next = CopyQueryCredential(previous, next, applied.QueryName);

        request.RequestUri = next;

        if (applied is null)
            request.Headers.Remove("Authorization");
        else if (crossOrigin)
            StripAppliedCredentials(request, applied);

        RewriteMethodIfRequired(request, status);
    }

    private static void RewriteMethodIfRequired(HttpRequestMessage request, System.Net.HttpStatusCode status)
    {
        var code = (int)status;
        if (code is 307 or 308)
            return;

        if (code == 303 || (code is 301 or 302 && request.Method == HttpMethod.Post))
        {
            request.Method = HttpMethod.Get;
            request.Content = null;
        }
    }

    internal static bool IsCrossOrigin(Uri? previous, Uri next) => HttpOrigin.IsCrossOrigin(previous, next);

    private static Uri CopyQueryCredential(Uri previous, Uri next, string queryName)
    {
        var previousQuery = previous.Query.TrimStart('?');
        if (previousQuery.Length == 0)
            return next;

        string? credential = null;
        foreach (var part in previousQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var name = separator < 0 ? part : part[..separator];
            if (string.Equals(Uri.UnescapeDataString(name), queryName, StringComparison.OrdinalIgnoreCase))
            {
                credential = part;
                break;
            }
        }

        if (credential is null)
            return next;

        var builder = new UriBuilder(next);
        var existing = builder.Query.TrimStart('?');
        foreach (var part in existing.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var name = separator < 0 ? part : part[..separator];
            if (string.Equals(Uri.UnescapeDataString(name), queryName, StringComparison.OrdinalIgnoreCase))
                return next;
        }

        builder.Query = string.IsNullOrEmpty(existing) ? credential : existing + "&" + credential;
        return builder.Uri;
    }

    private static void StripAppliedCredentials(HttpRequestMessage request, HttpConnectionApplication applied)
    {
        foreach (var header in applied.HeaderNames)
            request.Headers.Remove(header);

        if (applied.QueryName is { Length: > 0 } && request.RequestUri is not null)
        {
            var builder = new UriBuilder(request.RequestUri);
            var query = builder.Query.TrimStart('?');
            if (query.Length > 0)
            {
                var kept = query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Where(part =>
                    {
                        var separator = part.IndexOf('=');
                        var name = separator < 0 ? part : part[..separator];
                        return !string.Equals(Uri.UnescapeDataString(name), applied.QueryName, StringComparison.OrdinalIgnoreCase);
                    });
                builder.Query = string.Join('&', kept);
                request.RequestUri = builder.Uri;
            }
        }
    }

    private static bool HasAppliedCredentials(HttpRequestMessage request, HttpConnectionApplication applied)
    {
        foreach (var header in applied.HeaderNames)
        {
            if (request.Headers.Contains(header))
                return true;
        }

        return applied.QueryName is { Length: > 0 } && QueryHasName(request.RequestUri, applied.QueryName);
    }

    private static bool QueryHasName(Uri? uri, string queryName)
    {
        if (uri?.Query is not { Length: > 1 } query)
            return false;

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var name = separator < 0 ? part : part[..separator];
            if (string.Equals(Uri.UnescapeDataString(name), queryName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsRedirect(HttpResponseMessage response) =>
        (int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is not null;

    private static Uri ToAbsolute(Uri? requestUri, Uri location) =>
        location.IsAbsoluteUri ? location : new Uri(requestUri ?? throw new InvalidOperationException("The HTTP request has no URI."), location);
}
