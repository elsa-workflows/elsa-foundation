using System.Buffers.Binary;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Oidc;

internal sealed class ManagedOidcProtocol(IHttpClientFactory clients, IServiceScopeFactory scopes, IOptions<ManagedOidcOptions> options,
    IOptions<ExternalAuthenticationOptions> identityOptions, TimeProvider clock, IHostEnvironment? environment = null) : IExternalAuthenticationProtocol
{
    public string Name => "oidc";

    public ValueTask<ExternalProtocolCheck> CheckAsync(ConnectionRecord connection, CancellationToken cancellationToken = default) =>
        Run(async token => { var settings = Settings(connection); var credential = await Credential(connection, settings, token);
                return new ExternalProtocolCheck { Issuer = settings.Authority, CredentialBinding = credential.Binding }; },
            failure => new ExternalProtocolCheck(failure), cancellationToken);

    public ValueTask<ExternalProtocolCheck> TestAsync(ConnectionRecord connection, CancellationToken cancellationToken = default) =>
        Run(async token =>
        {
            var settings = Settings(connection);
            var credential = await Credential(connection, settings, token);
            using var client = clients.CreateClient(ManagedOidcRegistration.HttpClientName);
            var metadata = await OidcTransport.Metadata(client, settings, options.Value, environment, token);
            _ = await Keys(client, metadata, settings, token);
            return new ExternalProtocolCheck { Issuer = metadata.Issuer, CredentialBinding = credential.Binding };
        }, failure => new ExternalProtocolCheck(failure), cancellationToken);

    public ValueTask<ExternalProtocolChallenge> ChallengeAsync(ConnectionRecord connection, ExternalProtocolChallengeRequest request, CancellationToken cancellationToken = default) =>
        Run(async token =>
        {
            var settings = Settings(connection);
            if (!Redirect(request.RedirectUri) || !Opaque(request.State) || !Opaque(request.Nonce) || !Opaque(request.CodeVerifier))
                throw new OidcFailure(ExternalProtocolFailure.InvalidResponse);
            var credential = await Credential(connection, settings, token);
            using var client = clients.CreateClient(ManagedOidcRegistration.HttpClientName);
            var metadata = await OidcTransport.Metadata(client, settings, options.Value, environment, token);
            var url = QueryHelpers.AddQueryString(metadata.Authorization.AbsoluteUri, new Dictionary<string, string?>
            {
                ["client_id"] = settings.ClientId, ["redirect_uri"] = request.RedirectUri, ["response_type"] = "code", ["response_mode"] = "query",
                ["scope"] = settings.Scopes, ["state"] = request.State, ["nonce"] = request.Nonce,
                ["code_challenge_method"] = "S256", ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(request.CodeVerifier)))
            });
            return new ExternalProtocolChallenge(url, metadata.Issuer, credential.Binding);
        }, failure => new ExternalProtocolChallenge(Failure: failure), cancellationToken);

    public ValueTask<ExternalProtocolIdentity> RedeemAsync(ConnectionRecord connection, ExternalProtocolCodeRequest request, CancellationToken cancellationToken = default) =>
        Run(async token =>
        {
            var settings = Settings(connection);
            if (!Redirect(request.RedirectUri) || !Opaque(request.Nonce) || !Opaque(request.CodeVerifier) ||
                string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 8192 || request.Code.Any(char.IsControl))
                throw new OidcFailure(ExternalProtocolFailure.InvalidResponse);
            var credential = await Credential(connection, settings, token);
            if (!FixedEquals(request.CredentialBinding, credential.Binding)) throw new OidcFailure(ExternalProtocolFailure.SecretChanged);
            using var client = clients.CreateClient(ManagedOidcRegistration.HttpClientName);
            var metadata = await OidcTransport.Metadata(client, settings, options.Value, environment, token);
            if (metadata.Issuer != request.ExpectedIssuer) throw new OidcFailure(ExternalProtocolFailure.ProviderChanged);
            if ((metadata.ResponseIssuerRequired || request.ResponseIssuer is not null) && request.ResponseIssuer != metadata.Issuer)
                throw new OidcFailure(ExternalProtocolFailure.ProviderChanged);
            var keys = await Keys(client, metadata, settings, token);
            using var message = new HttpRequestMessage(HttpMethod.Post, metadata.Token);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["code"] = request.Code,
                ["redirect_uri"] = request.RedirectUri, ["code_verifier"] = request.CodeVerifier
            };
            if (settings.Authentication == "client_secret_basic")
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(Form(settings.ClientId) + ":" + Form(credential.Value!))));
            else
            {
                form["client_id"] = settings.ClientId;
                if (settings.Authentication == "client_secret_post") form["client_secret"] = credential.Value!;
            }
            message.Content = new FormUrlEncodedContent(form);
            using var document = await OidcTransport.Send(client, message, ExternalProtocolFailure.UpstreamRejected, token);
            var json = document.RootElement;
            var idToken = OidcTransport.Text(json, "id_token");
            var accessToken = OidcTransport.Text(json, "access_token");
            if (json.TryGetProperty("error", out _) || string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(accessToken) ||
                !string.Equals(OidcTransport.Text(json, "token_type"), "Bearer", StringComparison.OrdinalIgnoreCase))
                throw new OidcFailure(ExternalProtocolFailure.InvalidResponse);
            IReadOnlyList<Claim> claims;
            try { claims = await Validate(idToken, accessToken, settings, keys, request.Nonce); }
            catch (SigningKeyMissing)
            {
                // Rotation may occur between the initial JWKS read and code redemption. Refresh only the
                // trusted key endpoint once; never re-submit the already consumed authorization code.
                keys = await Keys(client, metadata, settings, token);
                claims = await Validate(idToken, accessToken, settings, keys, request.Nonce);
            }
            var subject = claims.Single(claim => claim.Type == "sub").Value;
            if (subject.Length > 400)
                throw new OidcFailure(ExternalProtocolFailure.InvalidToken);
            return new ExternalProtocolIdentity(new(identityOptions.Value.TenantId, connection.Key, metadata.Issuer, subject), claims);
        }, failure => new ExternalProtocolIdentity(null, [], failure), cancellationToken);

    private OidcSettings Settings(ConnectionRecord connection) => OidcSettings.Read(connection, options.Value, environment);

    private async Task<IReadOnlyList<SecurityKey>> Keys(HttpClient client, OidcMetadata metadata, OidcSettings settings, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, metadata.Keys);
        using var document = await OidcTransport.Send(client, request, ExternalProtocolFailure.InvalidDiscovery, token);
        if (!document.RootElement.TryGetProperty("keys", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() is 0 or > 64)
            throw new OidcFailure(ExternalProtocolFailure.InvalidDiscovery);
        try
        {
            var keys = new JsonWebKeySet(document.RootElement.GetRawText()).GetSigningKeys().Where(key =>
                settings.Algorithm.StartsWith("ES", StringComparison.Ordinal)
                    ? key.KeySize >= 256 && (key is ECDsaSecurityKey || key is JsonWebKey { Kty: "EC" })
                    : key.KeySize >= 2048 && (key is RsaSecurityKey || key is JsonWebKey { Kty: "RSA" })).ToArray();
            if (keys.Length == 0) throw new OidcFailure(ExternalProtocolFailure.InvalidDiscovery);
            return keys;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException or FormatException)
        { throw new OidcFailure(ExternalProtocolFailure.InvalidDiscovery); }
    }

    private async Task<IReadOnlyList<Claim>> Validate(string encoded, string accessToken, OidcSettings settings, IReadOnlyList<SecurityKey> keys, string nonce)
    {
        try
        {
            var parts = encoded.Split('.');
            if (encoded.Length > 65536 || parts.Length != 3) throw new OidcFailure(ExternalProtocolFailure.InvalidToken);
            using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[0]));
            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
            OidcTransport.UniqueObject(header.RootElement, ExternalProtocolFailure.InvalidToken);
            OidcTransport.UniqueObject(payload.RootElement, ExternalProtocolFailure.InvalidToken);
            var typ = OidcTransport.Text(header.RootElement, "typ");
            if (OidcTransport.Text(header.RootElement, "alg") != settings.Algorithm || header.RootElement.TryGetProperty("crit", out _) || header.RootElement.TryGetProperty("b64", out _) ||
                (typ is not null && !string.Equals(typ, "JWT", StringComparison.OrdinalIgnoreCase) && !string.Equals(typ, "application/jwt", StringComparison.OrdinalIgnoreCase)))
                throw new OidcFailure(ExternalProtocolFailure.InvalidToken);
            var parameters = new TokenValidationParameters
            {
                RequireSignedTokens = true, RequireExpirationTime = true, ValidateIssuerSigningKey = true, IssuerSigningKeys = keys,
                ValidateIssuer = true, ValidIssuer = settings.Authority,
                IssuerValidator = (issuer, _, _) => issuer == settings.Authority ? issuer : throw new SecurityTokenInvalidIssuerException(),
                ValidateAudience = true, ValidAudience = settings.ClientId, IgnoreTrailingSlashWhenValidatingAudience = false,
                AudienceValidator = (audiences, _, _) => audiences.Count() == 1 && audiences.Single() == settings.ClientId,
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30), ValidAlgorithms = [settings.Algorithm],
                AuthenticationType = "Elsa.ManagedOidc.Validated", SaveSigninToken = false
            };
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 65536 };
            var result = await handler.ValidateTokenAsync(encoded, parameters);
            if (!result.IsValid)
            {
                if (result.Exception is SecurityTokenSignatureKeyNotFoundException) throw new SigningKeyMissing();
                throw new OidcFailure(ExternalProtocolFailure.InvalidToken);
            }
            if (result.SecurityToken is not JwtSecurityToken jwt) throw new OidcFailure(ExternalProtocolFailure.InvalidToken);
            new OpenIdConnectProtocolValidator { RequireNonce = true, RequireSub = true, RequireStateValidation = false, RequireTimeStampInNonce = false }
                .ValidateTokenResponse(new OpenIdConnectProtocolValidationContext
                {
                    ClientId = settings.ClientId, Nonce = nonce, ValidatedIdToken = jwt,
                    ProtocolMessage = new OpenIdConnectMessage { IdToken = encoded, AccessToken = accessToken, TokenType = "Bearer" }
                });
            var subject = OidcTransport.Text(payload.RootElement, "sub");
            if (string.IsNullOrWhiteSpace(subject) || !payload.RootElement.TryGetProperty("iat", out var issuedAt) || issuedAt.ValueKind != JsonValueKind.Number || !issuedAt.TryGetInt64(out var timestamp) ||
                timestamp > clock.GetUtcNow().AddSeconds(30).ToUnixTimeSeconds() ||
                (payload.RootElement.TryGetProperty("azp", out _) && OidcTransport.Text(payload.RootElement, "azp") != settings.ClientId))
                throw new OidcFailure(ExternalProtocolFailure.InvalidToken);
            return Array.AsReadOnly(jwt.Claims.Select(claim => new Claim(claim.Type, claim.Value, claim.ValueType, settings.Authority)).ToArray());
        }
        catch (Exception exception) when (exception is SecurityTokenException or OpenIdConnectProtocolException or JsonException or ArgumentException or FormatException or CryptographicException)
        { throw new OidcFailure(ExternalProtocolFailure.InvalidToken); }
    }

    private async Task<CredentialValue> Credential(ConnectionRecord connection, OidcSettings settings, CancellationToken token)
    {
        if (settings.Authentication == "none") return new(null, "public-client/v1");
        var reference = connection.Secrets["clientSecret"];
        if (string.IsNullOrWhiteSpace(reference.Name) || reference.TypeName is not (null or SecretTypeNames.Text))
            throw new OidcFailure(ExternalProtocolFailure.InvalidConfiguration);
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(identityOptions.Value.TenantId)));
        var resolver = scope.ServiceProvider.GetService<ISecretValueResolver>();
        if (resolver is null) throw new OidcFailure(ExternalProtocolFailure.SecretUnavailable);
        ResolvedSecret resolved;
        try { resolved = await resolver.ResolveAsync(identityOptions.Value.TenantId, reference, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new OidcFailure(ExternalProtocolFailure.SecretUnavailable); }
        var metadata = resolved.Metadata;
        if (!resolved.Succeeded || string.IsNullOrEmpty(resolved.Value) || resolved.Value.Length > 16384 || metadata is null ||
            string.IsNullOrEmpty(metadata.Id) || metadata.TenantId != identityOptions.Value.TenantId || metadata.Status != SecretStatus.Active ||
            metadata.TypeName != SecretTypeNames.Text || metadata.ExpiresAt <= clock.GetUtcNow())
            throw new OidcFailure(ExternalProtocolFailure.SecretUnavailable);
        return new(resolved.Value, Fingerprint(metadata.Id, reference.Name, reference.Scope, metadata.CurrentVersion?.ToString(CultureInfo.InvariantCulture),
            metadata.UpdatedAt?.UtcTicks.ToString(CultureInfo.InvariantCulture), resolved.Value));
    }

    private async ValueTask<T> Run<T>(Func<CancellationToken, Task<T>> action, Func<ExternalProtocolFailure, T> failure, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (options.Value.BackchannelTimeout <= TimeSpan.Zero || options.Value.BackchannelTimeout > TimeSpan.FromSeconds(30))
            return failure(ExternalProtocolFailure.InvalidConfiguration);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.BackchannelTimeout);
        try { return await action(timeout.Token); }
        catch (OidcFailure exception) { return failure(exception.Failure); }
        catch (SigningKeyMissing) { return failure(ExternalProtocolFailure.InvalidToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return failure(ExternalProtocolFailure.RemoteUnavailable); }
    }

    private bool Redirect(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && OidcSettings.Allowed(uri, options.Value, environment) &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;
    private static bool Opaque(string value) => value is { Length: >= 43 and <= 128 } && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '~');
    private static string Form(string value) => Uri.EscapeDataString(value).Replace("%20", "+", StringComparison.Ordinal);
    private static bool FixedEquals(string left, string right) => left is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private static string Fingerprint(params string?[] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        Span<byte> unit = stackalloc byte[sizeof(char)];
        foreach (var value in values)
        {
            BinaryPrimitives.WriteInt32BigEndian(length, value?.Length ?? -1); hash.AppendData(length);
            if (value is not null) foreach (var character in value) { BinaryPrimitives.WriteUInt16BigEndian(unit, character); hash.AppendData(unit); }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
    // A class deliberately avoids record-generated ToString exposing the resolved credential.
    private sealed class CredentialValue(string? value, string binding)
    {
        public string? Value { get; } = value;
        public string Binding { get; } = binding;
    }
    private sealed class SigningKeyMissing : Exception { }
}
