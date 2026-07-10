using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Http.Core;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// In-process execution coverage for <see cref="HttpEndpoint"/> through the real workflow agent on the shared
/// <see cref="WorkflowExecutionHarness"/>. Covers Result resolution (spec 089 sub-unit A, FR-002 — the live
/// request travels on the dedicated stimulus-input channel; the workflow-inputs bag can neither supply nor forge
/// it) and the start-vs-mid-flow decision (spec 089 sub-unit D, D-D1/D-D2 — the endpoint completes with the
/// stimulus when it is the run's trigger node or a start-capable direct run, otherwise suspends with one bookmark
/// per supported method).
/// </summary>
public sealed class HttpEndpointExecutionTests
{
    private const string NodeId = "node-http-endpoint";
    private const string ResultValueId = "http-endpoint-result";

    // The endpoint is the root node, so it is handed the first deterministic activity-execution id (see NewHarness).
    private const string ActivityExecutionId = "actexec-http-endpoint";

    [Fact]
    public async Task SurfacesLiveRequest_WhenStimulusInputIsSeeded()
    {
        await using var harness = NewHarness();
        var liveRequest = new HttpRequestModel(
            Path: "orders/webhook",
            Method: "POST",
            Headers: new Dictionary<string, string[]> { ["x-test"] = ["abc"] },
            Query: new Dictionary<string, string[]> { ["q"] = ["1"] },
            Body: """{"id":7}""");

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook"), JsonSerializer.SerializeToElement(liveRequest));

        run.AssertCompleted(NodeId);
        run.AssertWorkflowCompleted();

        var result = await ResultAsync(harness);
        Assert.Equal("POST", result.GetProperty(nameof(HttpRequestModel.Method)).GetString());
        Assert.Equal("""{"id":7}""", result.GetProperty(nameof(HttpRequestModel.Body)).GetString());
        Assert.Equal("abc", result.GetProperty(nameof(HttpRequestModel.Headers)).GetProperty("x-test")[0].GetString());
        Assert.Equal("1", result.GetProperty(nameof(HttpRequestModel.Query)).GetProperty("q")[0].GetString());
    }

    [Fact]
    public async Task FallsBackToAuthoredRoute_WhenNoStimulusInputIsSeeded()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(NewEndpointExecutable("Orders/Webhook/"));

        run.AssertWorkflowCompleted();

        var result = await ResultAsync(harness);
        Assert.Equal("orders/webhook", result.GetProperty(nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty(nameof(HttpRequestModel.Body)).ValueKind);
    }

    [Theory]
    [InlineData("\"not-a-request-model\"")] // JSON string — not an object
    [InlineData("""{"foo":1}""")] // foreign JSON object — deserializes but has no Path/Method
    [InlineData("""{"Path":"x"}""")] // half-shaped object — Method missing
    public async Task FallsBackToAuthoredRoute_WhenStimulusInputIsForeignOrMalformed(string foreignPayload)
    {
        // A foreign stimulus payload (another trigger module started this artifact) must never surface as a
        // half-populated request model — identifying members (Path, Method) are validated after deserialize.
        await using var harness = NewHarness();
        using var document = JsonDocument.Parse(foreignPayload);

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook"), document.RootElement.Clone());

        run.AssertWorkflowCompleted();

        var result = await ResultAsync(harness);
        Assert.Equal("orders/webhook", result.GetProperty(nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal("*", result.GetProperty(nameof(HttpRequestModel.Method)).GetString());
    }

    [Fact]
    public async Task WorkflowInputsBag_CannotForgeTheStimulusInput()
    {
        // Regression pin for the spec-089 review: a caller-supplied workflow input (the execute API's inputs
        // bag) named like the old well-known key must NOT surface as the endpoint's Result — the stimulus
        // travels on its own channel, so the activity falls back to the authored route.
        await using var harness = NewHarness();
        var forged = new HttpRequestModel("evil", "DELETE", new Dictionary<string, string[]>(), new Dictionary<string, string[]>(), "forged");
        var inputs = new Dictionary<string, JsonElement>
        {
            ["stimulusInput"] = JsonSerializer.SerializeToElement(forged)
        };

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook"), inputs);

        run.AssertWorkflowCompleted();

        var result = await ResultAsync(harness);
        Assert.Equal("orders/webhook", result.GetProperty(nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal("*", result.GetProperty(nameof(HttpRequestModel.Method)).GetString());
    }

    [Fact]
    public async Task StartPath_CompletesWithStimulus_WhenTriggerNodeIdMatchesOwnNode()
    {
        // D-D1 start path: the run carries this node's id as the trigger identity → the endpoint completes with the
        // live request from the stimulus channel (it does NOT suspend).
        await using var harness = NewHarness();
        var liveRequest = new HttpRequestModel("orders/webhook", "POST", new Dictionary<string, string[]>(), new Dictionary<string, string[]>(), """{"id":9}""");

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook"), JsonSerializer.SerializeToElement(liveRequest), triggerNodeId: NodeId);

        run.AssertCompleted(NodeId);
        run.AssertWorkflowCompleted();
        Assert.Empty(await BookmarksAsync(harness));

        var result = await ResultAsync(harness);
        Assert.Equal("POST", result.GetProperty(nameof(HttpRequestModel.Method)).GetString());
        Assert.Equal("""{"id":9}""", result.GetProperty(nameof(HttpRequestModel.Body)).GetString());
    }

    [Fact]
    public async Task MidFlow_Suspends_AndCreatesOneBookmarkPerMethod_WithHashesAndOptionMetadata()
    {
        // D-D2: a mid-flow endpoint (another node triggered the run — here a foreign node id) suspends with one
        // bookmark per supported method, each carrying the per-method hash and the full binding-parity metadata
        // (template + method + endpoint options), and does NOT complete.
        await using var harness = NewHarness();
        var stimulus = JsonSerializer.SerializeToElement(new HttpRequestModel("callbacks/{id}", "GET", new Dictionary<string, string[]>(), new Dictionary<string, string[]>(), null));

        var run = await harness.RunAsync(
            NewEndpointExecutable("callbacks/{id}", methods: ["GET", "POST"]),
            stimulus,
            triggerNodeId: "some-other-node");

        Assert.False(CompletedEndpoint(run));
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);

        var bookmarks = await BookmarksAsync(harness);
        Assert.Equal(2, bookmarks.Count);
        foreach (var method in new[] { "get", "post" })
        {
            var bookmark = Assert.Single(bookmarks, b => b.BookmarkId == $"http-endpoint:{ActivityExecutionId}:{method}");
            Assert.Equal(HttpEndpointRouting.StimulusType, bookmark.StimulusType);
            Assert.Equal(HttpEndpointStimulus.Hash("callbacks/{id}", method), bookmark.StimulusHash);
            Assert.Equal(HttpEndpoint.ResumeTargetId, bookmark.ResumeTargetId);
            Assert.Null(bookmark.ExpiresAt);
            Assert.Equal("callbacks/{id}", bookmark.Metadata[HttpEndpointRouting.TemplateMetadataKey]);
            Assert.Equal(method, bookmark.Metadata[HttpEndpointRouting.MethodMetadataKey]);
        }
    }

    [Fact]
    public async Task MidFlow_StampsEndpointOptionsOnBookmarkMetadata()
    {
        // The bookmark metadata is byte-identical to the trigger-binding metadata (same HttpEndpointStimulusOptions
        // payload) so the middleware reads authorize/policy/limit off a resume-only match exactly as off a binding.
        await using var harness = NewHarness();
        var node = NewEndpointExecutableWithOptions("secure/{id}", authorize: true, policy: "admins", sizeLimit: 2048);

        var run = await harness.RunAsync(node, JsonSerializer.SerializeToElement("x"), triggerNodeId: "other");

        Assert.False(CompletedEndpoint(run));
        var bookmark = Assert.Single(await BookmarksAsync(harness));
        Assert.Equal("true", bookmark.Metadata[HttpEndpointRouting.AuthorizeMetadataKey]);
        Assert.Equal("admins", bookmark.Metadata[HttpEndpointRouting.PolicyMetadataKey]);
        Assert.Equal("2048", bookmark.Metadata[HttpEndpointRouting.RequestSizeLimitMetadataKey]);
    }

    [Fact]
    public async Task DirectRun_CanStartTrue_Completes_ViaAuthoredRouteFallback()
    {
        // D-D1 exception: a start-capable endpoint on a direct run (no trigger identity) keeps A's authored-route
        // fallback and completes.
        await using var harness = NewHarness();

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook", canStartWorkflow: true));

        run.AssertCompleted(NodeId);
        run.AssertWorkflowCompleted();
        Assert.Empty(await BookmarksAsync(harness));
        var result = await ResultAsync(harness);
        Assert.Equal("orders/webhook", result.GetProperty(nameof(HttpRequestModel.Path)).GetString());
    }

    [Fact]
    public async Task DirectRun_CanStartFalse_AlwaysSuspends()
    {
        // D-D1: a CanStartWorkflow = false endpoint ALWAYS suspends, even on a direct run (the US4 independent test
        // starts a workflow directly and must suspend at the mid-flow endpoint).
        await using var harness = NewHarness();

        var run = await harness.RunAsync(NewEndpointExecutable("callbacks/{id}", methods: ["GET"], canStartWorkflow: false));

        Assert.False(CompletedEndpoint(run));
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        var bookmark = Assert.Single(await BookmarksAsync(harness));
        Assert.Equal($"http-endpoint:{ActivityExecutionId}:get", bookmark.BookmarkId);
    }

    [Fact]
    public async Task Resume_SetsResultRouteDataAndParsedContent_FromTheResumeInput()
    {
        // T008 (D-D3): suspend at a mid-flow endpoint, then resume it with a matching request on the resume-input
        // channel — the [ResumeTarget] reads the request and sets Result/RouteData/ParsedContent.
        await using var harness = NewHarness();
        var mid = await harness.RunAsync(NewEndpointExecutable("callbacks/{id}", methods: ["GET"]), JsonSerializer.SerializeToElement("x"), triggerNodeId: "other");
        Assert.False(CompletedEndpoint(mid));

        var resumeRequest = new HttpRequestModel(
            Path: "callbacks/42",
            Method: "GET",
            Headers: new Dictionary<string, string[]>(),
            Query: new Dictionary<string, string[]>(),
            Body: """{"ok":true}""",
            RouteData: new Dictionary<string, string> { ["id"] = "42" },
            ParsedContent: JsonSerializer.SerializeToElement(new { ok = true }));

        var run = await ResumeAsync(harness, JsonSerializer.SerializeToElement(resumeRequest));

        run.AssertCompleted(NodeId);
        run.AssertWorkflowCompleted();
        Assert.Empty(await BookmarksAsync(harness));

        var result = await ResultAsync(harness);
        Assert.Equal("callbacks/42", result.GetProperty(nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal("GET", result.GetProperty(nameof(HttpRequestModel.Method)).GetString());
        Assert.Equal("42", result.GetProperty(nameof(HttpRequestModel.RouteData)).GetProperty("id").GetString());
        Assert.True(result.GetProperty(nameof(HttpRequestModel.ParsedContent)).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Resume_FallsBackToAuthoredRoute_WhenResumeInputIsMalformed()
    {
        // D-D7: the resume input is produced by our own middleware, so a missing/malformed payload is a defensive
        // path — the resume target falls back to the authored-route model rather than faulting.
        await using var harness = NewHarness();
        var mid = await harness.RunAsync(NewEndpointExecutable("callbacks/{id}", methods: ["GET"]), JsonSerializer.SerializeToElement("x"), triggerNodeId: "other");
        Assert.False(CompletedEndpoint(mid));

        // A foreign JSON object (no Path/Method) is not a request model → fallback.
        var run = await ResumeAsync(harness, JsonSerializer.SerializeToElement(new { foo = 1 }));

        run.AssertCompleted(NodeId);
        var result = await ResultAsync(harness);
        Assert.Equal("callbacks/{id}", result.GetProperty(nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal("GET", result.GetProperty(nameof(HttpRequestModel.Method)).GetString());
    }

    private static async Task<WorkflowExecutionRun> ResumeAsync(WorkflowExecutionHarness harness, JsonElement resumeInput)
    {
        // The mid-flow suspension created one GET bookmark keyed by the (template, method) hash.
        var bookmark = Assert.Single(await BookmarksAsync(harness));
        return await harness.ResumeAsync(
            pinnedExecutable: WorkflowExecutionHarness.Identity,
            bookmarkId: bookmark.BookmarkId,
            activityExecutionId: ActivityExecutionId,
            executableNodeId: NodeId,
            resumeTargetId: HttpEndpoint.ResumeTargetId,
            stimulusType: bookmark.StimulusType,
            stimulusHash: bookmark.StimulusHash,
            input: resumeInput);
    }

    private static async Task<JsonElement> ResultAsync(WorkflowExecutionHarness harness)
    {
        var value = await harness.Services.GetRequiredService<IDurableValueStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, $"durable-{ResultValueId}");
        Assert.NotNull(value);
        return value!.InlineValue!.Value;
    }

    private static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services))
            .Build("actexec-http-endpoint");

    private static WorkflowExecutable NewEndpointExecutable(
        string path,
        IReadOnlyCollection<string>? methods = null,
        bool? canStartWorkflow = null)
    {
        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["Path"] = LiteralBinding("Path", JsonSerializer.SerializeToElement(path), "System.String")
        };

        if (methods is not null)
            inputBindings["SupportedMethods"] = LiteralBinding("SupportedMethods", JsonSerializer.SerializeToElement(methods), "System.Collections.Generic.ICollection`1[[System.String]]");

        if (canStartWorkflow is { } canStart)
            inputBindings["CanStartWorkflow"] = LiteralBinding("CanStartWorkflow", JsonSerializer.SerializeToElement(canStart), "System.Boolean");

        var node = new ExecutableNode(
            executableNodeId: NodeId,
            authoredActivityId: "authored-http-endpoint",
            activityType: HttpEndpoint.ActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: ClrConstruction.Payload(Serializer, typeof(HttpEndpoint)),
            inputBindings: inputBindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>
            {
                ["Result"] = new(
                    outputName: "Result",
                    valueId: ResultValueId,
                    type: new RuntimeValueTypeDescriptor("clr", "System.Object", null),
                    lifecycle: DurableValueLifecycle.Instance,
                    storage: DurableValueStorage.Inline,
                    captureOnSuccessfulCompletion: true)
            },
            metadata: new Dictionary<string, string>());

        // The real compiler indexes the [ResumeTarget] handler into this map; the harness builds executables
        // directly, so declare the same entry the compiler emits for the HttpEndpoint node (mirrors Delay's test).
        var resumeTargets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
        {
            [HttpEndpoint.ResumeTargetId] = new(
                ResumeTargetId: HttpEndpoint.ResumeTargetId,
                ExecutableNodeId: NodeId,
                HandlerKey: "OnRequestReceived",
                Metadata: new Dictionary<string, string>())
        };

        var now = new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
        return new WorkflowExecutable(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: node,
            resumeTargets: resumeTargets,
            createdAt: now,
            publishedAt: now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static WorkflowExecutable NewEndpointExecutableWithOptions(string path, bool authorize, string policy, long sizeLimit)
    {
        var executable = NewEndpointExecutable(path, methods: ["GET"]);
        var node = executable.RootActivity;
        var inputBindings = new Dictionary<string, RuntimeInputBinding>(node.InputBindings)
        {
            ["Authorize"] = LiteralBinding("Authorize", JsonSerializer.SerializeToElement(authorize), "System.Boolean"),
            ["Policy"] = LiteralBinding("Policy", JsonSerializer.SerializeToElement(policy), "System.String"),
            ["RequestSizeLimit"] = LiteralBinding("RequestSizeLimit", JsonSerializer.SerializeToElement(sizeLimit), "System.Int64")
        };

        var withOptions = new ExecutableNode(
            executableNodeId: node.ExecutableNodeId,
            authoredActivityId: node.AuthoredActivityId,
            activityType: node.ActivityType,
            activityTypeVersion: node.ActivityTypeVersion,
            descriptorType: node.DescriptorType,
            descriptorPayload: node.DescriptorPayload,
            inputBindings: inputBindings,
            outputCaptures: node.OutputCaptures,
            metadata: node.Metadata);

        return new WorkflowExecutable(
            identity: executable.Identity,
            rootActivity: withOptions,
            resumeTargets: executable.ResumeTargets,
            createdAt: executable.CreatedAt,
            publishedAt: executable.PublishedAt,
            compatibilityMetadata: executable.CompatibilityMetadata);
    }

    private static RuntimeInputBinding LiteralBinding(string name, JsonElement value, string clrType) =>
        new(
            inputName: name,
            source: RuntimeInputBindingSource.Literal,
            literalValue: value,
            metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = clrType });

    private static async Task<IReadOnlyCollection<BookmarkState>> BookmarksAsync(WorkflowExecutionHarness harness) =>
        await harness.Services.GetRequiredService<IBookmarkStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);

    private static bool CompletedEndpoint(WorkflowExecutionRun run) =>
        run.ActivityStates.Any(s => s.Execution.ExecutableNodeId == NodeId && s.Status == ActivityExecutionStatus.Completed);

    private static IPayloadSerializer Serializer =>
        TestPayloadSerializers.NewPayloadSerializer();
}
