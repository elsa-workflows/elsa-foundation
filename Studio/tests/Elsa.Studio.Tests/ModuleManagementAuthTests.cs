using System.Net;
using Elsa.Studio.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

/// <summary>
/// Exercises the real <see cref="ModuleManagementAuth"/> wiring (API-key scheme + policy) end to end against a
/// TestServer that maps a probe endpoint behind <see cref="ModuleManagementAuth.PolicyName"/>, mirroring how the
/// module-management, feature-management, and console-stream surfaces are gated in Program.cs.
/// </summary>
public sealed class ModuleManagementAuthTests : IAsyncDisposable
{
    private const string ValidKey = "s3cr3t-management-key";

    // A path under the query-token prefix (a browser WebSocket/SSE handshake) and one that is not.
    private const string HubPath = "/hub";
    private const string ScopedPath = $"{HubPath}?{ModuleManagementAuth.AccessTokenQueryParameterName}={ValidKey}";
    private const string UnscopedPath = $"/gated?{ModuleManagementAuth.AccessTokenQueryParameterName}={ValidKey}";

    private WebApplication? _app;

    [Fact]
    public async Task RejectsRequestWithoutApiKey()
    {
        var client = await StartGatedHostAsync(apiKey: ValidKey);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RejectsRequestWithWrongApiKey()
    {
        var client = await StartGatedHostAsync(apiKey: ValidKey);
        client.DefaultRequestHeaders.Add(ModuleManagementAuth.ApiKeyHeaderName, "not-the-key");

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowsRequestWithValidApiKey()
    {
        var client = await StartGatedHostAsync(apiKey: ValidKey);
        client.DefaultRequestHeaders.Add(ModuleManagementAuth.ApiKeyHeaderName, ValidKey);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AllowsRequestWithValidBearerToken()
    {
        // The SignalR client sends its access token as "Authorization: Bearer <token>" on negotiate and long-poll requests.
        var client = await StartGatedHostAsync(apiKey: ValidKey);
        client.DefaultRequestHeaders.Authorization = new("Bearer", ValidKey);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AllowsRequestWithValidAccessTokenQueryParameterOnScopedPath()
    {
        // WebSocket and SSE requests cannot carry headers from a browser; SignalR falls back to ?access_token=,
        // which is honoured only on the configured hub path.
        var client = await StartGatedHostAsync(apiKey: ValidKey);

        var response = await client.PostAsync(ScopedPath, content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectsValidAccessTokenQueryParameterOnNonScopedPath()
    {
        // Off the hub path the query credential is ignored so the key never lands in access logs of REST endpoints.
        var client = await StartGatedHostAsync(apiKey: ValidKey);

        var response = await client.PostAsync(UnscopedPath, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RejectsRequestWithWrongAccessTokenQueryParameterOnScopedPath()
    {
        var client = await StartGatedHostAsync(apiKey: ValidKey);

        var response = await client.PostAsync($"{HubPath}?{ModuleManagementAuth.AccessTokenQueryParameterName}=not-the-key", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowsValidAccessTokenAlongsideForeignMismatchingBearerOnScopedPath()
    {
        // A gateway may inject its own Authorization: Bearer <jwt>; the valid ?access_token= must still win.
        var client = await StartGatedHostAsync(apiKey: ValidKey);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "someone-elses-jwt");

        var response = await client.PostAsync(ScopedPath, content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectsForeignBearerAlone()
    {
        // A foreign bearer belongs to another scheme; under the built-in single scheme NoResult still 401s.
        var client = await StartGatedHostAsync(apiKey: ValidKey);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "someone-elses-jwt");

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowsValidAccessTokenWhenDedicatedHeaderIsEmptyOnScopedPath()
    {
        // A present-but-empty management-key header must not shadow a valid query credential.
        var client = await StartGatedHostAsync(apiKey: ValidKey);
        client.DefaultRequestHeaders.Add(ModuleManagementAuth.ApiKeyHeaderName, string.Empty);

        var response = await client.PostAsync(ScopedPath, content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AllowsRequestWhenOneOfMultipleAuthorizationHeadersIsValidBearer()
    {
        // Multiple Authorization values must be evaluated individually, not comma-joined.
        var client = await StartGatedHostAsync(apiKey: ValidKey);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer someone-elses-jwt");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {ValidKey}");

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FailsClosedWhenNoKeyConfiguredAndAnonymousNotAllowed()
    {
        var client = await StartGatedHostAsync(apiKey: null, allowAnonymous: false);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowsAnonymousWhenNoKeyConfiguredAndOptOutEnabled()
    {
        // The development opt-out (defaulted true only in appsettings.Development.json) keeps local dev functional.
        var client = await StartGatedHostAsync(apiKey: null, allowAnonymous: true);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpClient> StartGatedHostAsync(string? apiKey, bool allowAnonymous = false)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Studio:BackendModuleManagementApiKey"] = apiKey,
            ["Studio:AllowAnonymousManagementApi"] = allowAnonymous ? "true" : "false"
        };

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddModuleManagementAuth(builder.Configuration, HubPath);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        // Two probes behind the same policy: "/gated" is a plain REST endpoint, "/hub" sits under the
        // query-token prefix and stands in for the console-stream hub's WebSocket/SSE handshake.
        app.MapPost("/gated", () => Results.Ok())
            .RequireAuthorization(ModuleManagementAuth.PolicyName);
        app.MapPost(HubPath, () => Results.Ok())
            .RequireAuthorization(ModuleManagementAuth.PolicyName);

        await app.StartAsync();
        _app = app;
        return app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}
