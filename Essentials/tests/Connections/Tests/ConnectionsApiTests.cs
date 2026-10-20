using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Connections.Api.Features;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public async Task Update_MapsACredentialWriteFailureTo500WithoutLeakingTheStoreFailure()
    {
        var body = await UpdateFailingWithAsync(name => new ConnectionCredentialWriteException(
            name,
            ["token"],
            $"Connection '{name}' credential(s) 'token' were not written. Its other changes were saved.",
            new IOException("Connection refused by db-host-1")));

        Assert.Contains("token", body);
        Assert.DoesNotContain("db-host-1", body);
    }

    [Fact]
    public async Task Update_MapsAStoreFailureBeforeTheSaveTo500WithoutLeakingTheStoreFailure()
    {
        var body = await UpdateFailingWithAsync(name => new ConnectionStoreException(
            name,
            $"Connection '{name}' was not updated: the connection or secret store failed before the change was confirmed saved. Retry; if the retry reports a credential secret name as taken or held, use a different connection name or bind an existing secret.",
            new IOException("Connection refused by db-host-1")));

        Assert.Contains("not updated", body);
        Assert.DoesNotContain("db-host-1", body);
    }

    private async Task<string> UpdateFailingWithAsync(Func<string, Exception> failure)
    {
        await using var host = await StartHostAsync(configure: services =>
        {
            services.RemoveAll<IConnectionManager>();
            services.AddScoped<IConnectionManager>(_ => new FailingConnectionManager(failure));
        });
        using var client = host.GetTestClient();

        using var response = await client.SendAsync(Authed(HttpMethod.Put, "/connections/any", new { displayName = "Renamed" }));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
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
    public async Task Test_WithTestPermissionOnly_DoesNotRequireUse()
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

    private static object BearerBody(string name) => new
    {
        name,
        typeName = "http",
        authScheme = "bearer",
        settings = new { allowedHosts = "api.example" },
        secretFields = new { token = new { value = "tok" } }
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

    private static async Task<WebApplication> StartHostAsync(string? publicBaseUrl = "https://studio.example", Action<IServiceCollection>? configure = null)
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
        configure?.Invoke(builder.Services);

        var app = builder.Build();
        Assert.NotNull(app.Services.GetService<IConnectionManager>());
        app.UseAuthentication();
        app.UseAuthorization();
        new ConnectionsApiFeature().MapEndpoints(app, app.Environment);
        await app.StartAsync();
        return app;
    }

    /// <summary>Fails every update with the exception the default manager throws for a store outage.</summary>
    private sealed class FailingConnectionManager(Func<string, Exception> failure) : IConnectionManager
    {
        public ValueTask<ConnectionMetadata?> UpdateAsync(string tenantId, string name, UpdateConnectionRequest request, CancellationToken cancellationToken = default) =>
            throw failure(name);

        public ValueTask<ConnectionMetadata> CreateAsync(string tenantId, CreateConnectionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ConnectionMetadata?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<ConnectionMetadata>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ConnectionTestResult> TestAsync(string tenantId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ConnectionMetadata?> RevokeAsync(string tenantId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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
