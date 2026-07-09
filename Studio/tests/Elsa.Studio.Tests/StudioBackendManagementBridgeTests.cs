using System.Net;
using System.Net.Http.Json;
using Elsa.Studio.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

/// <summary>
/// Exercises the Studio management bridge (ADR 0037) at its HTTP boundary: a TestServer hosts the real bridge wiring
/// (<see cref="StudioBackendManagementBridge"/> + client + interim auth), and a recording stub stands in for the
/// backend host so status mapping and the "zero outbound calls when unconfigured" fail-closed guarantee are asserted
/// against the wire, not mocked internals.
/// </summary>
public sealed class StudioBackendManagementBridgeTests : IAsyncDisposable
{
    private const string StatusRoute = "/_elsa/studio/backend-management/status";
    private const string BackendBaseUrl = "https://backend.example";
    private const string ManagementKey = "s3cr3t-management-key";

    private WebApplication? _app;

    [Fact]
    public async Task ReturnsAvailableWhenBackendAcceptsTheManagementKey()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("""{ "host": { "id": "backend" }, "modules": [] }"""));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Available, status.Status);
        Assert.Single(backend.Requests);
        // The management key rides only on the Studio->backend call.
        Assert.Equal(ManagementKey, backend.Requests[0].ManagementKey);
    }

    [Fact]
    public async Task ReturnsUnconfiguredWithZeroOutboundCallsWhenNoManagementKey()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: null);

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unconfigured, status.Status);
        // Fail closed: no outbound backend request may be issued.
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task ReturnsUnconfiguredWithZeroOutboundCallsWhenNoBackendBaseUrl()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: null, managementKey: ManagementKey);

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unconfigured, status.Status);
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task ReturnsUnauthorizedWhenBackendRejectsTheManagementKey()
    {
        var backend = RecordingBackend.RespondingWith(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: "wrong-key");

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unauthorized, status.Status);
    }

    [Fact]
    public async Task ReturnsUnauthorizedWhenBackendSurfaceIsDisabled()
    {
        // The backend hides its management surface (404) when it has no key configured; from Studio's side that maps to
        // unauthorized — the operator must reconcile the key on one side.
        var backend = RecordingBackend.RespondingWith(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unauthorized, status.Status);
    }

    [Fact]
    public async Task ReturnsUnreachableWhenBackendTransportFails()
    {
        var backend = RecordingBackend.Throwing(new HttpRequestException("connection refused"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unreachable, status.Status);
    }

    [Fact]
    public async Task ReturnsDegradedWhenBackendRespondsWithServerError()
    {
        var backend = RecordingBackend.RespondingWith(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Degraded, status.Status);
    }

    [Fact]
    public async Task NeverEchoesTheManagementKeyOrRequiresItFromTheBrowser()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("""{ "modules": [] }"""));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        // The browser sends no management key; the bridge still answers (auth disabled) and never leaks the key.
        var response = await client.GetAsync(StatusRoute);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(ManagementKey, body);
    }

    [Fact]
    public async Task RejectsUnauthenticatedBrowserRequestWhenStudioAuthEnabled()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey, authEnabled: true);

        var response = await client.GetAsync(StatusRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Fail closed on the browser side too: no backend management call is issued for an unauthenticated caller.
        Assert.Empty(backend.ManagementRequests);
    }

    [Fact]
    public async Task AllowsAuthenticatedBrowserRequestWhenStudioAuthEnabled()
    {
        // The backend session endpoint validates the browser bearer; an authenticated session lets the request through.
        var backend = RecordingBackend.RespondingWith(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/identity/session")
                ? JsonOk("""{ "status": "authenticated", "subject": "user-1" }""")
                : JsonOk("""{ "modules": [] }"""));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey, authEnabled: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "a-valid-backend-bearer");

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Available, status.Status);
    }

    [Fact]
    public async Task RejectsBrowserRequestWhenBackendRejectsTheBearer()
    {
        var backend = RecordingBackend.RespondingWith(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/identity/session")
                ? JsonOk("""{ "status": "anonymous" }""")
                : JsonOk("""{ "modules": [] }"""));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey, authEnabled: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "an-expired-bearer");

        var response = await client.GetAsync(StatusRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<StudioBackendManagementStatus> GetStatusAsync(HttpClient client)
    {
        var response = await client.GetAsync(StatusRoute);
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<StudioBackendManagementStatus>();
        Assert.NotNull(status);
        return status!;
    }

    private static HttpResponseMessage JsonOk(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private async Task<HttpClient> StartBridgeHostAsync(
        RecordingBackend backend,
        string? backendBaseUrl,
        string? managementKey,
        bool authEnabled = false)
    {
        var settings = new Dictionary<string, string?>
        {
            [StudioBackendManagementOptions.BackendBaseUrlConfigurationKey] = backendBaseUrl,
            [StudioBackendManagementOptions.ManagementApiKeyConfigurationKey] = managementKey,
            ["Studio:Auth:Enabled"] = authEnabled ? "true" : "false"
        };

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);

        builder.Services.AddStudioBridgeAuth(builder.Configuration);
        builder.Services.AddStudioBackendManagementBridge(builder.Configuration);

        // Route both the typed management client and the named auth client through the recording backend stub so the
        // test asserts real HTTP behaviour (status codes, headers, outbound-call counts) at the wire.
        builder.Services.AddHttpClient(nameof(StudioBackendManagementClient))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        builder.Services.AddHttpClient(StudioBridgeAuthHandler.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => backend);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapStudioBackendManagementBridge();

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
    /// A recording <see cref="HttpMessageHandler"/> standing in for the backend Elsa host. Captures every request
    /// (path + management-key header) so tests can assert both the response mapping and the fail-closed no-call guarantee.
    /// </summary>
    private sealed class RecordingBackend : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _responder;
        private readonly Exception? _throw;

        private RecordingBackend(Func<HttpRequestMessage, HttpResponseMessage>? responder, Exception? toThrow)
        {
            _responder = responder;
            _throw = toThrow;
        }

        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>Recorded requests that carried the management key (i.e. Studio→backend management calls).</summary>
        public IReadOnlyList<RecordedRequest> ManagementRequests => Requests.Where(x => x.ManagementKey is not null).ToArray();

        public static RecordingBackend RespondingWith(Func<HttpRequestMessage, HttpResponseMessage> responder) => new(responder, null);

        public static RecordingBackend Throwing(Exception toThrow) => new(null, toThrow);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues(ModuleManagementAuth.ApiKeyHeaderName, out var keyValues);
            Requests.Add(new(request.RequestUri!.AbsolutePath, keyValues?.FirstOrDefault()));

            if (_throw is not null)
                throw _throw;

            return Task.FromResult(_responder!(request));
        }
    }

    private sealed record RecordedRequest(string Path, string? ManagementKey);
}
