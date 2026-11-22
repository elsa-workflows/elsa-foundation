using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

/// <summary>Owned HTTP protocol fixture with real signed tokens, client authentication and single-use PKCE codes.</summary>
internal sealed class ControlledOidcServer : IAsyncDisposable
{
    private WebApplication application = null!;
    private RSA key = RSA.Create(2048);
    private string keyId = Guid.NewGuid().ToString("N");
    private readonly ConcurrentDictionary<string, Grant> codes = new(StringComparer.Ordinal);
    public string Origin { get; private set; } = "";
    public string Issuer => Origin + "/issuer";
    public const string ClientId = "elsa-client";
    public const string ClientSecret = "client secret +/:?&";
    public const string Callback = "https://elsa.example/identity/external/company/callback";
    public HashSet<string> RegisteredCallbacks { get; } = new(StringComparer.Ordinal) { Callback };
    public Action<Dictionary<string, object>>? ChangeMetadata { get; set; }
    public string TokenScenario { get; set; } = "valid";
    public string Authentication { get; set; } = "client_secret_basic";
    public string Subject { get; set; } = "opaque-Subject";
    public bool RedirectDiscovery { get; set; }
    public bool DelayDiscovery { get; set; }
    public bool RotateOnToken { get; set; }
    public int TokenRequests;
    public int KeyRequests;
    private int denyNextAuthorization;
    public void DenyNextAuthorization() => Interlocked.Exchange(ref denyNextAuthorization, 1);

    public static async Task<ControlledOidcServer> Start()
    {
        var server = new ControlledOidcServer();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        server.application = builder.Build();
        server.application.MapGet("/issuer/.well-known/openid-configuration", (Delegate)(async Task<IResult> (HttpContext context) =>
        {
            if (server.DelayDiscovery) await Task.Delay(Timeout.Infinite, context.RequestAborted);
            if (server.RedirectDiscovery) return Results.Redirect(server.Origin + "/redirect-target");
            var metadata = new Dictionary<string, object>
            {
                ["issuer"] = server.Issuer, ["authorization_endpoint"] = server.Origin + "/authorize",
                ["token_endpoint"] = server.Origin + "/token", ["jwks_uri"] = server.Origin + "/keys",
                ["response_types_supported"] = new[] { "code" }, ["grant_types_supported"] = new[] { "authorization_code" },
                ["id_token_signing_alg_values_supported"] = new[] { "RS256", "HS256" },
                ["code_challenge_methods_supported"] = new[] { "S256" },
                ["token_endpoint_auth_methods_supported"] = new[] { "client_secret_basic", "client_secret_post", "none" },
                ["authorization_response_iss_parameter_supported"] = true
            };
            server.ChangeMetadata?.Invoke(metadata);
            return Results.Json(metadata);
        }));
        server.application.MapGet("/keys", () =>
        {
            Interlocked.Increment(ref server.KeyRequests);
            var rsa = server.key.ExportParameters(false);
            return Results.Json(new { keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = server.keyId, n = Base64UrlEncoder.Encode(rsa.Modulus), e = Base64UrlEncoder.Encode(rsa.Exponent) } } });
        });
        server.application.MapGet("/authorize", server.Authorize);
        server.application.MapPost("/token", (Delegate)server.Token);
        await server.application.StartAsync();
        server.Origin = server.application.Urls.Single().TrimEnd('/');
        return server;
    }

    public void RotateKey()
    {
        key.Dispose(); key = RSA.Create(2048); keyId = Guid.NewGuid().ToString("N");
    }

    private IResult Authorize(HttpContext context)
    {
        var query = context.Request.Query;
        var callback = query["redirect_uri"].ToString();
        if (query["client_id"] != ClientId || !RegisteredCallbacks.Contains(callback) || query["response_type"] != "code" ||
            query["code_challenge_method"] != "S256" || string.IsNullOrEmpty(query["nonce"]) || string.IsNullOrEmpty(query["state"]))
            return Results.BadRequest();
        if (Interlocked.Exchange(ref denyNextAuthorization, 0) == 1)
            return Results.Redirect(QueryHelpers.AddQueryString(callback, new Dictionary<string, string?>
                { ["error"] = "access_denied", ["state"] = query["state"], ["iss"] = Issuer }));
        var code = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        codes[code] = new(query["nonce"].ToString(), query["code_challenge"].ToString(), callback, Subject);
        return Results.Redirect(QueryHelpers.AddQueryString(callback, new Dictionary<string, string?>
            { ["code"] = code, ["state"] = query["state"], ["iss"] = Issuer }));
    }

    private async Task<IResult> Token(HttpContext context)
    {
        Interlocked.Increment(ref TokenRequests);
        var form = await context.Request.ReadFormAsync();
        var client = form["client_id"].ToString();
        var secret = form["client_secret"].ToString();
        var basic = context.Request.Headers.Authorization.ToString();
        if (basic.StartsWith("Basic ", StringComparison.Ordinal))
        {
            var pair = Encoding.UTF8.GetString(Convert.FromBase64String(basic[6..])).Split(':', 2);
            client = WebUtility.UrlDecode(pair[0]); secret = WebUtility.UrlDecode(pair[1]);
        }
        if (client != ClientId ||
            (Authentication == "client_secret_basic" && !basic.StartsWith("Basic ", StringComparison.Ordinal)) ||
            (Authentication == "client_secret_post" && (basic.Length != 0 || form["client_secret"].Count != 1)) ||
            (Authentication == "none" ? secret.Length != 0 || basic.Length != 0 : secret != ClientSecret))
            return Results.Json(new { error = "invalid_client", error_description = ClientSecret }, statusCode: 400);
        var code = form["code"].ToString();
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
        if (form["grant_type"] != "authorization_code" || !codes.TryGetValue(code, out var grant) || form["redirect_uri"] != grant.Callback ||
            grant.Challenge != challenge || !codes.TryRemove(new KeyValuePair<string, Grant>(code, grant)))
            return Results.Json(new { error = "invalid_grant", error_description = "sensitive upstream diagnostic" }, statusCode: 400);
        if (RotateOnToken) { RotateKey(); RotateOnToken = false; }
        var payload = new Dictionary<string, object>
        {
            ["iss"] = Issuer, ["sub"] = grant.Subject, ["aud"] = ClientId,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), ["nbf"] = DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeSeconds(),
            ["exp"] = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(), ["nonce"] = grant.Nonce,
            ["email"] = "ada@example.test", ["email_verified"] = true, ["name"] = "Ada", ["roles"] = new[] { "external-admin" }
        };
        switch (TokenScenario)
        {
            case "nonce": payload["nonce"] = "wrong-nonce"; break;
            case "audience": payload["aud"] = "another-client"; break;
            case "issuer": payload["iss"] = Issuer + "/"; break;
            case "expired": payload["exp"] = DateTimeOffset.UtcNow.AddMinutes(-2).ToUnixTimeSeconds(); break;
            case "missing-subject": payload.Remove("sub"); break;
            case "missing-nonce": payload.Remove("nonce"); break;
            case "missing-iat": payload.Remove("iat"); break;
            case "future-iat": payload["iat"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(); break;
            case "string-iat": payload["iat"] = "not-a-number"; break;
            case "string-exp": payload["exp"] = "not-a-number"; break;
            case "azp": payload["azp"] = "another-client"; break;
            case "multiple-audiences": payload["aud"] = new[] { ClientId, "untrusted" }; payload["azp"] = ClientId; break;
            case "access-hash": payload["at_hash"] = "wrong-hash"; break;
        }
        var signatureKey = TokenScenario == "signature" ? RSA.Create(2048) : key;
        try
        {
            var header = Base64UrlEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = TokenScenario == "token-type" ? "at+jwt" : "JWT", kid = keyId }));
            var json = JsonSerializer.Serialize(payload);
            if (TokenScenario == "duplicate-subject") json = json[..^1] + ",\"sub\":\"different\"}";
            var body = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(json));
            var input = header + "." + body;
            var jwt = input + "." + Base64UrlEncoder.Encode(signatureKey.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            return Results.Json(new { id_token = jwt, access_token = "upstream-access-not-retained", refresh_token = "upstream-refresh-not-retained", token_type = "Bearer" });
        }
        finally { if (signatureKey != key) signatureKey.Dispose(); }
    }

    public async ValueTask DisposeAsync() { await application.DisposeAsync(); key.Dispose(); }
    private sealed record Grant(string Nonce, string Challenge, string Callback, string Subject);
}
