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
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Studio.Tests;

/// <summary>
/// Exercises the Extension Builder relay of the Studio management bridge (#256, ADR 0037) at its HTTP boundary: a
/// TestServer hosts the real table-driven relay wiring, and a recording stub stands in for the backend host so
/// forwarding fidelity, the two-plane status mapping, the permission gates, and the fail-closed zero-outbound
/// guarantees are asserted against the wire, not mocked internals. The theories parameterize over the operation table
/// itself, so a new table row is covered the moment it is added.
/// </summary>
public sealed class StudioExtensionBuilderBridgeTests : IAsyncDisposable
{
    private const string BackendBaseUrl = "https://backend.example";
    private const string ManagementKey = "s3cr3t-management-key";
    private const string BrowserBearer = "browser-bearer-never-for-the-backend";

    // Bearers the stub backend session endpoint recognizes, mapped to the permission set it reports for each. These
    // exercise the Extension Builder read/manage policies plus the independence from module-management permissions.
    private const string ExtensionBuilderReadBearer = "user-extension-builder-read";
    private const string ExtensionBuilderManageBearer = "user-extension-builder-manage";
    private const string ModuleReadBearer = "user-module-read";

    private static readonly IReadOnlyDictionary<string, string[]> BearerPermissions = new Dictionary<string, string[]>
    {
        [ExtensionBuilderReadBearer] = [StudioBridgeAuth.ExtensionBuilderReadPermission],
        [ExtensionBuilderManageBearer] = [StudioBridgeAuth.ExtensionBuilderManagePermission],
        [ModuleReadBearer] = [StudioBridgeAuth.ModuleManagementReadPermission]
    };

    private WebApplication? _app;

    public static TheoryData<string> AllOperationNames() => OperationNames(_ => true);
    public static TheoryData<string> ReadOperationNames() => OperationNames(op => op.Access == StudioExtensionBuilderBridge.BridgeAccess.Read);
    public static TheoryData<string> MutationOperationNames() => OperationNames(op => op.Access == StudioExtensionBuilderBridge.BridgeAccess.Manage);

    [Fact]
    public void OperationTableHasUniqueOperationNames()
    {
        Assert.Equal(StudioExtensionBuilderBridge.Operations.Count, StudioExtensionBuilderBridge.Operations.Select(op => op.Name).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(AllOperationNames))]
    public async Task ForwardsOperationWithManagementKeyAndWithoutBrowserAuthorization(string operationName)
    {
        var op = Op(operationName);
        var backend = RecordingBackend.RespondingWith(_ => BackendSuccessFor(op));
        var client = await StartBridgeHostAsync(backend);
        client.DefaultRequestHeaders.Authorization = new("Bearer", BrowserBearer);

        var response = await SendAsync(client, op);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode);
        // Exactly one backend call, to the backend root + the same suffix (route params AND query string) the browser
        // sent, carrying the management key — and never the browser's Authorization header. Bodied operations relay
        // the browser's request body and Content-Type byte-identical.
        var recorded = Assert.Single(backend.Requests);
        Assert.Equal(op.Method, recorded.Method);
        Assert.Equal("/_elsa/extension-builder" + SampleSuffix(op), recorded.PathAndQuery);
        Assert.Equal(ManagementKey, recorded.ManagementKey);
        Assert.False(recorded.HasAuthorization);
        AssertRequestBodyRelayed(op, recorded);
        Assert.DoesNotContain(ManagementKey, body);
    }

    [Theory]
    [MemberData(nameof(AllOperationNames))]
    public async Task ReturnsUnconfiguredWithZeroOutboundCallsWhenNoManagementKey(string operationName)
    {
        var op = Op(operationName);
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend, managementKey: null);

        var response = await SendAsync(client, op);
        var body = await response.Content.ReadAsStringAsync();
        var error = ReadError(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(StudioBackendManagementStatus.Unconfigured, error.Management?.Status);
        // Fail closed: no outbound backend request may be issued, and the (absent) key never appears in the body.
        Assert.Empty(backend.Requests);
        Assert.DoesNotContain(ManagementKey, body);
    }

    [Fact]
    public async Task ReturnsUnconfiguredWithZeroOutboundCallsWhenNoBackendBaseUrl()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend, backendBaseUrl: null);

        var response = await SendAsync(client, Op("list-workspaces"));
        var error = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(StudioBackendManagementStatus.Unconfigured, error.Management?.Status);
        Assert.Empty(backend.Requests);
    }

    [Theory]
    [MemberData(nameof(AllOperationNames))]
    public async Task RejectsUnauthenticatedBrowserRequestWhenStudioAuthEnabled(string operationName)
    {
        var op = Op(operationName);
        var backend = RecordingBackend.RespondingWith(WithSessionEndpoint(_ => BackendSuccessFor(op)));
        var client = await StartBridgeHostAsync(backend, authEnabled: true);

        var response = await SendAsync(client, op);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // Fail closed on the browser side too: no backend management call is issued for an unauthenticated caller.
        Assert.Empty(backend.ManagementRequests);
    }

    [Theory]
    [MemberData(nameof(ReadOperationNames))]
    public async Task AllowsExtensionBuilderReadHolderOnReadOperationWhenStudioAuthEnabled(string operationName)
    {
        var op = Op(operationName);
        var (response, backend) = await SendWithBearerAsync(op, ExtensionBuilderReadBearer);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Single(backend.ManagementRequests);
    }

    [Theory]
    [MemberData(nameof(ReadOperationNames))]
    public async Task AllowsExtensionBuilderManageHolderOnReadOperationWhenStudioAuthEnabled(string operationName)
    {
        // manage implies read: a manage-only holder satisfies the read relays (expanded locally by the read policy).
        var op = Op(operationName);
        var (response, backend) = await SendWithBearerAsync(op, ExtensionBuilderManageBearer);

        Assert.True(response.IsSuccessStatusCode);
        Assert.Single(backend.ManagementRequests);
    }

    [Theory]
    [MemberData(nameof(ReadOperationNames))]
    public async Task ForbidsModuleManagementHolderOnReadOperationWhenStudioAuthEnabled(string operationName)
    {
        // module-management permissions must NOT satisfy the Extension Builder relays — the surfaces are gated
        // independently, so a module-only holder is forbidden (403) with zero outbound management calls.
        var op = Op(operationName);
        var (response, backend) = await SendWithBearerAsync(op, ModuleReadBearer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(backend.ManagementRequests);
    }

    [Theory]
    [MemberData(nameof(MutationOperationNames))]
    public async Task ForwardsMutationWithManageHolderWhenStudioAuthEnabled(string operationName)
    {
        // A manage holder's mutation forwards exactly once with full fidelity: method, path suffix, management key,
        // no browser Authorization, and (for bodied operations) the request body + Content-Type byte-identical.
        var op = Op(operationName);
        var (response, backend) = await SendWithBearerAsync(op, ExtensionBuilderManageBearer);

        Assert.True(response.IsSuccessStatusCode);
        var recorded = Assert.Single(backend.ManagementRequests);
        Assert.Equal(op.Method, recorded.Method);
        Assert.Equal("/_elsa/extension-builder" + SampleSuffix(op), recorded.PathAndQuery);
        Assert.Equal(ManagementKey, recorded.ManagementKey);
        Assert.False(recorded.HasAuthorization);
        AssertRequestBodyRelayed(op, recorded);
    }

    [Theory]
    [MemberData(nameof(MutationOperationNames))]
    public async Task ForbidsReadOnlyHolderOnMutationWhenStudioAuthEnabled(string operationName)
    {
        var op = Op(operationName);
        var (response, backend) = await SendWithBearerAsync(op, ExtensionBuilderReadBearer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(backend.ManagementRequests);
    }

    // ---- Status mapping (two planes), on representative read and mutation operations --------------------------------

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task MapsBackendKeyRejectionToUnauthorized503(HttpStatusCode backendStatus)
    {
        var backend = RecordingBackend.RespondingWith(_ => new HttpResponseMessage(backendStatus));
        var response = await SendThroughBridgeAsync(backend, "list-workspaces");
        var error = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(StudioBackendManagementStatus.Unauthorized, error.Management?.Status);
    }

    [Fact]
    public async Task MapsBareBackend404ToUnauthorized503()
    {
        // The backend hides its management surface with a bare (non-JSON) 404 when it has no key configured; from
        // Studio's side that maps to unauthorized — the operator must reconcile the key on one side.
        var backend = RecordingBackend.RespondingWith(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var response = await SendThroughBridgeAsync(backend, "list-workspaces");
        var error = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(StudioBackendManagementStatus.Unauthorized, error.Management?.Status);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{ "error": "invalid-request" }""")]
    [InlineData(HttpStatusCode.NotFound, """{ "error": "workspace-not-found" }""")]
    [InlineData(HttpStatusCode.Conflict, """{ "error": "workspace-busy" }""")]
    public async Task RelaysBackendDomainErrorWithJsonBodyVerbatim(HttpStatusCode backendStatus, string domainErrorJson)
    {
        // Plane B: 400/404/409 WITH JSON bodies are Extension Builder domain answers the SPA must see verbatim.
        var backend = RecordingBackend.RespondingWith(_ => Json(backendStatus, domainErrorJson));
        var response = await SendThroughBridgeAsync(backend, "list-workspaces");

        Assert.Equal(backendStatus, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType!.ToString());
        Assert.Equal(domainErrorJson, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MapsBackendServerErrorToDegraded503()
    {
        var backend = RecordingBackend.RespondingWith(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var response = await SendThroughBridgeAsync(backend, "list-workspaces");
        var error = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(StudioBackendManagementStatus.Degraded, error.Management?.Status);
    }

    [Fact]
    public async Task MapsNonJsonSuccessOnJsonOperationToDegraded503()
    {
        // A 2xx that isn't JSON on a JSON operation means the relay hit something other than the surface (e.g. an SPA
        // fallback page) — the backend body is never echoed.
        var backend = RecordingBackend.RespondingWith(_ => TextOk("<html>not the surface</html>"));
        var response = await SendThroughBridgeAsync(backend, "list-workspaces");
        var error = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(StudioBackendManagementStatus.Degraded, error.Management?.Status);
        Assert.DoesNotContain("not the surface", error.Detail);
    }

    [Fact]
    public async Task RelaysCommitConflictWithJsonBodyVerbatim()
    {
        // Plane B on a mutation: a commit the backend refuses (e.g. nothing staged) is a domain 409 the SPA must see
        // verbatim — status code, Content-Type, and body untouched.
        const string conflict = """{ "error": "nothing-to-commit" }""";
        var backend = RecordingBackend.RespondingWith(_ => Json(HttpStatusCode.Conflict, conflict));

        var response = await SendThroughBridgeAsync(backend, "commit");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType!.ToString());
        Assert.Equal(conflict, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RelaysRejectedPromotionOutcomeVerbatim()
    {
        // A promotion the backend evaluates and rejects is still a 200 domain answer with an outcome body — relayed
        // untouched so the SPA (not Studio) interprets the rejection.
        const string rejected = """{ "status": "rejected", "reason": "artifact failed validation" }""";
        var backend = RecordingBackend.RespondingWith(_ => JsonOk(rejected));

        var response = await SendThroughBridgeAsync(backend, "promote-build");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType!.ToString());
        Assert.Equal(rejected, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MapsMutationBackendServerErrorToDegraded503WithoutEchoingBackendBody()
    {
        // A backend 500 on a mutation is plane A: Studio answers 503 degraded with its OWN body — the backend's error
        // body (potentially a stack trace) is never echoed to the browser.
        const string backendSecret = "backend-stack-trace-secret";
        var backend = RecordingBackend.RespondingWith(_ => Json(HttpStatusCode.InternalServerError, $$"""{ "error": "{{backendSecret}}" }"""));

        var response = await SendThroughBridgeAsync(backend, "rollback-project");
        var body = await response.Content.ReadAsStringAsync();
        var error = ReadError(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(StudioBackendManagementStatus.Degraded, error.Management?.Status);
        Assert.DoesNotContain(backendSecret, body);
    }

    [Fact]
    public async Task MapsTransportFailureToUnreachable503()
    {
        var backend = RecordingBackend.Throwing(new HttpRequestException("connection refused"));
        var response = await SendThroughBridgeAsync(backend, "list-workspaces");
        var error = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(StudioBackendManagementStatus.Unreachable, error.Management?.Status);
    }

    [Fact]
    public async Task MapsExpiredOperationBudgetToUnreachable504()
    {
        var op = Op("list-workspaces");
        var timeProvider = new FakeTimeProvider();
        var backend = RecordingBackend.RespondingWithAsync(async (_, cancellationToken) =>
        {
            // Hang until the relay's budget cancels the outbound call.
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        });
        var client = await StartBridgeHostAsync(backend, timeProvider: timeProvider);

        var pending = SendAsync(client, op);
        // The budget timer exists once the backend has received the outbound call; only then can advancing the fake
        // clock expire it. Bounded so a relay regression fails the test instead of hanging the runner.
        var waitDeadline = DateTime.UtcNow.AddSeconds(10);
        while (backend.Requests.Count == 0 && DateTime.UtcNow < waitDeadline)
            await Task.Delay(10);
        Assert.NotEmpty(backend.Requests);
        timeProvider.Advance(TimeSpan.FromSeconds(op.TimeoutSeconds + 1));
        var response = await pending;
        var error = await ReadErrorAsync(response);

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal(StudioBackendManagementStatus.Unreachable, error.Management?.Status);
        // The mutation may still have completed backend-side; the detail must say so.
        Assert.Contains("may still have completed", error.Detail);
    }

    // ---- Relay fidelity ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task PreservesEncodedPathSegmentsOnTheBackendCall()
    {
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend);

        var response = await client.GetAsync(StudioExtensionBuilderBridge.RouteGroup + "/workspaces/ws-1/files/Activities/My%20File%23x.cs");

        Assert.True(response.IsSuccessStatusCode);
        var recorded = Assert.Single(backend.Requests);
        Assert.Equal("/_elsa/extension-builder/workspaces/ws-1/files/Activities/My%20File%23x.cs", recorded.PathAndQuery);
    }

    [Fact]
    public async Task StripsTheHostPathBaseFromTheBackendCall()
    {
        // GetEncodedPathAndQuery prepends the PathBase a Studio host is mounted under; the relay must still forward
        // only the operation suffix to the backend root.
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend, pathBase: "/mounted");

        var response = await client.GetAsync("/mounted" + StudioExtensionBuilderBridge.RouteGroup + "/workspaces/ws-1");

        Assert.True(response.IsSuccessStatusCode);
        var recorded = Assert.Single(backend.Requests);
        Assert.Equal("/_elsa/extension-builder/workspaces/ws-1", recorded.PathAndQuery);
    }

    [Fact]
    public async Task ForwardsTheCorrectSuffixWhenTheBrowserSendsMixedCaseRoutePath()
    {
        // Route matching is case-insensitive but GetEncodedPathAndQuery preserves the browser's casing; the suffix
        // locator must not silently slice a garbage path when the route-group casing differs.
        var backend = RecordingBackend.RespondingWith(_ => JsonOk("{}"));
        var client = await StartBridgeHostAsync(backend);

        var response = await client.GetAsync(StudioExtensionBuilderBridge.RouteGroup.ToUpperInvariant() + "/workspaces/ws-1");

        Assert.True(response.IsSuccessStatusCode);
        var recorded = Assert.Single(backend.Requests);
        Assert.Equal("/_elsa/extension-builder/workspaces/ws-1", recorded.PathAndQuery);
    }

    [Fact]
    public async Task RelaysBuildLogAsPlainTextVerbatim()
    {
        const string log = "restore ok\nbuild ok\n1 warning";
        var backend = RecordingBackend.RespondingWith(_ => TextOk(log));
        var client = await StartBridgeHostAsync(backend);

        var response = await SendAsync(client, Op("get-build-log"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType!.ToString());
        Assert.Equal(log, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RelaysBuildArtifactStreamWithContentDispositionPreserved()
    {
        var artifactBytes = new byte[] { 0x50, 0x4b, 0x03, 0x04, 0x2a };
        var backend = RecordingBackend.RespondingWith(_ =>
        {
            var content = new ByteArrayContent(artifactBytes);
            content.Headers.ContentType = new("application/octet-stream");
            content.Headers.ContentDisposition = new("attachment") { FileName = "artifact.nupkg" };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var client = await StartBridgeHostAsync(backend);

        var response = await SendAsync(client, Op("get-build-artifact"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.ToString());
        Assert.Contains("artifact.nupkg", response.Content.Headers.ContentDisposition!.ToString());
        Assert.Equal(artifactBytes.Length, response.Content.Headers.ContentLength);
        Assert.Equal(artifactBytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task RelaysJsonArrayPayloadVerbatim()
    {
        // Regression against reusing the probe's JSON-object guard: the relay's JSON check is a content-type sniff
        // only, so array payloads relay untouched.
        const string workspaces = """[{"id":"ws-1"},{"id":"ws-2"}]""";
        var backend = RecordingBackend.RespondingWith(_ => JsonOk(workspaces));
        var client = await StartBridgeHostAsync(backend);

        var response = await SendAsync(client, Op("list-workspaces"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType!.ToString());
        Assert.Equal(workspaces, await response.Content.ReadAsStringAsync());
    }

    // ---- Harness -----------------------------------------------------------------------------------------------------

    private static StudioExtensionBuilderBridge.BridgeOperation Op(string name) =>
        StudioExtensionBuilderBridge.Operations.Single(op => op.Name == name);

    private static TheoryData<string> OperationNames(Func<StudioExtensionBuilderBridge.BridgeOperation, bool> predicate)
    {
        var data = new TheoryData<string>();
        foreach (var op in StudioExtensionBuilderBridge.Operations.Where(predicate))
            data.Add(op.Name);
        return data;
    }

    // A concrete browser URL suffix for the operation: route parameters filled with sample values, plus the query
    // strings the real SPA sends on the routes that take them — the relay must carry those through untouched.
    private static string SampleSuffix(StudioExtensionBuilderBridge.BridgeOperation op)
    {
        var suffix = op.Route
            .Replace("{workspaceId}", "ws-1")
            .Replace("{projectId}", "proj-1")
            .Replace("{buildId}", "build-1")
            .Replace("{artifactId}", "art-1")
            .Replace("{**path}", "Activities/Sample.cs");

        if (op.Route.EndsWith("/working-copies"))
            suffix += "?sessionId=s1";
        else if (op.Route.EndsWith("/repository-tree"))
            suffix += "?solutionPath=Sample.sln";
        else if (op.Route.Contains("/source-control/diff/"))
            suffix += "?staged=true";

        return suffix;
    }

    // The JSON body the tests send on bodied (POST/PUT) operations; the relay must forward it byte-identical.
    private const string SampleRequestBody = """{ "message": "sample browser payload" }""";

    private static bool IsBodied(StudioExtensionBuilderBridge.BridgeOperation op) => op.Method is "POST" or "PUT";

    private static HttpRequestMessage BuildBrowserRequest(StudioExtensionBuilderBridge.BridgeOperation op)
    {
        var request = new HttpRequestMessage(new HttpMethod(op.Method), StudioExtensionBuilderBridge.RouteGroup + SampleSuffix(op));
        if (IsBodied(op))
            request.Content = new StringContent(SampleRequestBody, System.Text.Encoding.UTF8, "application/json");
        return request;
    }

    private static void AssertRequestBodyRelayed(StudioExtensionBuilderBridge.BridgeOperation op, RecordedRequest recorded)
    {
        if (IsBodied(op))
        {
            Assert.Equal(SampleRequestBody, recorded.Body);
            Assert.StartsWith("application/json", recorded.ContentType);
        }
        else
        {
            Assert.True(string.IsNullOrEmpty(recorded.Body));
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, StudioExtensionBuilderBridge.BridgeOperation op)
    {
        using var request = BuildBrowserRequest(op);
        return await client.SendAsync(request);
    }

    // Starts an auth-enabled host whose stub backend recognizes the known bearers, then sends the operation with the
    // given one. Returns the backend too so callers can assert the outbound management-call count.
    private async Task<(HttpResponseMessage Response, RecordingBackend Backend)> SendWithBearerAsync(
        StudioExtensionBuilderBridge.BridgeOperation op,
        string bearer)
    {
        var backend = RecordingBackend.RespondingWith(WithSessionEndpoint(_ => BackendSuccessFor(op)));
        var client = await StartBridgeHostAsync(backend, authEnabled: true);
        using var request = BuildBrowserRequest(op);
        request.Headers.Authorization = new("Bearer", bearer);
        return (await client.SendAsync(request), backend);
    }

    // Starts an auth-disabled host against the given backend stub and sends the named operation through the bridge.
    private async Task<HttpResponseMessage> SendThroughBridgeAsync(RecordingBackend backend, string operationName)
    {
        var client = await StartBridgeHostAsync(backend);
        return await SendAsync(client, Op(operationName));
    }

    private static StudioExtensionBuilderBridgeError ReadError(string body)
    {
        var error = JsonSerializer.Deserialize<StudioExtensionBuilderBridgeError>(body, ErrorJsonOptions);
        Assert.NotNull(error);
        return error!;
    }

    private static async Task<StudioExtensionBuilderBridgeError> ReadErrorAsync(HttpResponseMessage response) =>
        ReadError(await response.Content.ReadAsStringAsync());

    private static readonly JsonSerializerOptions ErrorJsonOptions = new(JsonSerializerDefaults.Web);

    private static HttpResponseMessage BackendSuccessFor(StudioExtensionBuilderBridge.BridgeOperation op) => op.Payload switch
    {
        StudioExtensionBuilderBridge.BridgePayloadKind.Text => TextOk("log line"),
        StudioExtensionBuilderBridge.BridgePayloadKind.Stream => new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("application/octet-stream") } }
        },
        _ => JsonOk("""{ "ok": true }""")
    };

    private static HttpResponseMessage JsonOk(string json) => Json(HttpStatusCode.OK, json);

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

    private static HttpResponseMessage TextOk(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, System.Text.Encoding.UTF8, "text/plain") };

    // Wraps a backend responder with the backend's anonymous /_elsa/identity/session endpoint, recognizing the known
    // bearers and reflecting their permission sets (the flat camelCase array the auth gate projects onto the ticket).
    private static Func<HttpRequestMessage, HttpResponseMessage> WithSessionEndpoint(Func<HttpRequestMessage, HttpResponseMessage> backendResponder) =>
        request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/identity/session"))
                return backendResponder(request);

            var bearer = request.Headers.Authorization?.Parameter;
            var json = bearer is not null && BearerPermissions.TryGetValue(bearer, out var permissions)
                ? JsonSerializer.Serialize(new { status = "authenticated", subject = "alice", permissions })
                : """{ "status": "anonymous" }""";
            return JsonOk(json);
        };

    private async Task<HttpClient> StartBridgeHostAsync(
        RecordingBackend backend,
        string? backendBaseUrl = BackendBaseUrl,
        string? managementKey = ManagementKey,
        bool authEnabled = false,
        TimeProvider? timeProvider = null,
        string? pathBase = null)
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
        if (timeProvider is not null)
            builder.Services.AddSingleton(timeProvider);

        // Route both the typed relay client and the named auth client through the recording backend stub so the test
        // asserts real HTTP behaviour (methods, paths, headers, bodies, outbound-call counts) at the wire.
        builder.Services.AddHttpClient(nameof(StudioExtensionBuilderRelayClient))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        builder.Services.AddHttpClient(StudioBridgeAuthHandler.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => backend);

        var app = builder.Build();
        if (pathBase is not null)
            app.UsePathBase(pathBase);
        app.UseAuthentication();
        app.UseAuthorization();
        // Mapping the management bridge maps the Extension Builder relay group with it — Program.cs stays untouched.
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
    /// (method, path+query, body, content type, management-key header, Authorization presence) so tests can assert
    /// forwarding fidelity, header hygiene, and the fail-closed no-call guarantees.
    /// </summary>
    private sealed class RecordingBackend : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _responder;
        private readonly Exception? _throw;

        private RecordingBackend(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? responder, Exception? toThrow)
        {
            _responder = responder;
            _throw = toThrow;
        }

        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>Recorded requests that carried the management key (i.e. Studio→backend management calls).</summary>
        public IReadOnlyList<RecordedRequest> ManagementRequests => Requests.Where(x => x.ManagementKey is not null).ToArray();

        public static RecordingBackend RespondingWith(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            new((request, _) => Task.FromResult(responder(request)), null);

        public static RecordingBackend RespondingWithAsync(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
            new(responder, null);

        public static RecordingBackend Throwing(Exception toThrow) => new(null, toThrow);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryGetValues(StudioBackendManagementOptions.ManagementApiKeyHeaderName, out var keyValues);
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(
                request.Method.Method,
                request.RequestUri!.PathAndQuery,
                body,
                request.Content?.Headers.ContentType?.ToString(),
                keyValues?.FirstOrDefault(),
                request.Headers.Authorization is not null));

            if (_throw is not null)
                throw _throw;

            return await _responder!(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(
        string Method,
        string PathAndQuery,
        string? Body,
        string? ContentType,
        string? ManagementKey,
        bool HasAuthorization);
}
