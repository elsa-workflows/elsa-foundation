using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Web;
using Elsa.Connections.Api;
using Elsa.Connections.Api.Features;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Extensions;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class AuthorizeBeginApiTests
{
    [Fact]
    public async Task Unauthorized_request_is_refused()
    {
        await using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/connections/http-oauth/oauth/authorize"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_for_a_different_tenant_is_refused()
    {
        await using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        await CreateAuthorizationCodeConnectionAsync(client);

        using var response = await client.SendAsync(Authed(
            HttpMethod.Post,
            "/connections/http-oauth/oauth/authorize",
            tenantId: "tenant-b"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Valid_request_returns_authorize_url_with_public_base_redirect()
    {
        await using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        await CreateAuthorizationCodeConnectionAsync(client);

        using var response = await client.SendAsync(Authed(HttpMethod.Post, "/connections/http-oauth/oauth/authorize"));
        var errorBody = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, errorBody);
        var body = JsonSerializer.Deserialize<OAuthAuthorizeUrlResponse>(errorBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.False(string.IsNullOrWhiteSpace(body?.AuthorizationUrl));

        var query = HttpUtility.ParseQueryString(new Uri(body!.AuthorizationUrl).Query);
        Assert.Equal(
            "https://app.example/_elsa/connections/oauth/callback",
            query["redirect_uri"]);
        Assert.Equal(ConnectionsApi.OAuthCallbackPath, new Uri(query["redirect_uri"]!).AbsolutePath);
        Assert.False(string.IsNullOrWhiteSpace(query["state"]));
        Assert.True(OAuth2TokenService.TryParseState(query["state"], out var tenantId, out var connectionName, out _));
        Assert.Equal("tenant-a", tenantId);
        Assert.Equal("http-oauth", connectionName);
    }

    private static async Task CreateAuthorizationCodeConnectionAsync(HttpClient client)
    {
        using var created = await client.SendAsync(Authed(HttpMethod.Post, "/connections", new
        {
            name = "http-oauth",
            typeName = "http",
            authScheme = "oauth2",
            settings = new
            {
                grantType = "authorization_code",
                tokenUrl = "https://oauth.test/token",
                authorizationUrl = "https://oauth.test/authorize",
                clientId = "valence-client",
                allowedHosts = "oauth.test",
                scopes = "read write"
            },
            secretFields = new
            {
                clientSecret = new { value = "canary-client-secret-2727" }
            }
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, object? body = null, string? tenantId = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Identity", "admin");
        if (!string.IsNullOrWhiteSpace(tenantId))
            request.Headers.Add("X-Test-Tenant", tenantId);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<WebApplication> StartHostAsync()
    {
        const string publicBaseUrl = "https://app.example";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Secrets:EncryptionKey"] = "oauth2-authorize-api-tests-key",
                ["Elsa:Connections:PublicBaseUrl"] = publicBaseUrl
            })
            .Build();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddAuthentication(HeaderAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(HeaderAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal)
            {
                HeaderAuthenticationHandler.SchemeName
            });
        builder.Services.AddHttpClient();
        builder.Services.AddSecrets(configuration);
        builder.Services.Configure<SecretsOptions>(options => options.EncryptionKey ??= "oauth2-authorize-api-tests-key");
        builder.Services.AddConnections(configuration);
        builder.Services.Configure<Elsa.Connections.Options.ConnectionsOptions>(options => options.PublicBaseUrl = publicBaseUrl);
        builder.Services.AddOAuth2Connector();
        new ConnectionsApiFeature().ConfigureServices(builder.Services);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        new ConnectionsApiFeature().MapEndpoints(app, app.Environment);
        await app.StartAsync();
        return app;
    }

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Identity", out var identity) || string.IsNullOrWhiteSpace(identity))
                return Task.FromResult(AuthenticateResult.NoResult());

            var tenantId = Request.Headers.TryGetValue("X-Test-Tenant", out var tenant) && !string.IsNullOrWhiteSpace(tenant)
                ? tenant.ToString()
                : "tenant-a";
            var claims = new[]
            {
                new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard),
                new Claim(IdentityClaimTypes.Normalized, "v1"),
                new Claim(IdentityClaimTypes.Provider, "test"),
                new Claim(IdentityClaimTypes.TenantId, tenantId)
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
    }
}
