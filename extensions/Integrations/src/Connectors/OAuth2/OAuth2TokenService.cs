using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Options;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Exceptions;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Core.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Integrations.Connectors.OAuth2;

internal sealed class OAuth2TokenService(
    ISecretValueResolver secrets,
    ISecretManager secretManager,
    ISecretStoreRegistry stores,
    IConnectionGrantWriter grants,
    IConnectionRepository connections,
    IHttpClientFactory httpClientFactory,
    IOptions<ConnectionsOptions> connectionsOptions,
    IOptions<WritableSecretStoreOptions> writableSecretStore,
    TimeProvider timeProvider)
{
    public static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public Uri CallbackUri
    {
        get
        {
            var origin = connectionsOptions.Value.PublicBaseUrl?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var baseUri))
                throw new InvalidOperationException("Elsa:Connections:PublicBaseUrl must be an absolute URL.");
            return new Uri(baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/_elsa/connections/oauth/callback");
        }
    }

    public async ValueTask<AuthorizationCodeRequest> BeginAuthorizationCodeAsync(
        IResolvedConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        EnsureGrantStoreWritable();
        var authorizationUrl = RequiredSetting(connection, OAuth2SettingKeys.AuthorizationUrl);
        var client = await ResolveClientAsync(connection, cancellationToken);
        var verifier = Pkce.CreateVerifier();
        var nonce = Pkce.CreateNonce();
        var state = EncodeState(connection.TenantId, connection.Name, nonce);
        await WriteNewAsync(
            connection.TenantId,
            OAuth2SecretKeys.Pkce(connection.Name, nonce),
            verifier,
            timeProvider.GetUtcNow().Add(OAuth2GrantStorePolicy.PkceLifetime),
            cancellationToken);

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

        var pkceName = OAuth2SecretKeys.Pkce(connection.Name, nonce);
        var verifier = await ReadRawAsync(connection.TenantId, pkceName, cancellationToken)
            ?? throw new InvalidOperationException("Unknown or expired authorization state.");

        try
        {
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

            return await ExchangeAsync(connection, form, expectedVersion: null, previous: null, cancellationToken);
        }
        finally
        {
            await DeletePkceAsync(connection.TenantId, connection.Name, nonce, cancellationToken);
        }
    }

    public ValueTask<bool> DeletePkceAsync(
        string tenantId,
        string connectionName,
        string nonce,
        CancellationToken cancellationToken = default) =>
        secretManager.DeleteAsync(tenantId, OAuth2SecretKeys.Pkce(connectionName, nonce), cancellationToken);

    public async ValueTask<OAuth2Grant> GetValidGrantAsync(IResolvedConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var current = await ReadGrantAsync(connection, cancellationToken);
        if (current is { HasAccessToken: true } && !IsExpired(current))
            return current;

        return await RefreshAsync(connection, current, cancellationToken);
    }

    /// <summary>
    /// Refreshes an expired or rejected grant. A 401 retry with no refresh token (authorization-code)
    /// faults: the connection needs authorization again. Pass <paramref name="forceRefresh"/> on
    /// that retry so this method reads the stored grant once instead of the caller reading it first.
    /// </summary>
    public async ValueTask<OAuth2Grant> RefreshAsync(
        IResolvedConnection connection,
        OAuth2Grant? current,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(connection);
        OAuth2Grant? latest;
        if (forceRefresh)
        {
            latest = current ?? await ReadGrantAsync(connection, cancellationToken);
        }
        else
        {
            latest = await ReadGrantAsync(connection, cancellationToken);
            if (latest is { HasAccessToken: true } && !IsExpired(latest) && latest.Version != current?.Version)
                return latest;

            latest ??= current;
        }

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
            return await ExchangeAsync(connection, form, latest.Version, latest, cancellationToken);
        }

        var grantType = connection.Settings.GetValueOrDefault(OAuth2SettingKeys.GrantType) ?? OAuth2GrantTypes.ClientCredentials;
        if (!string.Equals(grantType, OAuth2GrantTypes.ClientCredentials, StringComparison.OrdinalIgnoreCase))
        {
            await FailNeedsAuthorizationAsync(
                connection,
                $"Connection '{connection.Name}' needs authorization; its grant is missing or expired and has no refresh token.",
                cancellationToken);
        }

        return await AcquireClientCredentialsAsync(connection, latest?.Version, latest, cancellationToken);
    }

    public ValueTask<OAuth2Grant> AcquireClientCredentialsAsync(
        IResolvedConnection connection,
        int? expectedVersion = null,
        CancellationToken cancellationToken = default) =>
        AcquireClientCredentialsAsync(connection, expectedVersion, previous: null, cancellationToken);

    private async ValueTask<OAuth2Grant> AcquireClientCredentialsAsync(
        IResolvedConnection connection,
        int? expectedVersion,
        OAuth2Grant? previous,
        CancellationToken cancellationToken)
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
        return await ExchangeAsync(connection, form, expectedVersion, previous, cancellationToken);
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

    public static string EncodeState(string tenantId, string connectionName, string nonce)
    {
        var json = JsonSerializer.Serialize(new OAuth2StatePayload(tenantId, connectionName, nonce), JsonOptions);
        return Pkce.Base64Url(Encoding.UTF8.GetBytes(json));
    }

    public static bool TryParseState(string? state, out string tenantId, out string connectionName, out string nonce)
    {
        tenantId = "";
        connectionName = "";
        nonce = "";
        if (string.IsNullOrWhiteSpace(state) || !Pkce.TryBase64UrlDecode(state, out var bytes))
            return false;

        try
        {
            var payload = JsonSerializer.Deserialize<OAuth2StatePayload>(bytes, JsonOptions);
            if (payload is null ||
                string.IsNullOrWhiteSpace(payload.TenantId) ||
                string.IsNullOrWhiteSpace(payload.ConnectionName) ||
                string.IsNullOrWhiteSpace(payload.Nonce))
            {
                return false;
            }

            tenantId = payload.TenantId;
            connectionName = payload.ConnectionName;
            nonce = payload.Nonce;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal async ValueTask<OAuth2ClientCredentials> ResolveClientAsync(
        IResolvedConnection connection,
        CancellationToken cancellationToken)
    {
        var clientId = RequiredSetting(connection, OAuth2SettingKeys.ClientId);
        try
        {
            var secret = await connection.GetSensitiveAsync(OAuth2SecretFields.ClientSecret, cancellationToken);
            return new OAuth2ClientCredentials(clientId, secret.IsEmpty ? null : secret.Reveal());
        }
        catch (ConnectionUnavailableException)
        {
            return new OAuth2ClientCredentials(clientId, null);
        }
    }

    private string GrantStoreName =>
        DefaultWritableSecretStore.ResolveName(writableSecretStore.Value.DefaultWritableStoreName);

    private void EnsureGrantStoreWritable() =>
        DefaultWritableSecretStore.GetRequiredWritable(stores, writableSecretStore.Value.DefaultWritableStoreName);

    private async ValueTask<OAuth2Grant> ExchangeAsync(
        IResolvedConnection connection,
        IReadOnlyDictionary<string, string> form,
        int? expectedVersion,
        OAuth2Grant? previous,
        CancellationToken cancellationToken)
    {
        var tokenUrl = RequiredSetting(connection, OAuth2SettingKeys.TokenUrl);
        var client = httpClientFactory.CreateClient(OAuth2HttpClientNames.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowForTokenEndpointFailureAsync(connection, response.StatusCode, body, cancellationToken);
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var root = document.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var access) ? access.GetString() : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            await ThrowForMissingAccessTokenAsync(connection, root, cancellationToken);
        }

        DateTimeOffset? expiresAt = null;
        if (root.TryGetProperty("expires_in", out var expires) && TryReadExpiresIn(expires, out var seconds))
            expiresAt = timeProvider.GetUtcNow().AddSeconds(seconds);

        var responseRefresh = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : null;
        var refreshToken = !string.IsNullOrWhiteSpace(responseRefresh)
            ? responseRefresh
            : form.TryGetValue("refresh_token", out var existingRefresh) ? existingRefresh : previous?.RefreshToken;

        var tokenType = root.TryGetProperty("token_type", out var type) && !string.IsNullOrWhiteSpace(type.GetString())
            ? type.GetString()!
            : previous?.TokenType ?? "Bearer";
        var scope = root.TryGetProperty("scope", out var scopeElement) && !string.IsNullOrWhiteSpace(scopeElement.GetString())
            ? scopeElement.GetString()
            : previous?.Scope;

        var grant = new OAuth2Grant
        {
            AccessToken = accessToken!,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            TokenType = tokenType,
            Scope = scope
        };

        var written = await WriteGrantAsync(connection, grant, expectedVersion, cancellationToken);
        await MarkReadyAsync(connection, OAuth2SecretKeys.Grant(connection.Name), written.Version, cancellationToken);
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
                    TypeName = SecretTypeNames.Text,
                    StoreName = GrantStoreName
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
            ExpiresAt = grant.ExpiresAt,
            TokenType = grant.TokenType,
            Scope = grant.Scope
        }, cancellationToken);

        if (cas.Status == GrantWriteStatus.Written)
            return grant with { Version = cas.CurrentVersion ?? (grant.Version + 1) };

        var existing = await ReadGrantAsync(connection, cancellationToken);
        if (existing is not null)
            return existing;
        throw new InvalidOperationException("Grant compare-and-swap lost and no grant could be re-read.");
    }

    private async ValueTask WriteNewAsync(
        string tenantId,
        string name,
        string value,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        await secretManager.CreateAsync(tenantId, new CreateSecretRequest
        {
            Name = name,
            Value = value,
            TypeName = SecretTypeNames.Text,
            StoreName = GrantStoreName,
            ExpiresAt = expiresAt
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

    private static bool TryReadExpiresIn(JsonElement expires, out int seconds)
    {
        if (expires.ValueKind == JsonValueKind.Number && expires.TryGetInt32(out seconds))
            return true;
        if (expires.ValueKind == JsonValueKind.String
            && int.TryParse(expires.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds))
        {
            return true;
        }

        seconds = 0;
        return false;
    }

    [DoesNotReturn]
    private async ValueTask ThrowForTokenEndpointFailureAsync(
        IResolvedConnection connection,
        HttpStatusCode statusCode,
        string body,
        CancellationToken cancellationToken)
    {
        var error = OAuth2TokenError.TryReadError(body);
        if (IsInteractive(connection) && OAuth2TokenError.IsPermanentGrantLoss(error))
        {
            await FailNeedsAuthorizationAsync(
                connection,
                $"Connection '{connection.Name}' needs authorization; the authorization server rejected its grant.",
                cancellationToken);
        }

        throw new InvalidOperationException($"Token endpoint returned {(int)statusCode}.");
    }

    [DoesNotReturn]
    private async ValueTask ThrowForMissingAccessTokenAsync(
        IResolvedConnection connection,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var error = OAuth2TokenError.ReadError(root);
        if (IsInteractive(connection) && OAuth2TokenError.IsPermanentGrantLoss(error))
        {
            await FailNeedsAuthorizationAsync(
                connection,
                $"Connection '{connection.Name}' needs authorization; the authorization server rejected its grant.",
                cancellationToken);
        }

        throw new InvalidOperationException(OAuth2TokenError.MissingAccessTokenMessage(root));
    }

    private static bool IsInteractive(IResolvedConnection connection)
    {
        var grantType = connection.Settings.GetValueOrDefault(OAuth2SettingKeys.GrantType) ?? OAuth2GrantTypes.ClientCredentials;
        return !string.Equals(grantType, OAuth2GrantTypes.ClientCredentials, StringComparison.OrdinalIgnoreCase);
    }

    [DoesNotReturn]
    private async ValueTask FailNeedsAuthorizationAsync(
        IResolvedConnection connection,
        string message,
        CancellationToken cancellationToken)
    {
        await TrySetStatusAsync(
            connection,
            ConnectionStatuses.NeedsAuthorization,
            grantSecretName: null,
            grantSecretVersion: null,
            writeGrant: false,
            cancellationToken);
        throw new ConnectionUnavailableException(connection.Name, ConnectionFaultCodes.Unavailable, message);
    }

    private ValueTask MarkReadyAsync(
        IResolvedConnection connection,
        string grantSecretName,
        int grantSecretVersion,
        CancellationToken cancellationToken) =>
        TrySetStatusAsync(
            connection,
            ConnectionStatuses.Ready,
            grantSecretName,
            grantSecretVersion,
            writeGrant: true,
            cancellationToken);

    private async ValueTask TrySetStatusAsync(
        IResolvedConnection connection,
        string status,
        string? grantSecretName,
        int? grantSecretVersion,
        bool writeGrant,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var document = await connections.FindAsync(connection.TenantId, connection.Name, cancellationToken);
            if (document is null)
            {
                throw new ConnectionUnavailableException(
                    connection.Name,
                    ConnectionFaultCodes.Unavailable,
                    $"Connection '{connection.Name}' was not found in this tenant.");
            }

            if (string.Equals(document.State.Status, ConnectionStatuses.Revoked, StringComparison.Ordinal))
                return;

            var sameStatus = string.Equals(document.State.Status, status, StringComparison.Ordinal);
            var sameGrant = !writeGrant ||
                (string.Equals(document.State.GrantSecretName, grantSecretName, StringComparison.Ordinal) &&
                 document.State.GrantSecretVersion == grantSecretVersion);
            if (sameStatus && sameGrant)
                return;

            document.State.Status = status;
            if (writeGrant)
            {
                document.State.GrantSecretName = grantSecretName;
                document.State.GrantSecretVersion = grantSecretVersion;
            }

            document.State.UpdatedAt = timeProvider.GetUtcNow();
            try
            {
                await connections.SaveAsync(document, cancellationToken);
                return;
            }
            catch (ConnectionConflictException) when (attempt < 7)
            {
                // Another writer won the compare-and-swap; re-read and try again.
            }
        }
    }

    private static string RequiredSetting(IResolvedConnection connection, string key) =>
        connection.Settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Connection '{connection.Name}' is missing setting '{key}'.");
}

internal sealed record OAuth2ClientCredentials(string ClientId, string? ClientSecret);

internal sealed record AuthorizationCodeRequest(string AuthorizationUrl, string State, string Challenge);

internal sealed record OAuth2StatePayload(string TenantId, string ConnectionName, string Nonce);
