using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Http;

/// <summary>
/// Follows redirects after the primary handler has auto-redirect disabled. Connection-applied
/// credentials are kept only for hops the connection's host policy allows, and are stripped when
/// the next hop is a different host.
/// </summary>
internal sealed class HttpConnectionRedirectHandler : DelegatingHandler
{
    public const int DefaultMaxRedirects = 8;

    public HttpConnectionRedirectHandler()
    {
    }

    public HttpConnectionRedirectHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Options.TryGetValue(HttpConnectionRequestOptionKeys.Applied, out var applied);

        var remaining = DefaultMaxRedirects;
        while (true)
        {
            EnsureHopAllowed(request, applied);
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response) || remaining-- <= 0)
                return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
                return response;

            var next = ToAbsolute(request.RequestUri, location);
            PrepareRedirect(request, next, applied);
        }
    }

    private static void EnsureHopAllowed(HttpRequestMessage request, HttpConnectionApplication? applied)
    {
        if (applied is null)
            return;

        var uri = request.RequestUri ?? throw new InvalidOperationException("The HTTP request has no URI.");
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

    private static void PrepareRedirect(HttpRequestMessage request, Uri next, HttpConnectionApplication? applied)
    {
        var previous = request.RequestUri;
        var crossHost = previous is not null &&
            !string.Equals(previous.IdnHost, next.IdnHost, StringComparison.OrdinalIgnoreCase);

        if (applied is not null && !crossHost && applied.QueryName is { Length: > 0 } && previous is not null)
            next = CopyQueryCredential(previous, next, applied.QueryName);

        request.RequestUri = next;

        if (applied is not null && crossHost)
            StripAppliedCredentials(request, applied);

        if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
        {
            request.Method = HttpMethod.Get;
            request.Content = null;
        }
    }

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

        if (applied.QueryName is { Length: > 0 } && request.RequestUri?.Query is { Length: > 1 } query)
            return query.Contains(Uri.EscapeDataString(applied.QueryName), StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static bool IsRedirect(HttpResponseMessage response) =>
        (int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is not null;

    private static Uri ToAbsolute(Uri? requestUri, Uri location) =>
        location.IsAbsoluteUri ? location : new Uri(requestUri ?? throw new InvalidOperationException("The HTTP request has no URI."), location);
}
