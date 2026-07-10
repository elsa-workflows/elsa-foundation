using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Models;
using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Acceptance for spec 089 sub-unit E (synchronous HTTP responses) — User Story 5 acceptance 5.1-5.5 and FR-021 —
/// proven at the host level over the real ASP.NET Core pipeline (see <see cref="HttpEndpointHostFixture"/>). These
/// are the first tests to exercise the WHOLE sync-response chain end to end with no fakes: a sync-mode endpoint
/// populates the request scope's <c>SyncHttpResponseSink</c> and dispatches with the request services as ambient
/// services → the in-process actor drains INLINE on the caller's async flow → a <c>WriteHttpResponse</c> the run
/// reaches resolves that same sink and writes the live response in the SAME exchange → the middleware returns the
/// workflow-authored status/headers/body rather than the 202 baseline. The degrade path (suspend-first, timeout),
/// the async baseline's bit-identical 202, the mid-flow (D+E) resume-writes-response scenario, and the FR-021
/// ambient-services purity sweep are all covered here.
/// </summary>
public sealed class HttpEndpointSyncResponseEndToEndTests : IAsyncLifetime
{
    private const string BasePath = "/workflows/http/";

    private HttpEndpointHostFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await HttpEndpointHostFixture.StartAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task SyncEndpoint_WorkflowWritesResponse_SameExchangeReturnsAuthoredResponse_AndRecordsTheDurableArtifact()
    {
        // Scenario 5.1: a sync endpoint whose workflow writes a custom status/body/content-type/header returns THAT
        // response in the same exchange (NOT 202), and the durable HttpResponseInstruction artifact is still recorded.
        await _fixture.PublishSyncEndpointWithWriteResponseWorkflowAsync(
            artifactId: "artifact-sync-write",
            path: "sync/orders",
            method: "POST",
            resultValueId: "sync-result",
            statusCode: 201,
            body: """{"id":42}""",
            contentType: "application/json",
            header: ("X-Elsa-Sync", "yes"));

        var response = await _fixture.Client.PostAsync($"{BasePath}sync/orders",
            new StringContent("""{"orderId":7}""", Encoding.UTF8, "application/json"));

        // The workflow authored the live response — not the 202 baseline.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("""{"id":42}""", await response.Content.ReadAsStringAsync());
        Assert.StartsWith("application/json", response.Content.Headers.ContentType!.ToString());
        Assert.Equal("yes", Assert.Single(response.Headers.GetValues("X-Elsa-Sync")));

        // The durable artifact is recorded alongside the live write (E-D3: the artifact is unconditional).
        var artifact = await ReadArtifactForSingleRunAsync();
        Assert.Equal(201, artifact.GetProperty(nameof(HttpResponseInstruction.StatusCode)).GetInt32());
        Assert.Equal("""{"id":42}""", artifact.GetProperty(nameof(HttpResponseInstruction.Body)).GetString());
        Assert.Equal("application/json", artifact.GetProperty(nameof(HttpResponseInstruction.ContentType)).GetString());
    }

    [Fact]
    public async Task SyncEndpoint_SuspendsBeforeWritingResponse_DegradesTo202WithExecutionId()
    {
        // Scenario 5.2: a sync endpoint whose run suspends at a mid-flow endpoint BEFORE any WriteHttpResponse. No
        // live response was written, so the middleware degrades to the 202 baseline with the started execution id.
        await _fixture.PublishSyncSuspendBeforeResponseWorkflowAsync(
            artifactId: "artifact-sync-suspend",
            startPath: "sync/suspend/start",
            suspendPath: "sync/suspend/callback",
            method: "POST",
            startResultValueId: "sync-suspend-result");

        var response = await _fixture.Client.PostAsync($"{BasePath}sync/suspend/start",
            new StringContent("""{"phase":"start"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var startedId = Assert.Single(await ReadStartedIdsAsync(response));

        // The run really suspended at the mid-flow endpoint (a waiting bookmark exists), so the 202 degrade is the
        // correct outcome — no response was authored in the starting exchange.
        Assert.Single(await _fixture.ListBookmarksAsync(startedId));
    }

    [Fact]
    public async Task SyncEndpoint_RequestTimeoutExceeded_Replies408_AndTheInstanceRemainsValid()
    {
        // Scenario 5.3: a sync endpoint with a short RequestTimeout whose workflow stalls past it. The inline drain
        // stalls on the timeout token; the middleware maps the OperationCanceledException to 408. The instance's
        // durable state remains valid (the run was persisted before the stall and stays inspectable).
        await _fixture.PublishSyncStallThenResponseWorkflowAsync(
            artifactId: "artifact-sync-timeout",
            path: "sync/slow",
            method: "POST",
            resultValueId: "sync-slow-result",
            requestTimeout: TimeSpan.FromMilliseconds(150),
            stallDuration: TimeSpan.FromSeconds(30));

        var response = await _fixture.Client.PostAsync($"{BasePath}sync/slow",
            new StringContent("""{"n":1}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.RequestTimeout, response.StatusCode);

        // Durable state remains valid: the run was persisted (the endpoint completed and captured its result before
        // the stall), so the timeout aborted the in-flight wait without corrupting the instance.
        Assert.Equal(1, await _fixture.CountWorkflowExecutionsAsync());
    }

    [Fact]
    public async Task AsyncEndpoint_SameWriteResponseWorkflow_RepliesBitIdentical202Baseline_AndRecordsTheArtifact()
    {
        // Scenario 5.4: the SAME WriteHttpResponse workflow authored Async (the default). The response is the
        // bit-identical 202 baseline {started:[...],resumed:[...]}; the live response was NOT written by the workflow;
        // the durable artifact is still recorded.
        await _fixture.PublishSyncEndpointWithWriteResponseWorkflowAsync(
            artifactId: "artifact-async-write",
            path: "async/orders",
            method: "POST",
            resultValueId: "async-result",
            statusCode: 201,
            body: """{"id":42}""",
            contentType: "application/json",
            header: ("X-Elsa-Sync", "yes"),
            responseMode: ResponseMode.Async);

        var response = await _fixture.Client.PostAsync($"{BasePath}async/orders",
            new StringContent("""{"orderId":7}""", Encoding.UTF8, "application/json"));

        // Bit-identical 202 baseline: the workflow-authored status/body/header did NOT reach the wire.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.StartsWith("application/json", response.Content.Headers.ContentType!.ToString());
        Assert.False(response.Headers.Contains("X-Elsa-Sync"));
        var body = await response.Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(body);
        // The 202 body is exactly {started:[id], resumed:[]} — the workflow body "{\"id\":42}" is nowhere in it.
        var startedId = Assert.Single(payload.RootElement.GetProperty("started").EnumerateArray().Select(e => e.GetString()!));
        Assert.Empty(payload.RootElement.GetProperty("resumed").EnumerateArray());

        // The artifact is still recorded — the async run reached WriteHttpResponse and recorded its intended response.
        var artifact = await _fixture.ReadHttpResponseArtifactAsync(startedId);
        Assert.Equal(201, artifact.GetProperty(nameof(HttpResponseInstruction.StatusCode)).GetInt32());
        Assert.Equal("""{"id":42}""", artifact.GetProperty(nameof(HttpResponseInstruction.Body)).GetString());
    }

    [Fact]
    public async Task MidFlowSyncEndpoint_ResumingRequestReceivesTheWorkflowsSubsequentWriteResponse_InTheSameExchange()
    {
        // Scenario 5.5 — the whole reason the D+E composition exists. A workflow started DIRECTLY suspends at a
        // mid-flow endpoint authored Sync; the RESUMING request drains the subsequent WriteHttpResponse INLINE on its
        // own fresh scope, so that request receives the workflow-authored response in the same exchange (fresh ambient
        // context on resume — the resume dispatch carries the resuming request's own RequestServices).
        const string path = "sync/midflow/{id}";
        await _fixture.PublishMidFlowSyncWriteResponseWorkflowAsync(
            artifactId: "artifact-sync-midflow",
            path: path,
            method: "POST",
            resultValueId: "sync-midflow-result",
            statusCode: 202 + 1, // 203 — a distinctive non-baseline status proving the workflow authored the reply
            body: """{"resumed":true}""",
            contentType: "application/json");

        var workflowExecutionId = await _fixture.StartWorkflowDirectlyAsync("artifact-sync-midflow");

        // The direct run drained to the mid-flow suspension: a waiting bookmark, and its route is live and resumable.
        Assert.Single(await _fixture.ListBookmarksAsync(workflowExecutionId));
        Assert.True(_fixture.RouteTableContains(path), "The mid-flow bookmark's route did not reach the live route table.");

        var response = await _fixture.Client.PostAsync($"{BasePath}sync/midflow/42",
            new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json"));

        // The resuming request received the workflow's subsequent WriteHttpResponse in THIS exchange (not a 202).
        Assert.Equal((HttpStatusCode)203, response.StatusCode);
        Assert.Equal("""{"resumed":true}""", await response.Content.ReadAsStringAsync());
        Assert.StartsWith("application/json", response.Content.Headers.ContentType!.ToString());

        // And the durable artifact was recorded on the resumed run.
        var artifact = await _fixture.ReadHttpResponseArtifactAsync(workflowExecutionId);
        Assert.Equal(203, artifact.GetProperty(nameof(HttpResponseInstruction.StatusCode)).GetInt32());
    }

    [Fact]
    public async Task SyncDispatch_PersistedState_CarriesNoLiveServiceReferences_FR021()
    {
        // FR-021: after a sync dispatch (which passed the request services as ambient services), sweep the persisted
        // state stores accessible via the fixture's service provider and assert NO live-service references crossed
        // into durable state — the ambient services are a live, request-affine reference that must never serialize.
        await _fixture.PublishSyncEndpointWithWriteResponseWorkflowAsync(
            artifactId: "artifact-sync-purity",
            path: "sync/purity",
            method: "POST",
            resultValueId: "sync-purity-result",
            statusCode: 200,
            body: "ok",
            contentType: "text/plain");

        var response = await _fixture.Client.PostAsync($"{BasePath}sync/purity",
            new StringContent("""{"n":1}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Sweep every persisted store's serialized JSON for a service-provider fingerprint. The runtime persists
        // envelopes/checkpoints as opaque state; a leaked IServiceProvider would surface as a provider type name in
        // the serialized payload. (Mirrors the T003 integration purity assertion at the HTTP E2E level.)
        var services = _fixture.Services;
        var executions = await services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync();
        var runId = Assert.Single(executions).WorkflowExecutionId;

        var durableValues = await services.GetRequiredService<IDurableValueStateStore>().ListAsync(runId);
        var bookmarks = await services.GetRequiredService<IBookmarkStateStore>().ListAsync(runId);

        var serialized = new StringBuilder();
        foreach (var value in durableValues)
            serialized.Append(JsonSerializer.Serialize(value));
        foreach (var bookmark in bookmarks)
            serialized.Append(JsonSerializer.Serialize(bookmark));
        foreach (var execution in executions)
            serialized.Append(JsonSerializer.Serialize(execution));

        var haystack = serialized.ToString();
        Assert.DoesNotContain("ServiceProvider", haystack, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RequestServices", haystack, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AmbientServices", haystack, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Reads the durable HttpResponse artifact for the single run the test started (the store holds exactly one).</summary>
    private async Task<JsonElement> ReadArtifactForSingleRunAsync()
    {
        var executions = await _fixture.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync();
        var runId = Assert.Single(executions).WorkflowExecutionId;
        return await _fixture.ReadHttpResponseArtifactAsync(runId);
    }

    private static async Task<IReadOnlyList<string>> ReadStartedIdsAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(json);
        return payload.RootElement.GetProperty("started").EnumerateArray().Select(e => e.GetString()!).ToArray();
    }
}
