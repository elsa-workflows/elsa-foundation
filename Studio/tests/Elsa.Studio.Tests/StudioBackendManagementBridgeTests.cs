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
    private const string ModuleListJson = """{ "modules": [] }""";
    private const string TrustedCapabilitiesJson = """{ "canCreateWorkspace": true, "canEditFiles": true, "canBuild": true, "canPromote": false, "canRollback": false }""";

    private WebApplication? _app;

    // ---- Status read ----

    [Fact]
    public async Task ReturnsAvailableWhenBackendAcceptsTheManagementKey()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("""{ "host": { "id": "backend" }, "modules": [] }"""));
        var client = await StartConfiguredHostAsync(backend);

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

    [Theory]
    [InlineData(BackendBaseUrl, null)] // no management key
    [InlineData(null, ManagementKey)]  // no backend base URL
    public async Task ReturnsUnconfiguredWithZeroOutboundCallsWhenConfigIncomplete(string? backendBaseUrl, string? managementKey)
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: backendBaseUrl, managementKey: managementKey);

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unconfigured, status.Status);
        // The detail enumerates the required configuration keys so the operator can reconcile what is missing.
        Assert.Contains(StudioBackendManagementOptions.BackendBaseUrlConfigurationKey, status.Detail);
        Assert.Contains(StudioBackendManagementOptions.ManagementApiKeyConfigurationKey, status.Detail);
        // Fail closed: no outbound backend request may be issued.
        Assert.Empty(backend.Requests);
    }

    // A rejected key (401) and a hidden surface (404) both map to Unauthorized — the operator must reconcile the key on
    // one side; a server error is Degraded and a transport failure (null) is Unreachable.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, StudioBackendManagementStatus.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, StudioBackendManagementStatus.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError, StudioBackendManagementStatus.Degraded)]
    [InlineData(null, StudioBackendManagementStatus.Unreachable)]
    public async Task StatusMapsBackendOutcome(HttpStatusCode? backendStatus, string expected)
    {
        var client = await StartConfiguredHostAsync(BackendFor(backendStatus));

        var status = await GetStatusAsync(client);

        Assert.Equal(expected, status.Status);
    }

    [Fact]
    public async Task RejectsBrowserRequestWhenBackendRejectsTheBearer()
    {
        var backend = BackendWithSession("""{ "status": "anonymous" }""", ModuleListJson);
        var client = await StartConfiguredHostAsync(backend, authEnabled: true, bearer: "an-expired-bearer");

        var response = await client.GetAsync(StatusRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AllowsAuthenticatedBrowserRequestWithModuleManagementReadWhenStudioAuthEnabled()
    {
        // The backend session endpoint validates the browser bearer AND reports its permissions; a session holding
        // module-management.read passes the status/registry read gate (#249).
        var backend = AuthenticatedBackend(ModuleListJson, StudioBridgeAuth.ModuleManagementReadPermission);
        var client = await StartConfiguredHostAsync(backend, authEnabled: true, bearer: "a-valid-backend-bearer");

        var status = await GetStatusAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Available, status.Status);
    }

    [Fact]
    public async Task ForbidsAuthenticatedBrowserStatusRequestMissingModuleManagementReadWhenStudioAuthEnabled()
    {
        // A live session that lacks module-management.read is FORBIDDEN (403) — a distinct authorization failure, not a
        // 401 and not a backend-status state. No backend management call is issued for a forbidden caller.
        var backend = AuthenticatedBackend(ModuleListJson);
        var client = await StartConfiguredHostAsync(backend, authEnabled: true, bearer: "a-valid-backend-bearer");

        var response = await client.GetAsync(StatusRoute);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(backend.ManagementRequests);
    }

    // ---- Extension Builder capabilities read ----

    [Fact]
    public async Task ReturnsCapabilitiesWhenBackendAcceptsTheManagementKey()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk(TrustedCapabilitiesJson));
        var client = await StartConfiguredHostAsync(backend);

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

    [Theory]
    [InlineData(BackendBaseUrl, null)] // no management key
    [InlineData(null, ManagementKey)]  // no backend base URL
    public async Task ReturnsUnconfiguredCapabilitiesWithZeroOutboundCallsWhenConfigIncomplete(string? backendBaseUrl, string? managementKey)
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk(TrustedCapabilitiesJson));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: backendBaseUrl, managementKey: managementKey);

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Unconfigured, result.Status);
        Assert.Null(result.Capabilities);
        // Fail closed: no outbound backend request may be issued.
        Assert.Empty(backend.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, StudioExtensionBuilderCapabilitiesResult.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, StudioExtensionBuilderCapabilitiesResult.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError, StudioExtensionBuilderCapabilitiesResult.Degraded)]
    [InlineData(null, StudioExtensionBuilderCapabilitiesResult.Unreachable)]
    public async Task CapabilitiesMapsBackendOutcome(HttpStatusCode? backendStatus, string expected)
    {
        var client = await StartConfiguredHostAsync(BackendFor(backendStatus));

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Capabilities);
    }

    [Fact]
    public async Task AllowsAuthenticatedBrowserCapabilitiesRequestWithExtensionBuilderReadWhenStudioAuthEnabled()
    {
        // The capabilities read is gated by extension-builder.read (not module-management): a holder passes (#249).
        var backend = AuthenticatedBackend(TrustedCapabilitiesJson, StudioBridgeAuth.ExtensionBuilderReadPermission);
        var client = await StartConfiguredHostAsync(backend, authEnabled: true, bearer: "a-valid-backend-bearer");

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Available, result.Status);
    }

    [Fact]
    public async Task ForbidsAuthenticatedBrowserCapabilitiesRequestMissingExtensionBuilderReadWhenStudioAuthEnabled()
    {
        // module-management.read does NOT satisfy the Extension Builder capabilities gate — the surfaces are gated
        // independently, so a module-only holder is forbidden (403).
        var backend = AuthenticatedBackend(TrustedCapabilitiesJson, StudioBridgeAuth.ModuleManagementReadPermission);
        var client = await StartConfiguredHostAsync(backend, authEnabled: true, bearer: "a-valid-backend-bearer");

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
        var client = await StartConfiguredHostAsync(backend);

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

    [Theory]
    [InlineData(BackendBaseUrl, null)] // no management key
    [InlineData(null, ManagementKey)]  // no backend base URL
    public async Task RegistryReturnsUnconfiguredWithZeroOutboundCallsWhenConfigIncomplete(string? backendBaseUrl, string? managementKey)
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: backendBaseUrl, managementKey: managementKey);

        var envelope = await GetRegistryAsync(client);

        Assert.Equal(StudioBackendManagementStatus.Unconfigured, envelope.Status);
        Assert.Null(envelope.Registry);
        // Fail closed: no outbound backend request may be issued.
        Assert.Empty(backend.Requests);
    }

    // A rejected key (401) and a hidden surface (404) both map to Unauthorized; a server error is Degraded and a
    // transport failure (null) is Unreachable. The registry payload is always null on a non-Available outcome.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, StudioBackendManagementStatus.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, StudioBackendManagementStatus.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError, StudioBackendManagementStatus.Degraded)]
    [InlineData(null, StudioBackendManagementStatus.Unreachable)]
    public async Task RegistryMapsBackendOutcome(HttpStatusCode? backendStatus, string expected)
    {
        var client = await StartConfiguredHostAsync(BackendFor(backendStatus));

        var envelope = await GetRegistryAsync(client);

        Assert.Equal(expected, envelope.Status);
        Assert.Null(envelope.Registry);
    }

    // ---- Cross-surface browser-side guarantees ----

    [Theory]
    [InlineData(StatusRoute)]
    [InlineData(RegistryRoute)]
    public async Task NeverEchoesTheManagementKeyOrRequiresItFromTheBrowser(string route)
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk(ModuleListJson));
        var client = await StartConfiguredHostAsync(backend);

        // The browser sends no management key; the bridge still answers (auth disabled) and never leaks the key.
        var response = await client.GetAsync(route);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(ManagementKey, body);
    }

    [Theory]
    [InlineData(StatusRoute)]
    [InlineData(CapabilitiesRoute)]
    [InlineData(RegistryRoute)]
    public async Task RejectsUnauthenticatedBrowserRequestWhenStudioAuthEnabled(string route)
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartConfiguredHostAsync(backend, authEnabled: true);

        var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Fail closed on the browser side too: no backend management call is issued for an unauthenticated caller.
        Assert.Empty(backend.ManagementRequests);
    }

    // ---- Helpers ----

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

    // A backend that maps a Studio->backend management call to a fixed outcome: a bare status code, or (when null) a
    // transport failure.
    private static RecordingBackend BackendFor(HttpStatusCode? backendStatus) =>
        backendStatus is null
            ? RecordingBackend.Throwing(new HttpRequestException("connection refused"))
            : RecordingBackend.RespondingWith(_ => new HttpResponseMessage(backendStatus.Value));

    // A backend that answers the identity/session probe with the given session JSON and every other (management) call
    // with the given payload.
    private static RecordingBackend BackendWithSession(string sessionJson, string managementPayload) =>
        RecordingBackend.RespondingWith(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/identity/session")
                ? JsonOk(sessionJson)
                : JsonOk(managementPayload));

    private static RecordingBackend AuthenticatedBackend(string managementPayload, params string[] permissions) =>
        BackendWithSession(AuthenticatedSessionJson(permissions), managementPayload);

    // Builds the backend session-endpoint response for an authenticated user carrying the given host-control
    // permissions (the flat camelCase `permissions` array the gate projects onto the ticket). No permissions models a
    // signed-in user with only a session and no host-control grants.
    private static string AuthenticatedSessionJson(params string[] permissions) =>
        JsonSerializer.Serialize(new { status = "authenticated", subject = "user-1", permissions });

    // Fully-configured host (backend base URL + management key), the common arrangement; optionally with browser auth
    // enabled and a bearer preset on the client.
    private async Task<HttpClient> StartConfiguredHostAsync(RecordingBackend backend, bool authEnabled = false, string? bearer = null)
    {
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: BackendBaseUrl, managementKey: ManagementKey, authEnabled: authEnabled);
        if (bearer is not null)
            client.DefaultRequestHeaders.Authorization = new("Bearer", bearer);
        return client;
    }

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
