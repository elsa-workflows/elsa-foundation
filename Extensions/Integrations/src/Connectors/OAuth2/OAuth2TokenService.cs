using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Elsa.Activities.Http.Constants;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Options;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Exceptions;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Integrations.Connectors.OAuth2;

internal sealed class OAuth2TokenService(
    ISecretValueResolver secrets,
    ISecretManager secretManager,
    IConnectionGrantWriter grants,
    IValenceOAuthAppRegistry valenceApps,
    IHttpClientFactory httpClientFactory,
    IOptions<ConnectionsOptions> connectionsOptions,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public Uri CallbackUri
    {
        get
        {
            var origin = connectionsOptions.Value.PublicBaseUrl?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var baseUri))
                throw new InvalidOperationException("Elsa:Connections:PublicBaseUrl must be an absolute URL.");
            return new Uri(baseUri, "/_elsa/connections/oauth/callback");
        }
    }

    public async ValueTask<AuthorizationCodeRequest> BeginAuthorizationCodeAsync(
        IResolvedConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var authorizationUrl = RequiredSetting(connection, OAuth2SettingKeys.AuthorizationUrl);
        var client = await ResolveClientAsync(connection, cancellationToken);
        var verifier = Pkce.CreateVerifier();
        var nonce = Pkce.CreateNonce();
        var state = $"{connection.TenantId}:{connection.Name}:{nonce}";
        await WriteNewAsync(connection.TenantId, OAuth2SecretKeys.Pkce(connection.Name, nonce), verifier, cancellationToken);

        var challenge = Pkce.ChallengeS256(verifier);
        var uri = new UriBuilder(authorizationUrl);
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["response_type"] = "code",
            ["client_id"] = client.ClientId,
            ["redirect_uri"] = CallbackUri.ToString(),
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state
        };
        if (connection.Settings.TryGetValue(OAuth2SettingKeys.Scopes, out var scopes) && !string.IsNullOrWhiteSpace(scopes))
            query["scope"] = scopes;
        uri.Query = string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new AuthorizationCodeRequest(uri.Uri.ToString(), state, challenge);
    }

    public async ValueTask<OAuth2Grant> CompleteAuthorizationCodeAsync(
        IResolvedConnection connection,
        string code,
        string state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!TryParseState(state, out var tenantId, out var connectionName, out var nonce) ||
            !string.Equals(tenantId, connection.TenantId, StringComparison.Ordinal) ||
            !string.Equals(connectionName, connection.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unknown or expired authorization state.");
        }

        var verifier = await ReadRawAsync(connection.TenantId, OAuth2SecretKeys.Pkce(connection.Name, nonce), cancellationToken)
            ?? throw new InvalidOperationException("Unknown or expired authorization state.");

        var client = await ResolveClientAsync(connection, cancellationToken);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = OAuth2GrantTypes.AuthorizationCode,
            ["code"] = code,
            ["client_id"] = client.ClientId,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = CallbackUri.ToString()
        };
        if (client.ClientSecret is not null)
            form["client_secret"] = client.ClientSecret;

        var grant = await ExchangeAsync(connection, form, expectedVersion: null, cancellationToken);
        await secretManager.DeleteAsync(connection.TenantId, OAuth2SecretKeys.Pkce(connection.Name, nonce), cancellationToken);
        return grant;
    }

    public async ValueTask<OAuth2Grant> GetValidGrantAsync(IResolvedConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var current = await ReadGrantAsync(connection, cancellationToken);
        if (current is { HasAccessToken: true } && !IsExpired(current))
            return current;

        return await RefreshAsync(connection, current, cancellationToken);
    }

    public async ValueTask<OAuth2Grant> RefreshAsync(
        IResolvedConnection connection,
        OAuth2Grant? current,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var gate = _locks.GetOrAdd($"{connection.TenantId}/{connection.Name}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var latest = await ReadGrantAsync(connection, cancellationToken);
            if (latest is { HasAccessToken: true } && !IsExpired(latest) && latest.Version != current?.Version)
                return latest;

            latest ??= current;
            if (latest?.RefreshToken is { Length: > 0 })
            {
                var client = await ResolveClientAsync(connection, cancellationToken);
                var form = new Dictionary<string, string>
                {
                    ["grant_type"] = OAuth2GrantTypes.RefreshToken,
                    ["refresh_token"] = latest.RefreshToken,
                    ["client_id"] = client.ClientId
                };
                if (client.ClientSecret is not null)
                    form["client_secret"] = client.ClientSecret;
                return await ExchangeAsync(connection, form, latest.Version, cancellationToken);
            }

            var grantType = connection.Settings.GetValueOrDefault(OAuth2SettingKeys.GrantType) ?? OAuth2GrantTypes.ClientCredentials;
            if (!string.Equals(grantType, OAuth2GrantTypes.ClientCredentials, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Connection '{connection.Name}' needs authorization; its grant is missing or expired and has no refresh token.");
            }

            return await AcquireClientCredentialsAsync(connection, latest?.Version, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<OAuth2Grant> AcquireClientCredentialsAsync(
        IResolvedConnection connection,
        int? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var client = await ResolveClientAsync(connection, cancellationToken);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = OAuth2GrantTypes.ClientCredentials,
            ["client_id"] = client.ClientId
        };
        if (client.ClientSecret is not null)
            form["client_secret"] = client.ClientSecret;
        if (connection.Settings.TryGetValue(OAuth2SettingKeys.Scopes, out var scopes) && !string.IsNullOrWhiteSpace(scopes))
            form["scope"] = scopes;
        return await ExchangeAsync(connection, form, expectedVersion, cancellationToken);
    }

    public bool IsExpired(OAuth2Grant grant)
    {
        if (grant.ExpiresAt is null)
            return false;
        return grant.ExpiresAt.Value - ExpirySkew <= timeProvider.GetUtcNow();
    }

    public async ValueTask<OAuth2Grant?> ReadGrantAsync(IResolvedConnection connection, CancellationToken cancellationToken)
    {
        var name = OAuth2SecretKeys.Grant(connection.Name);
        var resolved = await secrets.ResolveAsync(connection.TenantId, new SecretReference(name), cancellationToken);
        if (!resolved.Succeeded || string.IsNullOrWhiteSpace(resolved.Value))
            return null;

        var grant = JsonSerializer.Deserialize<OAuth2Grant>(resolved.Value, JsonOptions);
        if (grant is null)
            return null;

        var meta = await secretManager.FindAsync(connection.TenantId, name, cancellationToken);
        return grant with { Version = meta?.CurrentVersion ?? grant.Version };
    }

    public static bool TryParseState(string? state, out string tenantId, out string connectionName, out string nonce)
    {
        tenantId = "";
        connectionName = "";
        nonce = "";
        if (string.IsNullOrWhiteSpace(state))
            return false;

        var parts = state.Split(':');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
            return false;

        tenantId = parts[0];
        connectionName = parts[1];
        nonce = parts[2];
        return true;
    }

    internal async ValueTask<OAuth2ClientCredentials> ResolveClientAsync(
        IResolvedConnection connection,
        CancellationToken cancellationToken)
    {
        var source = connection.Settings.GetValueOrDefault(OAuth2SettingKeys.ClientSource) ?? OAuth2ClientSources.Valence;
        if (string.Equals(source, OAuth2ClientSources.BringYourOwn, StringComparison.OrdinalIgnoreCase))
        {
            var clientId = RequiredSetting(connection, OAuth2SettingKeys.ClientId);
            var secret = await connection.GetSensitiveAsync(OAuth2SecretFields.ClientSecret, cancellationToken);
            return new OAuth2ClientCredentials(clientId, secret.IsEmpty ? null : secret.Reveal());
        }

        var appId = connection.Settings.GetValueOrDefault(OAuth2SettingKeys.ValenceAppId) ?? "elsa.oauth2";
        var app = await valenceApps.GetAsync(appId, cancellationToken)
            ?? throw new InvalidOperationException($"No Valence OAuth app is registered for '{appId}'.");
        return new OAuth2ClientCredentials(app.ClientId, app.ClientSecret);
    }

    private async ValueTask<OAuth2Grant> ExchangeAsync(
        IResolvedConnection connection,
        IReadOnlyDictionary<string, string> form,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        var tokenUrl = RequiredSetting(connection, OAuth2SettingKeys.TokenUrl);
        var client = httpClientFactory.CreateClient(HttpActivityConstants.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token endpoint returned {(int)response.StatusCode}.");

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = document.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var access) ? access.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Token endpoint returned no access_token.");

        DateTimeOffset? expiresAt = null;
        if (root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds))
            expiresAt = timeProvider.GetUtcNow().AddSeconds(seconds);

        var grant = new OAuth2Grant
        {
            AccessToken = accessToken,
            RefreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null,
            ExpiresAt = expiresAt,
            TokenType = root.TryGetProperty("token_type", out var type) ? type.GetString() ?? "Bearer" : "Bearer",
            Scope = root.TryGetProperty("scope", out var scope) ? scope.GetString() : null
        };

        var written = await WriteGrantAsync(connection, grant, expectedVersion, cancellationToken);
        return written;
    }

    private async ValueTask<OAuth2Grant> WriteGrantAsync(
        IResolvedConnection connection,
        OAuth2Grant grant,
        int? expectedVersion,
        CancellationToken cancellationToken)
    {
        var name = OAuth2SecretKeys.Grant(connection.Name);
        var current = await secretManager.FindAsync(connection.TenantId, name, cancellationToken);
        if (current is null)
        {
            try
            {
                var created = await secretManager.CreateAsync(connection.TenantId, new CreateSecretRequest
                {
                    Name = name,
                    Value = SerializeGrant(grant),
                    TypeName = SecretTypeNames.Text
                }, cancellationToken);
                return grant with { Version = created.CurrentVersion ?? 1 };
            }
            catch (SecretNameInUseException)
            {
                var winner = await ReadGrantAsync(connection, cancellationToken);
                if (winner is not null)
                    return winner;
                throw;
            }
        }

        var cas = await grants.TryCompareAndSwapAsync(new GrantWriteRequest
        {
            TenantId = connection.TenantId,
            ConnectionName = connection.Name,
            GrantSecretName = name,
            ExpectedSecretVersion = expectedVersion ?? current.CurrentVersion ?? 0,
            AccessToken = grant.AccessToken,
            RefreshToken = grant.RefreshToken,
            ExpiresAt = grant.ExpiresAt ?? DateTimeOffset.UnixEpoch
        }, cancellationToken);

        if (cas.Status == GrantWriteStatus.Written)
            return grant with { Version = cas.CurrentVersion ?? (grant.Version + 1) };

        var existing = await ReadGrantAsync(connection, cancellationToken);
        if (existing is not null)
            return existing;
        throw new InvalidOperationException("Grant compare-and-swap lost and no grant could be re-read.");
    }

    private async ValueTask WriteNewAsync(string tenantId, string name, string value, CancellationToken cancellationToken)
    {
        await secretManager.CreateAsync(tenantId, new CreateSecretRequest
        {
            Name = name,
            Value = value,
            TypeName = SecretTypeNames.Text
        }, cancellationToken);
    }

    private async ValueTask<string?> ReadRawAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        var resolved = await secrets.ResolveAsync(tenantId, new SecretReference(name), cancellationToken);
        return resolved.Succeeded ? resolved.Value : null;
    }

    private static string SerializeGrant(OAuth2Grant grant) => JsonSerializer.Serialize(new Dictionary<string, string?>
    {
        ["access_token"] = grant.AccessToken,
        ["refresh_token"] = grant.RefreshToken,
        ["expires_at"] = grant.ExpiresAt?.ToString("O"),
        ["token_type"] = grant.TokenType,
        ["scope"] = grant.Scope
    });

    private static string RequiredSetting(IResolvedConnection connection, string key) =>
        connection.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Connection '{connection.Name}' is missing setting '{key}'.");
}

internal sealed record OAuth2ClientCredentials(string ClientId, string? ClientSecret);

internal sealed record AuthorizationCodeRequest(string AuthorizationUrl, string State, string Challenge);
