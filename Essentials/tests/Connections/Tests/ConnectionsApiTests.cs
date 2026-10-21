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
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
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

        using var missingSecret = await client.SendAsync(Authed(HttpMethod.Post, "/connections", new
        {
            name = "missing-secret",
            typeName = "http",
            authScheme = "bearer",
            settings = new { allowedHosts = "api.example" },
            secretFields = new { token = new { sourceKind = "secret", existingSecretName = "missing-token" } }
        }));
        Assert.Equal(HttpStatusCode.BadRequest, missingSecret.StatusCode);
        Assert.DoesNotContain("value", await missingSecret.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var created = await client.SendAsync(Authed(HttpMethod.Post, "/connections", BearerBody("dup")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var duplicate = await client.SendAsync(Authed(HttpMethod.Post, "/connections", BearerBody("dup")));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task CreateNullCollectionAndCreateOrUpdateNullEntries_Return400()
    {
        await using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        using var nullSecretFields = await client.SendAsync(Authed(HttpMethod.Post, "/connections", new
        {
            name = "null-secret-fields",
            typeName = "http",
            authScheme = "bearer",
            settings = new { allowedHosts = "api.example" },
            secretFields = (object?)null
        }));
        Assert.Equal(HttpStatusCode.BadRequest, nullSecretFields.StatusCode);

        using var nullCreateField = await client.SendAsync(Authed(HttpMethod.Post, "/connections", new
        {
            name = "null-create-field",
            typeName = "http",
            authScheme = "bearer",
            settings = new { allowedHosts = "api.example" },
            secretFields = new { token = (object?)null }
        }));
        Assert.Equal(HttpStatusCode.BadRequest, nullCreateField.StatusCode);

        using var created = await client.SendAsync(Authed(HttpMethod.Post, "/connections", BearerBody("null-update-field")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var nullUpdateField = await client.SendAsync(Authed(HttpMethod.Put, "/connections/null-update-field", new
        {
            secretFields = new { token = (object?)null }
        }));
        Assert.Equal(HttpStatusCode.BadRequest, nullUpdateField.StatusCode);
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

    [Fact]
    public async Task Test_WithoutTestPermission_Returns403()
    {
        await using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        using var created = await client.SendAsync(Authed(HttpMethod.Post, "/connections", BearerBody("needs-test")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var tested = await client.SendAsync(Authed(
            HttpMethod.Post,
            "/connections/needs-test/test",
            permissions: "connections:manage"));
        Assert.Equal(HttpStatusCode.Forbidden, tested.StatusCode);
        var body = await tested.Content.ReadAsStringAsync();
        Assert.DoesNotContain("succeeded", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test_WithTestPermissionOnly_Succeeds()
    {
        await using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        using var created = await client.SendAsync(Authed(HttpMethod.Post, "/connections", BearerBody("test-only")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var tested = await client.SendAsync(Authed(
            HttpMethod.Post,
            "/connections/test-only/test",
            permissions: "connections:test"));
        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        var result = await tested.Content.ReadFromJsonAsync<ConnectionTestResult>(Json);
        Assert.True(result!.Succeeded);
    }

    public static TheoryData<string, string> ListingRoutes => new()
    {
        { "GET", "/connections" },
        { "GET", "/connections/descriptors" },
        { "GET", "/connections/listed" },
        { "POST", "/connections/picker" }
    };

    /// <summary>
    /// Listing connection metadata is implied by <c>workflows/design:read</c> (and so by <c>workflows/design:manage</c>)
    /// or by <c>connections:manage</c>, whose holder must see what they manage. The other connections rights and an
    /// unrelated grant do not imply it, and no listing carries a secret value.
    /// </summary>
    [Theory]
    [MemberData(nameof(ListingRoutes))]
    public async Task Listing_requires_workflow_design_read_or_connections_manage(string method, string route)
    {
        const string secretValue = "listed-canary-secret-value";
        await using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        using var created = await client.SendAsync(Authed(HttpMethod.Post, "/connections", BearerBody("listed", secretValue)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        foreach (var (grant, expected) in new[]
        {
            ("workflows/design:read", HttpStatusCode.OK),
            ("workflows/design:manage", HttpStatusCode.OK),
            ("connections:manage", HttpStatusCode.OK),
            ("connections:bind-secret,connections:test", HttpStatusCode.Forbidden),
            ("workflows/runtime:read", HttpStatusCode.Forbidden)
        })
        {
            using var response = await client.SendAsync(Authed(new HttpMethod(method), route, method == "POST" ? new { } : null, grant));
            Assert.True(expected == response.StatusCode, $"'{grant}' on {method} {route} returned {(int)response.StatusCode}, not {(int)expected}.");
            Assert.DoesNotContain(secretValue, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    public static TheoryData<string, string, string> AdministrationRoutes => new()
    {
        { "POST", "/connections", "connections:manage" },
        { "PUT", "/connections/administered", "connections:manage" },
        { "POST", "/connections/administered/revoke", "connections:manage" },
        { "GET", "/connections/administered/oauth-redirect-uri", "connections:manage" },
        { "POST", "/connections/administered/test", "connections:test" },
        { "DELETE", "/connections/administered", "connections:manage" }
    };

    /// <summary>The connections admin rights are unchanged: each administration route still needs its own right, which no workflow design grant implies.</summary>
    [Theory]
    [MemberData(nameof(AdministrationRoutes))]
    public async Task Administration_still_requires_its_own_connections_right(string method, string route, string right)
    {
        await using var host = await StartHostAsync();
        using var client = host.GetTestClient();
        using var created = await client.SendAsync(Authed(HttpMethod.Post, "/connections", BearerBody("administered")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        object? body = (method, route) switch
        {
            ("POST", "/connections") => BearerBody("created-by-an-administrator"),
            ("PUT", _) => new { displayName = "Renamed" },
            _ => null
        };

        using var designer = await client.SendAsync(Authed(new HttpMethod(method), route, body, "workflows/design:manage"));
        Assert.Equal(HttpStatusCode.Forbidden, designer.StatusCode);

        using var administrator = await client.SendAsync(Authed(new HttpMethod(method), route, body, right));
        Assert.True(administrator.IsSuccessStatusCode, $"'{right}' on {method} {route} returned {(int)administrator.StatusCode}.");
    }

    [Fact]
    public async Task Binding_an_existing_secret_still_requires_bind_secret()
    {
        await using var host = await StartHostAsync();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISecretManager>().CreateAsync("tenant-a", new CreateSecretRequest
            {
                Name = "existing-token",
                Value = "tok",
                TypeName = SecretTypeNames.Text
            });
        }

        using var client = host.GetTestClient();
        var body = new
        {
            name = "bound",
            typeName = "http",
            authScheme = "bearer",
            settings = new { allowedHosts = "api.example" },
            secretFields = new { token = new { sourceKind = "secret", existingSecretName = "existing-token" } }
        };

        using var refused = await client.SendAsync(Authed(HttpMethod.Post, "/connections", body, "connections:manage,workflows/design:manage"));
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        using var bound = await client.SendAsync(Authed(HttpMethod.Post, "/connections", body, "connections:manage,connections:bind-secret"));
        Assert.Equal(HttpStatusCode.Created, bound.StatusCode);
    }

    private static object BearerBody(string name, string token = "tok") => new
    {
        name,
        typeName = "http",
        authScheme = "bearer",
        settings = new { allowedHosts = "api.example" },
        secretFields = new { token = new { value = token } }
    };

    private static HttpRequestMessage Authed(HttpMethod method, string path, object? body = null, string? permissions = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Identity", "admin");
        if (!string.IsNullOrWhiteSpace(permissions))
            request.Headers.Add("X-Test-Permissions", permissions);
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

            var permissions = Request.Headers.TryGetValue("X-Test-Permissions", out var requested) && !string.IsNullOrWhiteSpace(requested)
                ? requested.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : [PermissionKey.Wildcard];
            var claims = permissions
                .Select(permission => new Claim(IdentityClaimTypes.Permission, permission))
                .Append(new Claim(IdentityClaimTypes.Normalized, "v1"))
                .Append(new Claim(IdentityClaimTypes.Provider, "test"))
                .Append(new Claim(IdentityClaimTypes.TenantId, "tenant-a"));
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
