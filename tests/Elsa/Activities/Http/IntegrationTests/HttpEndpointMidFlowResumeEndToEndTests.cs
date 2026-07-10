using System.Net;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Acceptance for spec 089 sub-unit D (mid-flow <see cref="HttpEndpoint"/> resume) — User Story 4 and its edge
/// cases — proven at the host level over the real ASP.NET Core pipeline (see <see cref="HttpEndpointHostFixture"/>).
/// These are the first tests to exercise the WHOLE mid-flow chain end to end with no fakes: an activity suspends and
/// creates a bookmark → the bookmark-created checkpoint commits → the <c>BookmarkLifecycleNotifier</c> fires the
/// <c>RouteTableBookmarkObserver</c> → the resolver's union projects the waiting template into the live route table
/// → a matching request dispatches through the real middleware in <c>StartAndResume</c> mode → the router resumes the
/// waiting instance → the <c>[ResumeTarget]</c> sets the run's durable outputs from the live request. The in-process
/// actor drains synchronously before the response returns, so durable state is asserted immediately after each call.
/// </summary>
public sealed class HttpEndpointMidFlowResumeEndToEndTests : IAsyncLifetime
{
    private const string BasePath = "/workflows/http/";

    private HttpEndpointHostFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await HttpEndpointHostFixture.StartAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task DirectlyStartedWorkflow_SuspendsAtMidFlowEndpoint_MatchingRequestResumesItWithTheLiveRequest()
    {
        // Spec US4 independent test: a workflow started DIRECTLY (no HTTP stimulus) suspends at a
        // CanStartWorkflow=false endpoint; a matching request resumes it and the run's durable Result/RouteData
        // reflect the resume request. (ParsedContent is derived at the activity from Body + the Content-Type header
        // per spec 089 #9 — its derivation is unit-covered in HttpEndpointExecutionTests; here the delivered Body on
        // the durable Result is the whole-chain evidence.)
        const string path = "callbacks/{id}";
        const string resultValueId = "midflow-result";
        await _fixture.PublishResumableHttpEndpointWorkflowAsync("artifact-midflow", path, "POST", resultValueId);

        var workflowExecutionId = await _fixture.StartWorkflowDirectlyAsync("artifact-midflow");

        // The direct run drained synchronously to the mid-flow suspension: one waiting bookmark, and — the whole-chain
        // verification target — the bookmark-created lifecycle notification refreshed the route table so the template
        // is now live and resumable.
        var bookmark = Assert.Single(await _fixture.ListBookmarksAsync(workflowExecutionId));
        Assert.Equal(HttpEndpointRouting.StimulusType, bookmark.StimulusType);
        Assert.True(_fixture.RouteTableContains(path), "The mid-flow bookmark's route did not reach the live route table — the bookmark-lifecycle notifier likely did not fire on the create path.");

        var response = await _fixture.Client.PostAsync($"{BasePath}callbacks/42",
            new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var resumed = Assert.Single(await ReadResumedIdsAsync(response));
        Assert.Equal(workflowExecutionId, resumed);
        Assert.Empty(await ReadStartedIdsAsync(response)); // resume only — no trigger started a new instance

        var model = await ReadResultAsync(workflowExecutionId, resultValueId);
        Assert.Equal("POST", model.Method);
        Assert.Equal("callbacks/42", model.Path);
        Assert.Equal("42", Assert.Contains("id", model.RouteData!));
        Assert.Equal("""{"ok":true}""", model.Body);
    }

    [Fact]
    public async Task HttpStart_ThenMidFlowSuspend_CallbackResumes_BothRequestsLandOnTheirOwnEndpoint()
    {
        // Spec US4: one workflow sequences a start-trigger endpoint (template A) and a mid-flow endpoint (template B).
        // A request to A starts the run, which reaches B and suspends; a request to B resumes and completes. Assert
        // both requests' payloads landed on their respective endpoint's durable Result.
        await _fixture.PublishSequencedHttpEndpointsWorkflowAsync(
            artifactId: "artifact-seq",
            startPath: "seq/start",
            startResultValueId: "seq-start-result",
            callbackPath: "seq/callback",
            callbackResultValueId: "seq-callback-result",
            method: "POST");

        // Request A starts the run; the start endpoint completes and the Sequence schedules the mid-flow endpoint,
        // which suspends. The 202 reports the started instance.
        var startResponse = await _fixture.Client.PostAsync($"{BasePath}seq/start",
            new StringContent("""{"phase":"start"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, startResponse.StatusCode);
        var workflowExecutionId = Assert.Single(await ReadStartedIdsAsync(startResponse));

        // The mid-flow callback endpoint suspended and its route is live.
        Assert.Single(await _fixture.ListBookmarksAsync(workflowExecutionId));
        Assert.True(_fixture.RouteTableContains("seq/callback"));

        // Request B resumes the waiting instance.
        var callbackResponse = await _fixture.Client.PostAsync($"{BasePath}seq/callback",
            new StringContent("""{"phase":"callback"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, callbackResponse.StatusCode);
        var resumed = Assert.Single(await ReadResumedIdsAsync(callbackResponse));
        Assert.Equal(workflowExecutionId, resumed);

        // Both requests observed the live request on their own endpoint.
        var startModel = await ReadResultAsync(workflowExecutionId, "seq-start-result");
        Assert.Equal("seq/start", startModel.Path);
        Assert.Equal("""{"phase":"start"}""", startModel.Body);

        var callbackModel = await ReadResultAsync(workflowExecutionId, "seq-callback-result");
        Assert.Equal("seq/callback", callbackModel.Path);
        Assert.Equal("""{"phase":"callback"}""", callbackModel.Body);
    }

    [Fact]
    public async Task StartTriggerAndWaitingInstanceOnSameRoute_OneRequest_StartsOneAndResumesTheOther_NoSelfResume()
    {
        // Spec scenario 4.2: a published start trigger AND a suspended instance share the SAME (template, method).
        // One request starts a NEW instance AND resumes the pre-existing waiting one — and the freshly started
        // instance does NOT self-resume (StartAndResume's snapshot-before-start guard). Exactly one resume, of the
        // pre-existing instance; the 202 body carries both started[] and resumed[].
        const string path = "shared/orders";
        const string method = "POST";

        // A start-capable trigger workflow on the route.
        await _fixture.PublishHttpEndpointWorkflowAsync("artifact-shared-start", path, "shared-start-result", method);

        // A separate resumable workflow, started directly so it is already suspended on the same route before the
        // request arrives.
        await _fixture.PublishResumableHttpEndpointWorkflowAsync("artifact-shared-wait", path, method, "shared-wait-result");
        var waitingId = await _fixture.StartWorkflowDirectlyAsync("artifact-shared-wait");
        Assert.Single(await _fixture.ListBookmarksAsync(waitingId));

        var response = await _fixture.Client.PostAsync($"{BasePath}{path}",
            new StringContent("""{"n":1}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var started = await ReadStartedIdsAsync(response);
        var resumed = await ReadResumedIdsAsync(response);

        // Exactly one start (the trigger) and exactly one resume (the pre-existing waiting instance).
        var startedId = Assert.Single(started);
        var resumedId = Assert.Single(resumed);
        Assert.Equal(waitingId, resumedId);

        // The started instance did not resume itself.
        Assert.NotEqual(startedId, resumedId);
        Assert.DoesNotContain(startedId, resumed);
    }

    [Fact]
    public async Task ExpiredBookmark_DoesNotResume_RepliesNotFound()
    {
        // Spec scenario 4.3: a suspended instance whose bookmark has expired must not resume. Seed a suspended
        // instance, overwrite its bookmark with a past ExpiresAt (via the store upsert), refresh the route table,
        // then send the matching request → 404 (no dispatched resume, no start).
        const string path = "expiring/{id}";
        await _fixture.PublishResumableHttpEndpointWorkflowAsync("artifact-expiring", path, "POST", "expiring-result");
        var workflowExecutionId = await _fixture.StartWorkflowDirectlyAsync("artifact-expiring");

        var bookmark = Assert.Single(await _fixture.ListBookmarksAsync(workflowExecutionId));
        await _fixture.ExpireBookmarkAsync(bookmark);

        var response = await _fixture.Client.PostAsync($"{BasePath}expiring/7", new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConsumedBookmark_RouteIsRemoved_SecondIdenticalRequestRepliesNotFound()
    {
        // Post-resume: after a resume consumes the only waiting bookmark, the consume lifecycle notification
        // re-projects the route table (the template's last waiter is gone), so a second identical request 404s.
        const string path = "once/{id}";
        await _fixture.PublishResumableHttpEndpointWorkflowAsync("artifact-once", path, "POST", "once-result");
        var workflowExecutionId = await _fixture.StartWorkflowDirectlyAsync("artifact-once");
        Assert.Single(await _fixture.ListBookmarksAsync(workflowExecutionId));

        // First request resumes and completes the instance, consuming the bookmark.
        var first = await _fixture.Client.PostAsync($"{BasePath}once/1", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Single(await ReadResumedIdsAsync(first));

        // The bookmark is gone and the consume observer removed the now-unclaimed route.
        Assert.Empty(await _fixture.ListBookmarksAsync(workflowExecutionId));
        Assert.False(_fixture.RouteTableContains(path));

        // A second identical request has nothing to resume (and no start trigger) → 404.
        var second = await _fixture.Client.PostAsync($"{BasePath}once/1", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    private async Task<HttpRequestModel> ReadResultAsync(string workflowExecutionId, string valueId)
    {
        var captured = await _fixture.ReadCapturedOutputAsync(workflowExecutionId, valueId);
        return captured.Deserialize<HttpRequestModel>()!;
    }

    private static async Task<IReadOnlyList<string>> ReadStartedIdsAsync(HttpResponseMessage response) =>
        (await ReadOutcomeAsync(response)).Started;

    private static async Task<IReadOnlyList<string>> ReadResumedIdsAsync(HttpResponseMessage response) =>
        (await ReadOutcomeAsync(response)).Resumed;

    /// <summary>Parses the 202 body's <c>{started, resumed}</c> arrays in one read (the response stream reads once).</summary>
    private static async Task<(IReadOnlyList<string> Started, IReadOnlyList<string> Resumed)> ReadOutcomeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(json);
        return (ReadArray(payload.RootElement, "started"), ReadArray(payload.RootElement, "resumed"));
    }

    private static IReadOnlyList<string> ReadArray(JsonElement root, string property) =>
        root.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToArray();
}
