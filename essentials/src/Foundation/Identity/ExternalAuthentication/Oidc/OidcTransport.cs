using System.Text.Json;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.Extensions.Hosting;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Oidc;

internal sealed class OidcFailure(ExternalProtocolFailure failure) : Exception(failure.ToString())
{
    public ExternalProtocolFailure Failure { get; } = failure;
}

internal sealed record OidcSettings(string Authority, Uri AuthorityUri, string ClientId, string Scopes, string Authentication, string Algorithm)
{
    private static readonly HashSet<string> Keys = ["authority", "clientId", "scopes", "clientAuthentication", "signingAlgorithm"];
    internal static readonly HashSet<string> Algorithms = ["RS256", "RS384", "RS512", "PS256", "PS384", "PS512", "ES256", "ES384", "ES512"];

    public static OidcSettings Read(ConnectionRecord record, ManagedOidcOptions options, IHostEnvironment? environment)
    {
        if (record.Protocol != "oidc" || record.Settings.Keys.Any(key => !Keys.Contains(key)) || record.Secrets.Keys.Any(key => key != "clientSecret"))
            throw new OidcFailure(ExternalProtocolFailure.InvalidConfiguration);
        var authority = record.Settings.GetValueOrDefault("authority") ?? "";
        var client = record.Settings.GetValueOrDefault("clientId") ?? "";
        var scopes = record.Settings.GetValueOrDefault("scopes") ?? "openid profile email";
        var authentication = record.Settings.GetValueOrDefault("clientAuthentication") ?? (record.Secrets.ContainsKey("clientSecret") ? "client_secret_basic" : "none");
        var algorithm = record.Settings.GetValueOrDefault("signingAlgorithm") ?? "RS256";
        if (options.BackchannelTimeout <= TimeSpan.Zero || options.BackchannelTimeout > TimeSpan.FromSeconds(30) ||
            (options.AllowLoopbackHttpForDevelopment && environment?.IsDevelopment() != true) ||
            authority.Length > 400 || !Uri.TryCreate(authority, UriKind.Absolute, out var uri) || !Allowed(uri, options, environment) || !uri.IsWellFormedOriginalString() ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 || authority != authority.Trim() ||
            string.IsNullOrWhiteSpace(client) || client.Length > 400 || client.Any(char.IsControl) ||
            scopes.Length > 2048 || !scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("openid", StringComparer.Ordinal) ||
            scopes.Any(value => value != ' ' && (value < 0x21 || value > 0x7e || value is '"' or '\\')) ||
            authentication is not ("none" or "client_secret_basic" or "client_secret_post") || !Algorithms.Contains(algorithm) ||
            (authentication == "none" ? record.Secrets.Count != 0 : !record.Secrets.ContainsKey("clientSecret")))
            throw new OidcFailure(ExternalProtocolFailure.InvalidConfiguration);
        return new(authority, uri, client, scopes, authentication, algorithm);
    }

    public static bool Allowed(Uri uri, ManagedOidcOptions options, IHostEnvironment? environment) =>
        uri.Scheme == Uri.UriSchemeHttps ||
        (options.AllowLoopbackHttpForDevelopment && environment?.IsDevelopment() == true && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

    public Uri Endpoint(string value, ManagedOidcOptions options, IHostEnvironment? environment, bool allowQuery = false)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !Allowed(uri, options, environment) ||
            uri.GetLeftPart(UriPartial.Authority) != AuthorityUri.GetLeftPart(UriPartial.Authority) ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || (!allowQuery && uri.Query.Length != 0))
            throw new OidcFailure(ExternalProtocolFailure.UntrustedEndpoint);
        return uri;
    }
}

internal sealed record OidcMetadata(string Issuer, Uri Authorization, Uri Token, Uri Keys, bool ResponseIssuerRequired);

internal static class OidcTransport
{
    private const int MaximumBodyBytes = 131072;

    public static async Task<JsonDocument> Send(HttpClient client, HttpRequestMessage request, ExternalProtocolFailure failure, CancellationToken token)
    {
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumBodyBytes)
                throw new OidcFailure(failure);
            await using var input = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int read;
            while ((read = await input.ReadAsync(bytes, token)) > 0)
            {
                if (buffer.Length + read > MaximumBodyBytes) throw new OidcFailure(failure);
                buffer.Write(bytes, 0, read);
            }
            var document = JsonDocument.Parse(buffer.ToArray(), new() { MaxDepth = 32 });
            try { UniqueObject(document.RootElement, failure); }
            catch { document.Dispose(); throw; }
            return document;
        }
        catch (HttpRequestException) { throw new OidcFailure(ExternalProtocolFailure.RemoteUnavailable); }
        catch (IOException) { throw new OidcFailure(ExternalProtocolFailure.RemoteUnavailable); }
        catch (JsonException) { throw new OidcFailure(failure); }
    }

    public static void UniqueObject(JsonElement element, ExternalProtocolFailure failure)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            element.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != element.EnumerateObject().Count())
            throw new OidcFailure(failure);
    }

    public static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    public static async Task<OidcMetadata> Metadata(HttpClient client, OidcSettings settings, ManagedOidcOptions options, IHostEnvironment? environment, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, settings.Authority.TrimEnd('/') + "/.well-known/openid-configuration");
        using var document = await Send(client, request, ExternalProtocolFailure.InvalidDiscovery, token);
        var json = document.RootElement;
        if (Text(json, "issuer") != settings.Authority || !Supports(json, "response_types_supported", "code") ||
            !Supports(json, "id_token_signing_alg_values_supported", settings.Algorithm) ||
            (json.TryGetProperty("grant_types_supported", out _) && !Supports(json, "grant_types_supported", "authorization_code")) ||
            (json.TryGetProperty("code_challenge_methods_supported", out _) && !Supports(json, "code_challenge_methods_supported", "S256")))
            throw new OidcFailure(ExternalProtocolFailure.InvalidDiscovery);
        if (json.TryGetProperty("token_endpoint_auth_methods_supported", out _) ? !Supports(json, "token_endpoint_auth_methods_supported", settings.Authentication) : settings.Authentication != "client_secret_basic")
            throw new OidcFailure(ExternalProtocolFailure.UnsupportedClientAuthentication);
        var authorization = settings.Endpoint(Text(json, "authorization_endpoint") ?? "", options, environment);
        var endpoint = settings.Endpoint(Text(json, "token_endpoint") ?? "", options, environment);
        var keys = settings.Endpoint(Text(json, "jwks_uri") ?? "", options, environment, allowQuery: true);
        if (json.TryGetProperty("authorization_response_iss_parameter_supported", out var supported) && supported.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new OidcFailure(ExternalProtocolFailure.InvalidDiscovery);
        var responseIssuer = supported.ValueKind == JsonValueKind.True;
        return new(settings.Authority, authorization, endpoint, keys, responseIssuer);
    }

    private static bool Supports(JsonElement json, string property, string value) =>
        json.TryGetProperty(property, out var list) && list.ValueKind == JsonValueKind.Array &&
        list.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == value);
}
