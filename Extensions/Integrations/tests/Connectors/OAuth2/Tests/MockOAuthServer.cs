using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

/// <summary>In-process mock OAuth 2.0 authorization, token, scopes and resource server.</summary>
public sealed class MockOAuthServer : HttpMessageHandler
{
    public const string CanaryAccessToken = "canary-access-token-2727";
    public const string CanaryRefreshToken = "canary-refresh-token-2727";
    public const string CanaryClientSecret = "canary-client-secret-2727";

    private readonly ConcurrentDictionary<string, IssuedCode> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IssuedToken> _tokens = new(StringComparer.Ordinal);
    private int _accessSerial;

    public Uri BaseAddress { get; } = new("https://oauth.test/");
    public Uri AuthorizationEndpoint => new(BaseAddress, "authorize");
    public Uri TokenEndpoint => new(BaseAddress, "token");
    public Uri ScopesEndpoint => new(BaseAddress, "scopes");
    public Uri ResourceEndpoint => new(BaseAddress, "resource");

    public string ExpectedClientId { get; set; } = "valence-client";
    public string ExpectedClientSecret { get; set; } = CanaryClientSecret;
    public int AccessTokenLifetimeSeconds { get; set; } = 3600;
    public bool ExpireImmediately { get; set; }
    public bool UnauthorizedOnce { get; set; }
    public bool RotateRefreshTokens { get; set; }
    public bool OmitRefreshTokenOnRefresh { get; set; }
    public bool OmitExpiresIn { get; set; }
    public int TokenRequestCount { get; private set; }

    public IReadOnlyCollection<string> IssuedAccessTokens => _tokens.Keys.ToArray();

    public string ApproveAuthorization(Uri authorizationUrl)
    {
        var query = HttpUtility.ParseQueryString(authorizationUrl.Query);
        var clientId = query["client_id"] ?? throw new InvalidOperationException("authorize is missing client_id.");
        var challenge = query["code_challenge"] ?? throw new InvalidOperationException("authorize is missing code_challenge.");
        var method = query["code_challenge_method"];
        if (!string.Equals(method, "S256", StringComparison.Ordinal))
            throw new InvalidOperationException("PKCE method must be S256.");
        if (!string.Equals(clientId, ExpectedClientId, StringComparison.Ordinal))
            throw new InvalidOperationException("Unknown client_id at authorize.");

        var code = $"code-{Guid.NewGuid():N}";
        _codes[code] = new IssuedCode(clientId, challenge, query["state"]);
        return code;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath.TrimEnd('/') ?? "";
        return Task.FromResult(path switch
        {
            "/authorize" => Authorize(request),
            "/token" => Token(request),
            "/scopes" => Json(HttpStatusCode.OK, new { scopes = new[] { "read", "write" } }),
            "/resource" => Resource(request),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
    }

    private HttpResponseMessage Authorize(HttpRequestMessage request)
    {
        if (request.RequestUri is null)
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        var code = ApproveAuthorization(request.RequestUri);
        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        var location = $"{query["redirect_uri"] ?? "https://app.test/callback"}?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(query["state"] ?? "")}";
        return new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri(location) } };
    }

    private HttpResponseMessage Token(HttpRequestMessage request)
    {
        TokenRequestCount++;
        var form = ReadForm(request);
        var grantType = form.Get("grant_type");
        var clientId = form.Get("client_id");
        var clientSecret = form.Get("client_secret");
        if (!string.Equals(clientId, ExpectedClientId, StringComparison.Ordinal) ||
            !string.Equals(clientSecret, ExpectedClientSecret, StringComparison.Ordinal))
            return Json(HttpStatusCode.Unauthorized, new { error = "invalid_client" });

        return grantType switch
        {
            "authorization_code" => ExchangeAuthorizationCode(form, clientId!),
            "client_credentials" => IssueTokens(form.Get("scope"), includeRefreshToken: false),
            "refresh_token" => Refresh(form),
            _ => Json(HttpStatusCode.BadRequest, new { error = "unsupported_grant_type" })
        };
    }

    private HttpResponseMessage ExchangeAuthorizationCode(System.Collections.Specialized.NameValueCollection form, string clientId)
    {
        var code = form.Get("code");
        var verifier = form.Get("code_verifier");
        if (code is null || !_codes.TryRemove(code, out var issued) || !string.Equals(issued.ClientId, clientId, StringComparison.Ordinal))
            return Json(HttpStatusCode.BadRequest, new { error = "invalid_grant" });
        if (verifier is null || !string.Equals(PkceChallenge(verifier), issued.Challenge, StringComparison.Ordinal))
            return Json(HttpStatusCode.BadRequest, new { error = "invalid_grant", error_description = "pkce" });
        return IssueTokens(scope: null, includeRefreshToken: true);
    }

    private HttpResponseMessage Refresh(System.Collections.Specialized.NameValueCollection form)
    {
        var refresh = form.Get("refresh_token");
        var match = _tokens.FirstOrDefault(pair => pair.Value.RefreshToken == refresh);
        if (match.Value is null || refresh is null)
            return Json(HttpStatusCode.BadRequest, new { error = "invalid_grant" });
        _tokens.TryRemove(match.Key, out _);
        return IssueTokens(match.Value.Scope, includeRefreshToken: !OmitRefreshTokenOnRefresh, reusedRefreshToken: OmitRefreshTokenOnRefresh ? refresh : null);
    }

    private HttpResponseMessage IssueTokens(string? scope, bool includeRefreshToken, string? reusedRefreshToken = null)
    {
        var access = ExpireImmediately
            ? $"expired-{Interlocked.Increment(ref _accessSerial)}"
            : $"{CanaryAccessToken}-{Interlocked.Increment(ref _accessSerial)}";
        var refreshToken = reusedRefreshToken;
        if (includeRefreshToken && refreshToken is null)
            refreshToken = $"{CanaryRefreshToken}-{access}";
        if (RotateRefreshTokens && refreshToken is not null && reusedRefreshToken is null)
            refreshToken = $"{refreshToken}-rotated";
        _tokens[access] = new IssuedToken(refreshToken, scope);

        var payload = new Dictionary<string, object?>
        {
            ["access_token"] = access,
            ["token_type"] = "Bearer",
            ["scope"] = scope
        };
        if (!OmitExpiresIn)
            payload["expires_in"] = ExpireImmediately ? 1 : AccessTokenLifetimeSeconds;
        if (refreshToken is not null)
            payload["refresh_token"] = refreshToken;
        return Json(HttpStatusCode.OK, payload);
    }

    private HttpResponseMessage Resource(HttpRequestMessage request)
    {
        var token = request.Headers.Authorization?.Parameter;
        if (string.IsNullOrWhiteSpace(token) || !_tokens.ContainsKey(token) || token.StartsWith("expired-", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        if (UnauthorizedOnce)
        {
            UnauthorizedOnce = false;
            _tokens.TryRemove(token, out var previous);
            if (previous is not null)
                _tokens[$"revoked-{token}"] = previous;
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        return Json(HttpStatusCode.OK, new { ok = true, connection = "used" });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) =>
        new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };

    private static System.Collections.Specialized.NameValueCollection ReadForm(HttpRequestMessage request)
    {
        var text = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
        return HttpUtility.ParseQueryString(text);
    }

    private static string PkceChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private sealed record IssuedCode(string ClientId, string Challenge, string? State);
    private sealed record IssuedToken(string? RefreshToken, string? Scope);
}
