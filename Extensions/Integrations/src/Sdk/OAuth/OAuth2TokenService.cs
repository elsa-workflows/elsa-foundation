using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Elsa.Activities.Http.Constants;
using Elsa.Integrations.Sdk.Connections;
using Elsa.Integrations.Sdk.Secrets;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Integrations.Sdk.OAuth;

/// <summary>
/// STUB token lifecycle (#801 / #2727): authorization-code + PKCE, client-credentials, refresh
/// on expiry or 401. Reads go through <see cref="ISecretValueResolver"/>; writes use compare-and-swap
/// on the secret version. This type does not cache tokens.
/// </summary>
internal sealed class OAuth2TokenService(
    ISecretValueResolver secrets,
    IGrantSecretWriter grants,
    IValenceOAuthAppRegistry valenceApps,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async ValueTask<AuthorizationCodeRequest> BeginAuthorizationCodeAsync(
        ConnectionRecord connection,
        OAuthCallbackOptions callback,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(callback);
        var authorizationUrl = RequiredSetting(connection, "authorizationUrl");
        var client = await ResolveClientAsync(connection, cancellationToken);
        var verifier = Pkce.CreateVerifier();
        var state = Pkce.CreateState();
        await WriteNewAsync(connection.TenantId, OAuth2SecretKeys.PkceVerifier(state), verifier, cancellationToken);

        var challenge = Pkce.ChallengeS256(verifier);
        var uri = new UriBuilder(authorizationUrl);
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["response_type"] = "code",
            ["client_id"] = client.ClientId,
            ["redirect_uri"] = callback.CallbackUri.ToString(),
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state
        };
        if (connection.Settings.TryGetValue("scopes", out var scopes) && !string.IsNullOrWhiteSpace(scopes))
            query["scope"] = scopes;
        uri.Query = string.Join('&', query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new AuthorizationCodeRequest(uri.Uri.ToString(), state, challenge);
    }

    public async ValueTask<OAuth2Grant> CompleteAuthorizationCodeAsync(
        ConnectionRecord connection,
        AuthorizationCodeCallback callback,
        OAuthCallbackOptions callbackOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(callbackOptions);
        var verifier = await ReadRawAsync(connection.TenantId, OAuth2SecretKeys.PkceVerifier(callback.State), cancellationToken)
            ?? throw new InvalidOperationException("Unknown or expired authorization state.");

        var client = await ResolveClientAsync(connection, cancellationToken);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = OAuth2GrantTypes.AuthorizationCode,
            ["code"] = callback.Code,
            ["client_id"] = client.ClientId,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = callbackOptions.CallbackUri.ToString()
        };
        if (client.ClientSecret is not null)
            form["client_secret"] = client.ClientSecret;

        var grant = await ExchangeAsync(connection, form, expectedVersion: 0, cancellationToken);
        await grants.DeleteAsync(connection.TenantId, OAuth2SecretKeys.PkceVerifier(callback.State), cancellationToken);
        return grant;
    }

    public async ValueTask<OAuth2Grant> AcquireClientCredentialsAsync(
        ConnectionRecord connection,
        CancellationToken cancellationToken = default,
        int expectedVersion = 0)
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
        if (connection.Settings.TryGetValue("scopes", out var scopes) && !string.IsNullOrWhiteSpace(scopes))
            form["scope"] = scopes;
        return await ExchangeAsync(connection, form, expectedVersion, cancellationToken);
    }

    public async ValueTask<OAuth2Grant> GetValidGrantAsync(
        ConnectionRecord connection,
        IRuntimeSecretMask? mask,
        string? activityExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var current = await ReadGrantAsync(connection, mask, activityExecutionId, cancellationToken);
        if (current is { HasAccessToken: true } && !IsExpired(current))
            return current;

        return await RefreshAsync(connection, current, mask, activityExecutionId, cancellationToken);
    }

    public async ValueTask<OAuth2Grant> RefreshAsync(
        ConnectionRecord connection,
        OAuth2Grant? current,
        IRuntimeSecretMask? mask,
        string? activityExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var gate = _locks.GetOrAdd(LockKey(connection), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var latest = await ReadGrantAsync(connection, mask, activityExecutionId, cancellationToken);
            OAuth2Grant grant;
            if (latest is { HasAccessToken: true } && !IsExpired(latest) && latest.Version != current?.Version)
            {
                grant = latest;
            }
            else
            {
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
                    grant = await ExchangeAsync(connection, form, latest.Version, cancellationToken);
                }
                else
                {
                    var grantType = connection.Settings.GetValueOrDefault("grantType") ?? OAuth2GrantTypes.ClientCredentials;
                    if (!string.Equals(grantType, OAuth2GrantTypes.ClientCredentials, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            $"Connection '{connection.Name}' needs authorization; its grant is missing or expired and has no refresh token.");
                    grant = await AcquireClientCredentialsAsync(connection, cancellationToken, latest?.Version ?? 0);
                }
            }

            RegisterGrant(mask, activityExecutionId, connection, grant);
            return grant;
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<OAuth2ClientCredentials> ResolveClientAsync(
        ConnectionRecord connection,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(connection.ClientSource, OAuth2ClientSources.BringYourOwn, StringComparison.OrdinalIgnoreCase))
        {
            var clientId = RequiredSetting(connection, "clientId");
            var secret = await ReadRawAsync(connection.TenantId, OAuth2SecretKeys.ClientSecret(connection.Name), cancellationToken);
            return new OAuth2ClientCredentials(clientId, secret);
        }

        var appId = connection.Settings.GetValueOrDefault("valenceAppId") ?? connection.Type;
        var app = await valenceApps.GetAsync(appId, cancellationToken)
            ?? throw new InvalidOperationException($"No Valence OAuth app is registered for '{appId}'.");
        return new OAuth2ClientCredentials(app.ClientId, app.ClientSecret);
    }

    public bool IsExpired(OAuth2Grant grant)
    {
        if (grant.ExpiresAt is null)
            return false;
        return grant.ExpiresAt.Value - ExpirySkew <= timeProvider.GetUtcNow();
    }

    public async ValueTask<OAuth2Grant?> ReadGrantAsync(
        ConnectionRecord connection,
        IRuntimeSecretMask? mask,
        string? activityExecutionId,
        CancellationToken cancellationToken)
    {
        var name = OAuth2SecretKeys.Grant(connection.Name);
        var resolved = await secrets.ResolveAsync(connection.TenantId, new SecretReference(name), cancellationToken);
        if (!resolved.Succeeded || string.IsNullOrWhiteSpace(resolved.Value))
            return null;

        if (mask is not null && !string.IsNullOrWhiteSpace(activityExecutionId))
            RegisterGrantWithMask(mask, activityExecutionId, name, resolved.Value);

        return JsonSerializer.Deserialize<OAuth2Grant>(resolved.Value, JsonOptions);
    }

    private async ValueTask<OAuth2Grant> ExchangeAsync(
        ConnectionRecord connection,
        IReadOnlyDictionary<string, string> form,
        int expectedVersion,
        CancellationToken cancellationToken)
    {
        var tokenUrl = RequiredSetting(connection, "tokenUrl");
        var client = httpClientFactory.CreateClient(HttpActivityConstants.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Options.Set(OAuth2RequestFlags.SkipTokenInjection, true);

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
            Scope = root.TryGetProperty("scope", out var scope) ? scope.GetString() : null,
            Version = expectedVersion + 1
        };

        var json = JsonSerializer.Serialize(grant, JsonOptions);
        if (!await grants.CompareAndSwapAsync(connection.TenantId, OAuth2SecretKeys.Grant(connection.Name), expectedVersion, json, cancellationToken))
        {
            var winner = await ReadGrantAsync(connection, mask: null, activityExecutionId: null, cancellationToken);
            if (winner is not null)
                return winner;
            throw new InvalidOperationException("Grant compare-and-swap lost and no grant could be re-read.");
        }

        return grant;
    }

    private async ValueTask WriteNewAsync(string tenantId, string name, string value, CancellationToken cancellationToken)
    {
        if (!await grants.CompareAndSwapAsync(tenantId, name, expectedVersion: 0, value, cancellationToken))
            throw new InvalidOperationException($"Could not write secret '{name}'.");
    }

    private async ValueTask<string?> ReadRawAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        var resolved = await secrets.ResolveAsync(tenantId, new SecretReference(name), cancellationToken);
        return resolved.Succeeded ? resolved.Value : null;
    }

    private static void RegisterGrant(
        IRuntimeSecretMask? mask,
        string? activityExecutionId,
        ConnectionRecord connection,
        OAuth2Grant grant)
    {
        if (mask is null || string.IsNullOrWhiteSpace(activityExecutionId))
            return;
        var name = OAuth2SecretKeys.Grant(connection.Name);
        if (grant.AccessToken is { Length: > 0 })
            mask.Register(activityExecutionId, name, grant.AccessToken);
        if (grant.RefreshToken is { Length: > 0 })
            mask.Register(activityExecutionId, name, grant.RefreshToken);
    }

    private static void RegisterGrantWithMask(IRuntimeSecretMask mask, string activityExecutionId, string referenceName, string json)
    {
        var grant = JsonSerializer.Deserialize<OAuth2Grant>(json, JsonOptions);
        if (grant is null)
            return;
        if (grant.AccessToken is { Length: > 0 })
            mask.Register(activityExecutionId, referenceName, grant.AccessToken);
        if (grant.RefreshToken is { Length: > 0 })
            mask.Register(activityExecutionId, referenceName, grant.RefreshToken);
    }

    private static string LockKey(ConnectionRecord connection) => $"{connection.TenantId}/{connection.Name}";

    private static string RequiredSetting(ConnectionRecord connection, string key) =>
        connection.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Connection '{connection.Name}' is missing setting '{key}'.");
}

internal static class OAuth2RequestFlags
{
    public static readonly HttpRequestOptionsKey<bool> SkipTokenInjection = new("elsa.oauth2.skip-token-injection");
}
