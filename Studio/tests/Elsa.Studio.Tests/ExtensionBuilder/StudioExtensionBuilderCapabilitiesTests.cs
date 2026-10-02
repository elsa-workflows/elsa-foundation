using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Studio.ExtensionBuilder;
using Elsa.Studio.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using static Elsa.Studio.Tests.RecordingBackend;

namespace Elsa.Studio.Tests.ExtensionBuilder;

/// <summary>
/// Exercises the Extension Builder capabilities read of the Studio management bridge (#247, ADR 0037) at its HTTP
/// boundary: the same TestServer wiring the <see cref="ExtensionBuilderStudioFeature"/> maps, with a recording stub
/// standing in for the backend host so the status mapping, the <c>extension-builder.read</c> gate, and the "zero
/// outbound calls when unconfigured" fail-closed guarantee are asserted against the wire.
/// </summary>
public sealed class StudioExtensionBuilderCapabilitiesTests : IAsyncDisposable
{
    private const string CapabilitiesRoute = StudioExtensionBuilderBridge.RouteGroup + StudioExtensionBuilderBridge.CapabilitiesRoute;
    private const string BackendCapabilitiesPath = StudioExtensionBuilderBridge.BackendRoot + "/capabilities";
    private const string TrustedCapabilitiesJson = """{ "canCreateWorkspace": true, "canEditFiles": true, "canBuild": true, "canPromote": false, "canRollback": false }""";
    private const string ValidBearer = "a-valid-backend-bearer";

    private WebApplication? _app;

    [Fact]
    public async Task ReturnsCapabilitiesWhenBackendAcceptsTheManagementKey()
    {
        var backend = RespondingWith(_ => JsonOk(TrustedCapabilitiesJson));
        var client = await StartHostAsync(backend);

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Available, result.Status);
        Assert.NotNull(result.Capabilities);
        Assert.True(result.Capabilities!.CanCreateWorkspace);
        Assert.True(result.Capabilities.CanEditFiles);
        Assert.True(result.Capabilities.CanBuild);
        Assert.False(result.Capabilities.CanPromote);
        Assert.False(result.Capabilities.CanRollback);
        // The management key rides only on the Studio->backend call, against the Extension Builder capabilities path.
        var recorded = Assert.Single(backend.Requests);
        Assert.Equal(BackendCapabilitiesPath, recorded.PathAndQuery);
        Assert.Equal(BridgeTestHost.ManagementKey, recorded.ManagementKey);
    }

    [Theory]
    [InlineData(BridgeTestHost.BackendBaseUrl, null)] // no management key
    [InlineData(null, BridgeTestHost.ManagementKey)]  // no backend base URL
    public async Task ReturnsUnconfiguredCapabilitiesWithZeroOutboundCallsWhenConfigIncomplete(string? backendBaseUrl, string? managementKey)
    {
        var backend = RespondingWith(_ => JsonOk(TrustedCapabilitiesJson));
        var client = await StartHostAsync(backend, backendBaseUrl: backendBaseUrl, managementKey: managementKey);

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Unconfigured, result.Status);
        Assert.Null(result.Capabilities);
        // Fail closed: no outbound backend request may be issued.
        Assert.Empty(backend.Requests);
    }

    // A rejected key (401) and a hidden surface (404 — also what a backend WITHOUT Extension Builder answers) both map
    // to Unauthorized; a server error is Degraded and a transport failure (null) is Unreachable. Capabilities are null
    // on every non-Available outcome, so the page renders its explicit unavailable state instead of failing noisily.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, StudioExtensionBuilderCapabilitiesResult.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound, StudioExtensionBuilderCapabilitiesResult.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError, StudioExtensionBuilderCapabilitiesResult.Degraded)]
    [InlineData(null, StudioExtensionBuilderCapabilitiesResult.Unreachable)]
    public async Task CapabilitiesMapsBackendOutcome(HttpStatusCode? backendStatus, string expected)
    {
        var client = await StartHostAsync(RecordingBackend.For(backendStatus));

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Capabilities);
    }

    [Fact]
    public async Task MapsUnrecognizedCapabilitiesPayloadToDegraded()
    {
        // A JSON-object 200 whose members don't bind to the capability flags is degraded, not available.
        var client = await StartHostAsync(RespondingWith(_ => JsonOk("""{ "canCreateWorkspace": "not-a-flag" }""")));

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Degraded, result.Status);
        Assert.Null(result.Capabilities);
    }

    [Fact]
    public async Task AllowsAuthenticatedBrowserCapabilitiesRequestWithExtensionBuilderReadWhenStudioAuthEnabled()
    {
        // The capabilities read is gated by extension-builder.read (not module-management): a holder passes (#249).
        var (client, _) = await StartAuthenticatedHostWithBackendAsync(ExtensionBuilderPermissions.Read);

        var result = await GetCapabilitiesAsync(client);

        Assert.Equal(StudioExtensionBuilderCapabilitiesResult.Available, result.Status);
    }

    [Fact]
    public async Task ForbidsAuthenticatedBrowserCapabilitiesRequestMissingExtensionBuilderReadWhenStudioAuthEnabled()
    {
        // module-management.read does NOT satisfy the Extension Builder capabilities gate — the surfaces are gated
        // independently, so a module-only holder is forbidden (403).
        var (client, backend) = await StartAuthenticatedHostWithBackendAsync(StudioBridgeAuth.ModuleManagementReadPermission);

        var response = await client.GetAsync(CapabilitiesRoute);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(backend.ManagementRequests);
    }

    [Fact]
    public async Task RejectsUnauthenticatedBrowserRequestWhenStudioAuthEnabled()
    {
        var backend = RespondingWith(_ => JsonOk(TrustedCapabilitiesJson));
        var client = await StartHostAsync(backend, authEnabled: true);

        var response = await client.GetAsync(CapabilitiesRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Fail closed on the browser side too: no backend management call is issued for an unauthenticated caller.
        Assert.Empty(backend.ManagementRequests);
    }

    [Fact]
    public async Task NeverEchoesTheManagementKeyOrRequiresItFromTheBrowser()
    {
        var client = await StartHostAsync(RespondingWith(_ => JsonOk(TrustedCapabilitiesJson)));

        // The browser sends no management key; the bridge still answers (auth disabled) and never leaks the key.
        var response = await client.GetAsync(CapabilitiesRoute);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(BridgeTestHost.ManagementKey, body);
    }

    private static async Task<StudioExtensionBuilderCapabilitiesResult> GetCapabilitiesAsync(HttpClient client)
    {
        var response = await client.GetAsync(CapabilitiesRoute);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<StudioExtensionBuilderCapabilitiesResult>();
        Assert.NotNull(result);
        return result!;
    }

    // An auth-enabled host whose stub backend recognizes ValidBearer with the given host-control permissions and answers
    // the capabilities read with the trusted flags; the client carries that bearer.
    private async Task<(HttpClient Client, RecordingBackend Backend)> StartAuthenticatedHostWithBackendAsync(params string[] permissions)
    {
        var bearers = new Dictionary<string, string[]> { [ValidBearer] = permissions };
        var backend = RespondingWith(WithSessionEndpoint(bearers, _ => JsonOk(TrustedCapabilitiesJson)));
        var client = await StartHostAsync(backend, authEnabled: true);
        client.DefaultRequestHeaders.Authorization = new("Bearer", ValidBearer);
        return (client, backend);
    }

    private async Task<HttpClient> StartHostAsync(
        RecordingBackend backend,
        string? backendBaseUrl = BridgeTestHost.BackendBaseUrl,
        string? managementKey = BridgeTestHost.ManagementKey,
        bool authEnabled = false)
    {
        _app = await ExtensionBuilderBridgeHost.StartAsync(backend, backendBaseUrl, managementKey, authEnabled);
        return _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.DisposeAsync();
    }
}
