using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Connections.Api.Features;
using Elsa.Connections.Core.Contracts;
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
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class ConnectionsApiTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Create_MapsUnknownOwnerTo400_AndDuplicateNameTo409()
    {
        await using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        using var badOwner = await client.SendAsync(Authed(HttpMethod.Post, "/connections", new
        {
            name = "bad-owner",
            typeName = "http",
            authScheme = "bearer",
            owner = "user",
            settings = new { allowedHosts = "api.example" },
            secretFields = new { token = new { value = "tok" } }
        }));
        Assert.Equal(HttpStatusCode.BadRequest, badOwner.StatusCode);

        using var created = await client.SendAsync(Authed(HttpMethod.Post, "/connections", BearerBody("dup")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var duplicate = await client.SendAsync(Authed(HttpMethod.Post, "/connections", BearerBody("dup")));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task OAuthRedirectUri_RequiresPublicBaseUrl()
    {
        await using var host = await StartHostAsync(publicBaseUrl: null);
        using var client = host.GetTestClient();

        using var response = await client.SendAsync(Authed(HttpMethod.Get, "/connections/any/oauth-redirect-uri"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("PublicBaseUrl", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Test_ReturnsAFailedResultForARevokedConnection()
    {
        await using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        using var created = await client.SendAsync(Authed(HttpMethod.Post, "/connections", BearerBody("revoked-one")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var revoked = await client.SendAsync(Authed(HttpMethod.Post, "/connections/revoked-one/revoke"));
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);

        using var tested = await client.SendAsync(Authed(HttpMethod.Post, "/connections/revoked-one/test"));
        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        var result = await tested.Content.ReadFromJsonAsync<ConnectionTestResult>(Json);
        Assert.False(result!.Succeeded);
        Assert.Contains("Revoked", result.Message, StringComparison.Ordinal);
    }

    private static object BearerBody(string name) => new
    {
        name,
        typeName = "http",
        authScheme = "bearer",
        settings = new { allowedHosts = "api.example" },
        secretFields = new { token = new { value = "tok" } }
    };

    private static HttpRequestMessage Authed(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Identity", "admin");
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<WebApplication> StartHostAsync(string? publicBaseUrl = "https://studio.example")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Secrets:EncryptionKey"] = "connections-api-tests-encryption-key",
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
        builder.Services.AddSecrets(configuration);
        builder.Services.Configure<SecretsOptions>(options => options.EncryptionKey ??= "connections-api-tests-encryption-key");
        builder.Services.AddConnections(configuration);
        builder.Services.Configure<Elsa.Connections.Options.ConnectionsOptions>(options => options.PublicBaseUrl = publicBaseUrl);
        new ConnectionsApiFeature().ConfigureServices(builder.Services);

        var app = builder.Build();
        Assert.NotNull(app.Services.GetService<IConnectionManager>());
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

            var claims = new[]
            {
                new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard),
                new Claim(IdentityClaimTypes.Normalized, "v1"),
                new Claim(IdentityClaimTypes.Provider, "test"),
                new Claim(IdentityClaimTypes.TenantId, "tenant-a")
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
