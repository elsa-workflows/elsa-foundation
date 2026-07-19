using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    private const string RegistryRoute = "/_elsa/studio/backend-management/registry";
    private const string CapabilitiesRoute = "/_elsa/studio/backend-management/extension-builder/capabilities";
    private const string BackendCapabilitiesPath = "/_elsa/extension-builder/capabilities";
    private const string BackendBaseUrl = "https://backend.example";
    private const string BackendServerBaseUrl = "http://elsa-server:8080";
    private const string ManagementKey = "s3cr3t-management-key";
    private const string TrustedCapabilitiesJson = """{ "canCreateWorkspace": true, "canEditFiles": true, "canBuild": true, "canPromote": false, "canRollback": false }""";

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
    public async Task UsesDedicatedServerBaseUrlForStudioToBackendCalls()
    {
        var backend = RecordingBackend.RespondingWith(request =>
        {
            Assert.Equal("elsa-server", request.RequestUri!.Host);
            Assert.Equal(8080, request.RequestUri.Port);
            return JsonOk("""{ "host": { "id": "backend" }, "modules": [] }""");
        });
        var client = await StartBridgeHostAsync(
            backend,
            backendBaseUrl: BackendBaseUrl,
            backendServerBaseUrl: BackendServerBaseUrl,
            managementKey: ManagementKey);

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Available, status.Status);
        Assert.Equal(BackendServerBaseUrl, status.BackendBaseUrl);
    }

    [Fact]
    public async Task ReturnsUnconfiguredWithZeroOutboundCallsWhenNoManagementKey()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: null);

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unconfigured, status.Status);
        Assert.Contains(StudioBackendManagementOptions.BackendBaseUrlConfigurationKey, status.Detail);
        Assert.Contains(StudioBackendManagementOptions.ManagementApiKeyConfigurationKey, status.Detail);
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
    public async Task AllowsAuthenticatedBrowserRequestWithModuleManagementReadWhenStudioAuthEnabled()
    {
        // The backend session endpoint validates the browser bearer AND reports its permissions; a session holding
        // module-management.read passes the status/registry read gate (#249).
        var backend = RecordingBackend.RespondingWith(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/identity/session")
                ? JsonOk(AuthenticatedSessionJson(StudioBridgeAuth.ModuleManagementReadPermission))
                : JsonOk("""{ "modules": [] }"""));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey, authEnabled: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "a-valid-backend-bearer");

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Available, status.Status);
    }

    [Fact]
    public async Task ForbidsAuthenticatedBrowserStatusRequestMissingModuleManagementReadWhenStudioAuthEnabled()
    {
        // A live session that lacks module-management.read is FORBIDDEN (403) — a distinct authorization failure, not a
        // 401 and not a backend-status state. No backend management call is issued for a forbidden caller.
        var backend = RecordingBackend.RespondingWith(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/identity/session")
                ? JsonOk(AuthenticatedSessionJson())
                : JsonOk("""{ "modules": [] }"""));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey, authEnabled: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "a-valid-backend-bearer");

        var response = await client.GetAsync(StatusRoute);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(backend.ManagementRequests);
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

    [Fact]
    public async Task ReturnsCapabilitiesWhenBackendAcceptsTheManagementKey()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk(TrustedCapabilitiesJson));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Available, result.Status);
        Assert.NotNull(result.Capabilities);
        Assert.True(result.Capabilities!.CanCreateWorkspace);
        Assert.True(result.Capabilities.CanEditFiles);
        Assert.True(result.Capabilities.CanBuild);
        Assert.False(result.Capabilities.CanPromote);
        Assert.False(result.Capabilities.CanRollback);
        // The management key rides only on the Studio->backend call, against the Extension Builder capabilities path.
        Assert.Single(backend.Requests);
        Assert.Equal(BackendCapabilitiesPath, backend.Requests[0].Path);
        Assert.Equal(ManagementKey, backend.Requests[0].ManagementKey);
    }

    [Fact]
    public async Task ReturnsUnconfiguredCapabilitiesWithZeroOutboundCallsWhenNoManagementKey()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk(TrustedCapabilitiesJson));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: null);

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Unconfigured, result.Status);
        Assert.Null(result.Capabilities);
        // Fail closed: no outbound backend request may be issued.
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task ReturnsUnconfiguredCapabilitiesWithZeroOutboundCallsWhenNoBackendBaseUrl()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk(TrustedCapabilitiesJson));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: null, managementKey: ManagementKey);

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Unconfigured, result.Status);
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task ReturnsUnauthorizedCapabilitiesWhenBackendRejectsTheManagementKey()
    {
        var backend = RecordingBackend.RespondingWith(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: "wrong-key");

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Unauthorized, result.Status);
        Assert.Null(result.Capabilities);
    }

    [Fact]
    public async Task ReturnsUnauthorizedCapabilitiesWhenBackendSurfaceIsDisabled()
    {
        var backend = RecordingBackend.RespondingWith(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Unauthorized, result.Status);
    }

    [Fact]
    public async Task ReturnsUnreachableCapabilitiesWhenBackendTransportFails()
    {
        var backend = RecordingBackend.Throwing(new HttpRequestException("connection refused"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Unreachable, result.Status);
    }

    [Fact]
    public async Task ReturnsDegradedCapabilitiesWhenBackendRespondsWithServerError()
    {
        var backend = RecordingBackend.RespondingWith(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Degraded, result.Status);
    }

    [Fact]
    public async Task RejectsUnauthenticatedBrowserCapabilitiesRequestWhenStudioAuthEnabled()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk(TrustedCapabilitiesJson));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey, authEnabled: true);

        var response = await client.GetAsync(CapabilitiesRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Fail closed on the browser side too: no backend management call is issued for an unauthenticated caller.
        Assert.Empty(backend.ManagementRequests);
    }

    [Fact]
    public async Task AllowsAuthenticatedBrowserCapabilitiesRequestWithExtensionBuilderReadWhenStudioAuthEnabled()
    {
        // The capabilities read is gated by extension-builder.read (not module-management): a holder passes (#249).
        var backend = RecordingBackend.RespondingWith(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/identity/session")
                ? JsonOk(AuthenticatedSessionJson(StudioBridgeAuth.ExtensionBuilderReadPermission))
                : JsonOk(TrustedCapabilitiesJson));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey, authEnabled: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "a-valid-backend-bearer");

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Available, result.Status);
    }

    [Fact]
    public async Task ForbidsAuthenticatedBrowserCapabilitiesRequestMissingExtensionBuilderReadWhenStudioAuthEnabled()
    {
        // module-management.read does NOT satisfy the Extension Builder capabilities gate — the surfaces are gated
        // independently, so a module-only holder is forbidden (403).
        var backend = RecordingBackend.RespondingWith(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/identity/session")
                ? JsonOk(AuthenticatedSessionJson(StudioBridgeAuth.ModuleManagementReadPermission))
                : JsonOk(TrustedCapabilitiesJson));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey, authEnabled: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "a-valid-backend-bearer");

        var response = await client.GetAsync(CapabilitiesRoute);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(backend.ManagementRequests);
    }

    // ---- Registry read (#246) ----

    [Fact]
    public async Task RegistryReturnsPayloadWhenBackendAcceptsTheManagementKey()
    {
        const string registryJson = """{ "host": { "id": "backend" }, "modules": [ { "id": "m1" } ] }""";
        var backend = RecordingBackend.RespondingWith(_ => JsonOk(registryJson));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        var envelope = await GetRegistryAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Available, envelope.Status);
        Assert.NotNull(envelope.Registry);
        // The backend registry payload is passed through under the Studio-owned envelope.
        Assert.Equal("backend", envelope.Registry!.Value.GetProperty("host").GetProperty("id").GetString());
        Assert.Equal("m1", envelope.Registry!.Value.GetProperty("modules")[0].GetProperty("id").GetString());
        // The management key rode only on the single Studio->backend call.
        Assert.Single(backend.Requests);
        Assert.Equal(ManagementKey, backend.Requests[0].ManagementKey);
        // It hit the backend host-control registry path, not the Studio-owned browser route.
        Assert.Equal("/_elsa/module-management/registry", backend.Requests[0].Path);
    }

    [Fact]
    public async Task RegistryReturnsUnconfiguredWithZeroOutboundCallsWhenNoManagementKey()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: null);

        var envelope = await GetRegistryAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unconfigured, envelope.Status);
        Assert.Null(envelope.Registry);
        // Fail closed: no outbound backend request may be issued.
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task RegistryReturnsUnconfiguredWithZeroOutboundCallsWhenNoBackendBaseUrl()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: null, managementKey: ManagementKey);

        var envelope = await GetRegistryAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unconfigured, envelope.Status);
        Assert.Null(envelope.Registry);
        Assert.Empty(backend.Requests);
    }

    [Fact]
    public async Task RegistryReturnsUnauthorizedWithNullPayloadWhenBackendRejectsTheManagementKey()
    {
        var backend = RecordingBackend.RespondingWith(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: "wrong-key");

        var envelope = await GetRegistryAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unauthorized, envelope.Status);
        Assert.Null(envelope.Registry);
    }

    [Fact]
    public async Task RegistryReturnsUnauthorizedWhenBackendSurfaceIsDisabled()
    {
        // The backend hides its management surface (404) when it has no key configured; from Studio's side that maps to
        // unauthorized — the operator must reconcile the key on one side.
        var backend = RecordingBackend.RespondingWith(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        var envelope = await GetRegistryAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unauthorized, envelope.Status);
        Assert.Null(envelope.Registry);
    }

    [Fact]
    public async Task RegistryReturnsUnreachableWhenBackendTransportFails()
    {
        var backend = RecordingBackend.Throwing(new HttpRequestException("connection refused"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        var envelope = await GetRegistryAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unreachable, envelope.Status);
        Assert.Null(envelope.Registry);
    }

    [Fact]
    public async Task RegistryReturnsDegradedWhenBackendRespondsWithServerError()
    {
        var backend = RecordingBackend.RespondingWith(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        var envelope = await GetRegistryAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Degraded, envelope.Status);
        Assert.Null(envelope.Registry);
    }

    [Fact]
    public async Task RegistryNeverEchoesTheManagementKeyOrRequiresItFromTheBrowser()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("""{ "modules": [] }"""));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey);

        // The browser sends no management key; the bridge still answers (auth disabled) and never leaks the key.
        var response = await client.GetAsync(RegistryRoute);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(ManagementKey, body);
    }

    [Fact]
    public async Task RegistryRejectsUnauthenticatedBrowserRequestWhenStudioAuthEnabled()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey, authEnabled: true);

        var response = await client.GetAsync(RegistryRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Fail closed on the browser side too: no backend management call is issued for an unauthenticated caller.
        Assert.Empty(backend.ManagementRequests);
    }

    private static async Task<StudioBackendManagementStatus> GetStatusAsync(HttpClient client)
    {
        var response = await client.GetAsync(StatusRoute);
        response.EnsureSuccessStatusCode();
        var status = await response.Content.ReadFromJsonAsync<StudioBackendManagementStatus>();
        Assert.NotNull(status);
        return status!;
    }

    private static async Task<StudioExtensionBuilderCapabilitiesResult> GetCapabilitiesAsync(HttpClient client)
    {
        var response = await client.GetAsync(CapabilitiesRoute);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<StudioExtensionBuilderCapabilitiesResult>();
        Assert.NotNull(result);
        return result!;
    }

    private static async Task<StudioBackendManagementRegistryEnvelope> GetRegistryAsync(HttpClient client)
    {
        var response = await client.GetAsync(RegistryRoute);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<StudioBackendManagementRegistryEnvelope>();
        Assert.NotNull(envelope);
        return envelope!;
    }

    private static HttpResponseMessage JsonOk(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    // Builds the backend session-endpoint response for an authenticated user carrying the given host-control
    // permissions (the flat camelCase `permissions` array the gate projects onto the ticket). No permissions models a
    // signed-in user with only a session and no host-control grants.
    private static string AuthenticatedSessionJson(params string[] permissions) =>
        JsonSerializer.Serialize(new { status = "authenticated", subject = "user-1", permissions });

    private async Task<HttpClient> StartBridgeHostAsync(
        RecordingBackend backend,
        string? backendBaseUrl,
        string? managementKey,
        string? backendServerBaseUrl = null,
        bool authEnabled = false)
    {
        var settings = new Dictionary<string, string?>
        {
            [StudioBackendManagementOptions.BackendBaseUrlConfigurationKey] = backendBaseUrl,
            [StudioBackendManagementOptions.BackendServerBaseUrlConfigurationKey] = backendServerBaseUrl,
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
            request.Headers.TryGetValues(StudioBackendManagementOptions.ManagementApiKeyHeaderName, out var keyValues);
            Requests.Add(new(request.RequestUri!.AbsolutePath, keyValues?.FirstOrDefault()));

            if (_throw is not null)
                throw _throw;

            return Task.FromResult(_responder!(request));
        }
    }

    private sealed record RecordedRequest(string Path, string? ManagementKey);
}
