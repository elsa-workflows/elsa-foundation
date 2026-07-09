using System.Net;
using Elsa.Studio.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

/// <summary>
/// Exercises the real <see cref="StudioBridgeAuth"/> wiring (backend-bearer introspection scheme + policy) end to end
/// against a TestServer that maps probe endpoints behind <see cref="StudioBridgeAuth.PolicyName"/>. This is the single
/// gate for every browser-facing Studio host-control endpoint after ADR 0037 removed the browser-held management key:
/// module/feature/theme management, the console-stream hub, and the Studio management bridge all sit behind it.
///
/// The scheme validates the browser bearer by forwarding it to the backend's <c>/_elsa/identity/session</c> endpoint;
/// these tests route that named client through a stub backend so the assertions are about the gate's HTTP behaviour,
/// not the network. The browser never carries an Elsa host management key on any of these requests.
/// </summary>
public sealed class StudioBridgeAuthTests : IAsyncDisposable
{
    private const string BackendBaseUrl = "https://backend.example/";
    private const string ValidBearer = "valid-user-bearer";

    // A path under the query-token prefix (a browser WebSocket/SSE handshake) stands in for the console-stream hub,
    // and a plain REST probe that is not under it.
    private const string HubPath = "/hub";

    private WebApplication? _app;

    [Fact]
    public async Task AllowsAnonymouslyWhenStudioAuthDisabled()
    {
        // Demo mode (Studio:Auth:Enabled absent/false): the gate must not break the anonymous shell.
        var client = await StartGatedHostAsync(authEnabled: false);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectsRequestWithoutBearerWhenAuthEnabled()
    {
        var client = await StartGatedHostAsync(authEnabled: true);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowsRequestWithValidBearerHeaderWhenAuthEnabled()
    {
        var client = await StartGatedHostAsync(authEnabled: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", ValidBearer);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectsRequestWithInvalidBearerHeaderWhenAuthEnabled()
    {
        var client = await StartGatedHostAsync(authEnabled: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-recognized-token");

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowsValidAccessTokenQueryParameterOnHubPathWhenAuthEnabled()
    {
        // Browsers cannot attach headers to the WebSocket/SSE handshake; SignalR falls back to ?access_token=, honoured
        // only on the configured hub path.
        var client = await StartGatedHostAsync(authEnabled: true);

        var response = await client.PostAsync($"{HubPath}?{StudioBridgeAuth.AccessTokenQueryParameterName}={ValidBearer}", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RejectsValidAccessTokenQueryParameterOnNonHubPathWhenAuthEnabled()
    {
        // Off the hub path the query credential is ignored so the bearer never lands in access logs of REST endpoints.
        var client = await StartGatedHostAsync(authEnabled: true);

        var response = await client.PostAsync($"/gated?{StudioBridgeAuth.AccessTokenQueryParameterName}={ValidBearer}", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RejectsInvalidAccessTokenQueryParameterOnHubPathWhenAuthEnabled()
    {
        var client = await StartGatedHostAsync(authEnabled: true);

        var response = await client.PostAsync($"{HubPath}?{StudioBridgeAuth.AccessTokenQueryParameterName}=not-a-recognized-token", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FailsClosedWhenAuthEnabledButNoBackendBaseUrlConfigured()
    {
        // With no backend to validate the bearer against, the enabled gate cannot trust anyone.
        var client = await StartGatedHostAsync(authEnabled: true, backendBaseUrl: null);
        client.DefaultRequestHeaders.Authorization = new("Bearer", ValidBearer);

        var response = await client.PostAsync("/gated", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpClient> StartGatedHostAsync(bool authEnabled, string? backendBaseUrl = BackendBaseUrl)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Studio:Auth:Enabled"] = authEnabled ? "true" : "false",
            [StudioBackendManagementOptions.BackendBaseUrlConfigurationKey] = backendBaseUrl
        };

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddStudioBridgeAuth(builder.Configuration, HubPath);

        // Route the bearer-introspection client through a stub backend session endpoint that recognizes exactly the
        // ValidBearer, so the tests assert real gate behaviour without a network dependency.
        builder.Services.AddHttpClient(StudioBridgeAuthHandler.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new StubBackendSession(ValidBearer));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        // Two probes behind the same policy: "/gated" is a plain REST endpoint, "/hub" sits under the query-token
        // prefix and stands in for the console-stream hub's WebSocket/SSE handshake.
        app.MapPost("/gated", () => Results.Ok())
            .RequireAuthorization(StudioBridgeAuth.PolicyName);
        app.MapPost(HubPath, () => Results.Ok())
            .RequireAuthorization(StudioBridgeAuth.PolicyName);

        await app.StartAsync();
        _app = app;
        return app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }

    /// <summary>
    /// Stands in for the backend's anonymous <c>/_elsa/identity/session</c> endpoint: it reflects an
    /// <c>authenticated</c> session only when the forwarded bearer matches the recognized token, otherwise
    /// <c>anonymous</c>. Mirrors the real endpoint's contract the gate depends on.
    /// </summary>
    private sealed class StubBackendSession(string recognizedBearer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var forwarded = request.Headers.Authorization?.Parameter;
            var authenticated = string.Equals(forwarded, recognizedBearer, StringComparison.Ordinal);
            var json = authenticated
                ? """{ "status": "authenticated", "subject": "alice" }"""
                : """{ "status": "anonymous" }""";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
