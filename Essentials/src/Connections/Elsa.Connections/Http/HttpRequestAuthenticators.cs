using System.Net.Http.Headers;
using System.Text;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Http;

public static class HttpConnectionHostGuard
{
    public static string ReadSetting(IResolvedConnection connection, string key, string fallback) =>
        connection.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    public static bool AllowsInsecureHttp(IResolvedConnection connection) =>
        connection.Settings.TryGetValue(HttpConnectionSettingKeys.AllowInsecureHttp, out var flag) &&
        bool.TryParse(flag, out var allowed) &&
        allowed;

    public static void EnsureAllowed(HttpRequestMessage request, IResolvedConnection connection)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("The HTTP request has no URI.");
        if (!connection.Hosts.Allows(uri))
        {
            throw new ConnectionUnavailableException(
                connection.Name,
                ConnectionFaultCodes.HostNotAllowed,
                $"Connection '{connection.Name}' refuses to attach credentials to host '{uri.IdnHost}'.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !AllowsInsecureHttp(connection))
        {
            throw new ConnectionUnavailableException(
                connection.Name,
                ConnectionFaultCodes.InsecureHttp,
                $"Connection '{connection.Name}' requires HTTPS before applying credentials.");
        }
    }

    public static HttpConnectionApplication MarkApplied(HttpRequestMessage request, IResolvedConnection connection)
    {
        if (!request.Options.TryGetValue(HttpConnectionRequestOptionKeys.Applied, out var applied) || applied is null)
        {
            applied = new HttpConnectionApplication
            {
                ConnectionName = connection.Name,
                Hosts = connection.Hosts,
                AllowInsecureHttp = AllowsInsecureHttp(connection)
            };
            request.Options.Set(HttpConnectionRequestOptionKeys.Applied, applied);
        }

        return applied;
    }

    public static void SetAuthorization(HttpRequestMessage request, IResolvedConnection connection, string value)
    {
        if (value.AsSpan().IndexOfAny('\r', '\n') >= 0)
            throw new InvalidOperationException("A connection credential contains a carriage return or a line feed, which a header value cannot hold.");

        request.Headers.Remove("Authorization");
        request.Headers.TryAddWithoutValidation("Authorization", value);
        MarkApplied(request, connection).HeaderNames.Add("Authorization");
    }

    public static void SetHeader(HttpRequestMessage request, IResolvedConnection connection, string headerName, string value)
    {
        if (value.AsSpan().IndexOfAny('\r', '\n') >= 0)
            throw new InvalidOperationException("A connection credential contains a carriage return or a line feed, which a header value cannot hold.");

        request.Headers.Remove(headerName);
        request.Headers.TryAddWithoutValidation(headerName, value);
        MarkApplied(request, connection).HeaderNames.Add(headerName);
    }
}

internal sealed class ApiKeyHttpRequestAuthenticator : IHttpRequestAuthenticator
{
    public string SchemeName => HttpAuthSchemes.ApiKey;

    public async ValueTask ApplyAsync(HttpRequestMessage request, IResolvedConnection connection, CancellationToken cancellationToken = default)
    {
        HttpConnectionHostGuard.EnsureAllowed(request, connection);
        var key = await connection.GetSensitiveAsync(HttpConnectionSecretFields.ApiKey, cancellationToken);
        var placement = HttpConnectionHostGuard.ReadSetting(connection, HttpConnectionSettingKeys.ApiKeyPlacement, ApiKeyPlacements.Header);

        if (string.Equals(placement, ApiKeyPlacements.Query, StringComparison.OrdinalIgnoreCase))
        {
            var queryName = HttpConnectionHostGuard.ReadSetting(connection, HttpConnectionSettingKeys.ApiKeyQueryName, "api_key");
            var uri = request.RequestUri ?? throw new InvalidOperationException("The HTTP request has no URI.");
            var builder = new UriBuilder(uri);
            var separator = string.IsNullOrEmpty(builder.Query) ? "" : "&";
            builder.Query = builder.Query.TrimStart('?') + separator + Uri.EscapeDataString(queryName) + "=" + Uri.EscapeDataString(key.Reveal());
            request.RequestUri = builder.Uri;
            var applied = HttpConnectionHostGuard.MarkApplied(request, connection);
            applied.QueryName = queryName;
            return;
        }

        var headerName = HttpConnectionHostGuard.ReadSetting(connection, HttpConnectionSettingKeys.ApiKeyHeaderName, "Authorization");
        if (string.Equals(headerName, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            HttpConnectionHostGuard.SetAuthorization(request, connection, key.Reveal());
            return;
        }

        HttpConnectionHostGuard.SetHeader(request, connection, headerName, key.Reveal());
    }
}

internal sealed class BasicHttpRequestAuthenticator : IHttpRequestAuthenticator
{
    public string SchemeName => HttpAuthSchemes.Basic;

    public async ValueTask ApplyAsync(HttpRequestMessage request, IResolvedConnection connection, CancellationToken cancellationToken = default)
    {
        HttpConnectionHostGuard.EnsureAllowed(request, connection);
        var username = HttpConnectionHostGuard.ReadSetting(connection, HttpConnectionSettingKeys.Username, "");
        var password = await connection.GetSensitiveAsync(HttpConnectionSecretFields.Password, cancellationToken);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password.Reveal()}"));
        HttpConnectionHostGuard.SetAuthorization(request, connection, $"Basic {token}");
    }
}

internal sealed class BearerHttpRequestAuthenticator : IHttpRequestAuthenticator
{
    public string SchemeName => HttpAuthSchemes.Bearer;

    public async ValueTask ApplyAsync(HttpRequestMessage request, IResolvedConnection connection, CancellationToken cancellationToken = default)
    {
        HttpConnectionHostGuard.EnsureAllowed(request, connection);
        var token = await connection.GetSensitiveAsync(HttpConnectionSecretFields.Token, cancellationToken);
        var value = token.Reveal();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            value = $"Bearer {value}";
        HttpConnectionHostGuard.SetAuthorization(request, connection, value);
    }
}
