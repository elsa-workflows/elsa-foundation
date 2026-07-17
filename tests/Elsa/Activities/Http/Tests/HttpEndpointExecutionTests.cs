using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Services;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
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

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook", canStartWorkflow: true), JsonSerializer.SerializeToElement(liveRequest), triggerNodeId: NodeId);

        run.AssertCompleted(NodeId);
        run.AssertWorkflowCompleted();

        var result = await ResultAsync(harness);
        Assert.Equal("POST", RequestProperty(result, nameof(HttpRequestModel.Method)).GetString());
        Assert.Equal("""{"id":7}""", RequestProperty(result, nameof(HttpRequestModel.Body)).GetString());
        Assert.Equal("abc", RequestProperty(result, nameof(HttpRequestModel.Headers)).GetProperty("x-test")[0].GetString());
        Assert.Equal("1", RequestProperty(result, nameof(HttpRequestModel.Query)).GetProperty("q")[0].GetString());
    }

    [Fact]
    public async Task DerivesParsedContentOutput_FromJsonBody_ViaTheParserSeam()
    {
        // Spec 089 efficiency #9: ParsedContent is derived at the activity from Body + the Content-Type header,
        // not carried on the wire model. A JSON body surfaces as the parsed object on the ParsedContent output.
        await using var harness = NewHarness();
        var liveRequest = new HttpRequestModel(
            Path: "orders/webhook",
            Method: "POST",
            Headers: new Dictionary<string, string[]> { ["Content-Type"] = ["application/json"] },
            Query: new Dictionary<string, string[]>(),
            Body: """{"orderId":7}""");

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook", canStartWorkflow: true), JsonSerializer.SerializeToElement(liveRequest), triggerNodeId: NodeId);
        run.AssertWorkflowCompleted();

        var parsed = await ParsedContentAsync(harness);
        Assert.Equal(JsonValueKind.Object, parsed.ValueKind);
        Assert.Equal(7, parsed.GetProperty("orderId").GetInt32());
    }

    [Fact]
    public async Task DerivesParsedContent_ExplicitJsonNull_AsJsonNullElement_NotNoContent()
    {
        // Spec 089 #16: an explicit JSON `null` body is present content — it surfaces as a JsonElement of kind
        // Null, deliberately distinct from the CLR-null "no content" case (empty body) below.
        await using var harness = NewHarness();
        var liveRequest = new HttpRequestModel(
            Path: "orders/webhook",
            Method: "POST",
            Headers: new Dictionary<string, string[]> { ["Content-Type"] = ["application/json"] },
            Query: new Dictionary<string, string[]>(),
            Body: "null");

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook", canStartWorkflow: true), JsonSerializer.SerializeToElement(liveRequest), triggerNodeId: NodeId);
        run.AssertWorkflowCompleted();

        var parsed = await ParsedContentAsync(harness);
        Assert.Equal(JsonValueKind.Null, parsed.ValueKind);
    }

    [Fact]
    public async Task DerivesParsedContent_EmptyBody_AsNoContent()
    {
        // Spec 089 #16: an empty body is "no content" → the activity sets CLR null on the ParsedContent output.
        await using var harness = NewHarness();
        var liveRequest = new HttpRequestModel(
            Path: "orders/webhook",
            Method: "POST",
            Headers: new Dictionary<string, string[]> { ["Content-Type"] = ["application/json"] },
            Query: new Dictionary<string, string[]>(),
            Body: null);

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook", canStartWorkflow: true), JsonSerializer.SerializeToElement(liveRequest), triggerNodeId: NodeId);
        run.AssertWorkflowCompleted();

        var parsed = await ParsedContentAsync(harness);
        Assert.Equal(JsonValueKind.Null, parsed.ValueKind);
    }

    [Fact]
    public async Task FallsBackToAuthoredRoute_WhenNoStimulusInputIsSeeded()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(NewEndpointExecutable("Orders/Webhook/", canStartWorkflow: true));

        run.AssertWorkflowCompleted();

        var result = await ResultAsync(harness);
        Assert.Equal("orders/webhook", RequestProperty(result, nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal(JsonValueKind.Null, RequestProperty(result, nameof(HttpRequestModel.Body)).ValueKind);
    }

    [Theory]
    [InlineData("""{"foo":1}""")] // foreign JSON object — deserializes but has no Path/Method
    [InlineData("""{"Path":"x"}""")] // half-shaped object — Method missing
    public async Task FallsBackToAuthoredRoute_WhenTargetedPayloadDeserializesWithoutRequestIdentity(string foreignPayload)
    {
        // The wire model is forward-compatible and therefore permits missing members during deserialization.
        // HttpEndpoint applies its own Path/Method identity validation before accepting that model.
        await using var harness = NewHarness();
        using var document = JsonDocument.Parse(foreignPayload);

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook", canStartWorkflow: true), document.RootElement.Clone(), triggerNodeId: NodeId);

        run.AssertWorkflowCompleted();

        var result = await ResultAsync(harness);
        Assert.Equal("orders/webhook", RequestProperty(result, nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal("*", RequestProperty(result, nameof(HttpRequestModel.Method)).GetString());
    }

    [Fact]
    public async Task TargetedMalformedStimulus_FaultsInsteadOfFallingBackToDirectInvocation()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(
            NewEndpointExecutable("orders/webhook", canStartWorkflow: true),
            JsonSerializer.SerializeToElement("not-a-request-model"),
            triggerNodeId: NodeId);

        var endpoint = run.State(NodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, endpoint.Status);
        Assert.NotEmpty(endpoint.IncidentIds);
        Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
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

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook", canStartWorkflow: true), inputs);

        run.AssertWorkflowCompleted();

        var result = await ResultAsync(harness);
        Assert.Equal("orders/webhook", RequestProperty(result, nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal("*", RequestProperty(result, nameof(HttpRequestModel.Method)).GetString());
    }

    [Fact]
    public async Task StartPath_CompletesWithStimulus_WhenTriggerNodeIdMatchesOwnNode()
    {
        // D-D1 start path: the run carries this node's id as the trigger identity → the endpoint completes with the
        // live request from the stimulus channel (it does NOT suspend).
        await using var harness = NewHarness();
        var liveRequest = new HttpRequestModel("orders/webhook", "POST", new Dictionary<string, string[]>(), new Dictionary<string, string[]>(), """{"id":9}""");

        var run = await harness.RunAsync(NewEndpointExecutable("orders/webhook", canStartWorkflow: true), JsonSerializer.SerializeToElement(liveRequest), triggerNodeId: NodeId);

        run.AssertCompleted(NodeId);
        run.AssertWorkflowCompleted();
        Assert.Empty(await BookmarksAsync(harness));

        var result = await ResultAsync(harness);
        Assert.Equal("POST", RequestProperty(result, nameof(HttpRequestModel.Method)).GetString());
        Assert.Equal("""{"id":9}""", RequestProperty(result, nameof(HttpRequestModel.Body)).GetString());
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
            NewEndpointExecutable("callbacks/{id}", methods: ["GET", "POST"], canStartWorkflow: null),
            stimulus,
            triggerNodeId: "some-other-node");

        Assert.False(CompletedEndpoint(run));
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);

        var bookmarks = await BookmarksAsync(harness);
        Assert.Equal(2, bookmarks.Count);
        foreach (var method in new[] { "get", "post" })
        {
            var bookmark = Assert.Single(bookmarks, b => b.Metadata[HttpEndpointRouting.MethodMetadataKey] == method);
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
    public async Task MidFlow_StampsResponseModeSync_OnBookmarkMetadata()
    {
        // Spec 089 E-D1: an authored ResponseMode = Sync rides the bookmark metadata for free — the activity's
        // ReadOptions folds it into HttpEndpointStimulus.Describe, exactly as for a trigger binding — so the
        // middleware reads the mode off a resume-only match (scenario 5.5).
        await using var harness = NewHarness();
        var node = NewEndpointExecutableWithResponseMode("callbacks/{id}", ResponseMode.Sync);

        var run = await harness.RunAsync(node, JsonSerializer.SerializeToElement("x"), triggerNodeId: "other");

        Assert.False(CompletedEndpoint(run));
        var bookmark = Assert.Single(await BookmarksAsync(harness));
        Assert.Equal("Sync", bookmark.Metadata[HttpEndpointRouting.ResponseModeMetadataKey]);
    }

    [Fact]
    public async Task MidFlow_ResponseModeAsync_IsOmittedFromBookmarkMetadata()
    {
        await using var harness = NewHarness();
        var node = NewEndpointExecutableWithResponseMode("callbacks/{id}", ResponseMode.Async);

        var run = await harness.RunAsync(node, JsonSerializer.SerializeToElement("x"), triggerNodeId: "other");

        Assert.False(CompletedEndpoint(run));
        var bookmark = Assert.Single(await BookmarksAsync(harness));
        Assert.DoesNotContain(HttpEndpointRouting.ResponseModeMetadataKey, bookmark.Metadata.Keys);
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
        Assert.Equal("orders/webhook", RequestProperty(result, nameof(HttpRequestModel.Path)).GetString());
    }

    [Fact]
    public async Task DirectRun_DoesNotExposeAnAmbientStimulusPayload()
    {
        await using var harness = NewHarness();
        var ambient = new HttpRequestModel(
            "foreign/request", "DELETE", new Dictionary<string, string[]>(), new Dictionary<string, string[]>(), "foreign");

        var run = await harness.RunAsync(
            NewEndpointExecutable("orders/webhook", canStartWorkflow: true),
            JsonSerializer.SerializeToElement(ambient));

        run.AssertWorkflowCompleted();
        var result = await ResultAsync(harness);
        Assert.Equal("orders/webhook", RequestProperty(result, nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal("*", RequestProperty(result, nameof(HttpRequestModel.Method)).GetString());
    }

    [Fact]
    public async Task DirectRun_UnauthoredCanStart_AlwaysSuspends()
    {
        await using var harness = NewHarness();

        var run = await harness.RunAsync(NewEndpointExecutable("callbacks/{id}", methods: ["GET"], canStartWorkflow: null));

        Assert.False(CompletedEndpoint(run));
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        Assert.Single(await BookmarksAsync(harness));
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
        Assert.Contains(":trigger:", bookmark.BookmarkId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resume_SetsResultRouteDataAndParsedContent_FromTheResumeInput()
    {
        // T008 (D-D3): suspend at a mid-flow endpoint, then resume it with a matching request on the resume-input
        // channel — the [ResumeTarget] reads the request and sets Result/RouteData/ParsedContent.
        await using var harness = NewHarness();
        var mid = await harness.RunAsync(NewEndpointExecutable("callbacks/{id}", methods: ["GET"], canStartWorkflow: null), JsonSerializer.SerializeToElement("x"), triggerNodeId: "other");
        Assert.False(CompletedEndpoint(mid));

        // ParsedContent is derived at the activity from Body + the Content-Type header (spec 089 #9), not carried
        // on the wire model — so the resume request carries the JSON body and content type, and the derived value
        // is asserted off the ParsedContent output.
        var resumeRequest = new HttpRequestModel(
            Path: "callbacks/42",
            Method: "GET",
            Headers: new Dictionary<string, string[]> { ["Content-Type"] = ["application/json"] },
            Query: new Dictionary<string, string[]>(),
            Body: """{"ok":true}""",
            RouteData: new Dictionary<string, string> { ["id"] = "42" });

        var run = await ResumeAsync(harness, JsonSerializer.SerializeToElement(resumeRequest));

        run.AssertCompleted(NodeId);
        run.AssertWorkflowCompleted();
        Assert.Empty(await BookmarksAsync(harness));

        var result = await ResultAsync(harness);
        Assert.Equal("callbacks/42", RequestProperty(result, nameof(HttpRequestModel.Path)).GetString());
        Assert.Equal("GET", RequestProperty(result, nameof(HttpRequestModel.Method)).GetString());
        Assert.Equal("42", RequestProperty(result, nameof(HttpRequestModel.RouteData)).GetProperty("id").GetString());

        var parsed = await ParsedContentAsync(harness);
        Assert.True(parsed.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Resume_FallsBackToAuthoredRoute_WhenResumeInputIsMalformed()
    {
        // D-D7: the resume input is produced by our own middleware, so a missing/malformed payload is a defensive
        // path — the resume target falls back to the authored-route model rather than faulting.
        await using var harness = NewHarness();
        var mid = await harness.RunAsync(NewEndpointExecutable("callbacks/{id}", methods: ["GET"], canStartWorkflow: null), JsonSerializer.SerializeToElement("x"), triggerNodeId: "other");
        Assert.False(CompletedEndpoint(mid));

        // A foreign JSON object (no Path/Method) is not a request model → fallback.
        var run = await ResumeAsync(harness, JsonSerializer.SerializeToElement(new { foo = 1 }));

        run.AssertCompleted(NodeId);
        var result = await ResultAsync(harness);
        Assert.Equal("callbacks/{id}", RequestProperty(result, nameof(HttpRequestModel.Path)).GetString());
        // The authored-route fallback emits the stable non-routing "*" placeholder for Method (#592 item 20) — no
        // inbound request drove this resume, so there is no real request method to project.
        Assert.Equal("*", RequestProperty(result, nameof(HttpRequestModel.Method)).GetString());
    }

    private static async Task<WorkflowExecutionRun> ResumeAsync(WorkflowExecutionHarness harness, JsonElement resumeInput)
    {
        // The mid-flow suspension created one GET bookmark keyed by the (template, method) hash.
        var bookmark = Assert.Single(await BookmarksAsync(harness));
        var state = await harness.Services.GetRequiredService<IActivityExecutionStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, ActivityExecutionId);
        var registration = Assert.Single(state!.TriggerRegistrations!, candidate => candidate.RegistrationId == bookmark.BookmarkId);
        var delivery = new RuntimeTypedTriggerDeliveryMetadata(
            deliveryId: $"delivery:{bookmark.BookmarkId}",
            payloadType: registration.PayloadType,
            providerId: "test.http-endpoint",
            receivedAt: new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero),
            deduplicationKey: $"dedupe:{bookmark.BookmarkId}");
        return await harness.ResumeAsync(
            pinnedExecutable: WorkflowExecutionHarness.Identity,
            bookmarkId: bookmark.BookmarkId,
            activityExecutionId: ActivityExecutionId,
            executableNodeId: NodeId,
            resumeTargetId: HttpEndpoint.ResumeTargetId,
            stimulusType: bookmark.StimulusType,
            stimulusHash: bookmark.StimulusHash,
            input: resumeInput,
            triggerDelivery: delivery);
    }

    private static Task<JsonElement> ResultAsync(WorkflowExecutionHarness harness) => CompletionProjectionAsync(harness, "Request");

    private static Task<JsonElement> ParsedContentAsync(WorkflowExecutionHarness harness) => CompletionProjectionAsync(harness, "ParsedContent");

    private static JsonElement RequestProperty(JsonElement request, string clrPropertyName) =>
        request.TryGetProperty(clrPropertyName, out var value)
            ? value
            : request.GetProperty(JsonNamingPolicy.CamelCase.ConvertName(clrPropertyName));

    private static async Task<JsonElement> CompletionProjectionAsync(WorkflowExecutionHarness harness, string propertyName)
    {
        var state = await harness.Services.GetRequiredService<IActivityExecutionStateStore>()
            .FindAsync(WorkflowExecutionHarness.WorkflowExecutionId, ActivityExecutionId);
        var result = state?.Completion?.Result.InlineValue;
        Assert.NotNull(result);
        return RequestProperty(result.Value, propertyName);
    }

    private static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services))
            // The HttpEndpoint activity derives ParsedContent from the raw body via IHttpRequestBodyParser (spec
            // 089 efficiency #9). Full CShells composition registers it through ActivitiesHttp's DependsOn "Http";
            // this manual harness composes only the named feature, so register the seam it consumes directly.
            .WithFeature(services => services.AddSingleton<IHttpRequestBodyParser, HttpRequestBodyParser>())
            .Build("actexec-http-endpoint");

    private static WorkflowExecutable NewEndpointExecutable(
        string path,
        IReadOnlyCollection<string>? methods = null,
        bool? canStartWorkflow = null)
    {
        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(HttpEndpoint.Path)] = TypedLiteralBinding(nameof(HttpEndpoint.Path), path, typeof(string)),
            [nameof(HttpEndpoint.SupportedMethods)] = TypedLiteralBinding(nameof(HttpEndpoint.SupportedMethods), methods, typeof(ICollection<string>)),
            [nameof(HttpEndpoint.CanStartWorkflow)] = TypedLiteralBinding(nameof(HttpEndpoint.CanStartWorkflow), canStartWorkflow ?? false, typeof(bool)),
            [nameof(HttpEndpoint.Authorize)] = TypedLiteralBinding(nameof(HttpEndpoint.Authorize), false, typeof(bool)),
            [nameof(HttpEndpoint.Policy)] = TypedLiteralBinding(nameof(HttpEndpoint.Policy), null, typeof(string)),
            [nameof(HttpEndpoint.RequestTimeout)] = TypedLiteralBinding(nameof(HttpEndpoint.RequestTimeout), null, typeof(TimeSpan)),
            [nameof(HttpEndpoint.RequestSizeLimit)] = TypedLiteralBinding(nameof(HttpEndpoint.RequestSizeLimit), null, typeof(long)),
            [nameof(HttpEndpoint.ResponseMode)] = TypedLiteralBinding(nameof(HttpEndpoint.ResponseMode), ResponseMode.Async, typeof(ResponseMode))
        };

        var descriptorPayload = ClrConstruction.Payload(Serializer, typeof(HttpEndpoint));
        var contract = EndpointContract(descriptorPayload);

        var node = new ExecutableNode(
            executableNodeId: NodeId,
            authoredActivityId: "authored-http-endpoint",
            activityType: HttpEndpoint.ActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: ClrConstruction.DescriptorType,
            descriptorPayload: descriptorPayload,
            inputBindings: inputBindings,
            metadata: new Dictionary<string, string>(),
            activityContract: contract);

        // The real compiler indexes the [ResumeTarget] handler into this map; the harness builds executables
        // directly, so declare the same entry the compiler emits for the HttpEndpoint node (mirrors Delay's test).
        var resumeTargets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
        {
            [HttpEndpoint.ResumeTargetId] = new(
                ResumeTargetId: HttpEndpoint.ResumeTargetId,
                ExecutableNodeId: NodeId,
                HandlerKey: "ResumeAsync",
                Metadata: new Dictionary<string, string>())
        };

        var now = new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
        return new WorkflowExecutable(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: node,
            resumeTargets: resumeTargets,
            createdAt: now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static WorkflowExecutable NewEndpointExecutableWithOptions(string path, bool authorize, string policy, long sizeLimit)
    {
        var executable = NewEndpointExecutable(path, methods: ["GET"], canStartWorkflow: null);
        var node = executable.RootActivity;
        var inputBindings = new Dictionary<string, RuntimeInputBinding>(node.InputBindings)
        {
            [nameof(HttpEndpoint.Authorize)] = TypedLiteralBinding(nameof(HttpEndpoint.Authorize), authorize, typeof(bool)),
            [nameof(HttpEndpoint.Policy)] = TypedLiteralBinding(nameof(HttpEndpoint.Policy), policy, typeof(string)),
            [nameof(HttpEndpoint.RequestSizeLimit)] = TypedLiteralBinding(nameof(HttpEndpoint.RequestSizeLimit), sizeLimit, typeof(long))
        };

        var withOptions = new ExecutableNode(
            executableNodeId: node.ExecutableNodeId,
            authoredActivityId: node.AuthoredActivityId,
            activityType: node.ActivityType,
            activityTypeVersion: node.ActivityTypeVersion,
            descriptor: node.Descriptor,
            inputBindings: inputBindings,
            metadata: node.Metadata,
            activityContract: node.ActivityContract);

        return new WorkflowExecutable(
            identity: executable.Identity,
            rootActivity: withOptions,
            resumeTargets: executable.ResumeTargets,
            createdAt: executable.CreatedAt,
            compatibilityMetadata: executable.CompatibilityMetadata);
    }

    private static WorkflowExecutable NewEndpointExecutableWithResponseMode(string path, ResponseMode mode)
    {
        var executable = NewEndpointExecutable(path, methods: ["GET"], canStartWorkflow: null);
        var node = executable.RootActivity;
        var inputBindings = new Dictionary<string, RuntimeInputBinding>(node.InputBindings)
        {
            // The runtime input materializer deserializes the literal against the enum type via System.Text.Json;
            // author the underlying numeric value so it materializes regardless of enum-converter registration.
            [nameof(HttpEndpoint.ResponseMode)] = TypedLiteralBinding(nameof(HttpEndpoint.ResponseMode), mode, typeof(ResponseMode))
        };

        var withMode = new ExecutableNode(
            executableNodeId: node.ExecutableNodeId,
            authoredActivityId: node.AuthoredActivityId,
            activityType: node.ActivityType,
            activityTypeVersion: node.ActivityTypeVersion,
            descriptor: node.Descriptor,
            inputBindings: inputBindings,
            metadata: node.Metadata,
            activityContract: node.ActivityContract);

        return new WorkflowExecutable(
            identity: executable.Identity,
            rootActivity: withMode,
            resumeTargets: executable.ResumeTargets,
            createdAt: executable.CreatedAt,
            compatibilityMetadata: executable.CompatibilityMetadata);
    }

    private static ActivityContract EndpointContract(JsonElement descriptorPayload) =>
        new(
            HttpEndpoint.ActivityType,
            "1.0.0",
            ClrConstruction.DescriptorType,
            descriptorPayload,
            new[]
            {
                InputContract(nameof(HttpEndpoint.Path), typeof(string), isRequired: true),
                InputContract(nameof(HttpEndpoint.SupportedMethods), typeof(ICollection<string>)),
                InputContract(nameof(HttpEndpoint.CanStartWorkflow), typeof(bool)),
                InputContract(nameof(HttpEndpoint.Authorize), typeof(bool)),
                InputContract(nameof(HttpEndpoint.Policy), typeof(string)),
                InputContract(nameof(HttpEndpoint.RequestTimeout), typeof(TimeSpan)),
                InputContract(nameof(HttpEndpoint.RequestSizeLimit), typeof(long)),
                InputContract(nameof(HttpEndpoint.ResponseMode), typeof(ResponseMode))
            },
            new ActivityResultContract(
                ValueType(typeof(HttpEndpointResult)),
                isRequired: true,
                ActivityValuePolicy.Default,
                new[]
                {
                    Projection("Request", "request", typeof(HttpRequestModel), isRequired: true),
                    Projection("RouteData", "routeData", typeof(IReadOnlyDictionary<string, string>), isRequired: true),
                    Projection("ParsedContent", "parsedContent", typeof(JsonElement), isRequired: false)
                }),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement(ClrConstruction.DescriptorType, TypeAliasConvention.CanonicalAlias(typeof(HttpEndpoint))));

    private static ActivityInputContract InputContract(string key, Type type, bool isRequired = false) =>
        new(key, key, ValueType(type), isRequired, hasDefault: false, defaultValue: null, ActivityValuePolicy.Default);

    private static ActivityResultProjectionContract Projection(string key, string path, Type type, bool isRequired) =>
        new(key, path, ValueType(type), isRequired, ActivityValuePolicy.Default);

    private static RuntimeInputBinding TypedLiteralBinding(string inputName, object? value, Type type)
    {
        var valueType = ValueType(type);
        return new RuntimeInputBinding(
            inputKey: inputName,
            targetType: valueType,
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Literal,
            literal: value is null
                ? ValueEnvelope.Null(valueType, ValueProtectionPolicy.InstanceInline)
                : ValueEnvelope.Inline(
                    valueType,
                    JsonSerializer.SerializeToElement(value, type),
                    ValueProtectionPolicy.InstanceInline));
    }

    private static ValueTypeDescriptor ValueType(Type type) =>
        TypeReferenceFactory.FromClrType(type, TypeAliasConvention.CanonicalAlias) is { } reference
            ? new ValueTypeDescriptor(reference.Alias, reference.CollectionKind)
            : throw new InvalidOperationException($"Could not describe '{type}'.");

    private static RuntimeValueTypeDescriptor RuntimeValueType(Type type)
    {
        var valueType = ValueType(type);
        return new RuntimeValueTypeDescriptor("clr", valueType.Alias, null);
    }

    private static async Task<IReadOnlyCollection<BookmarkState>> BookmarksAsync(WorkflowExecutionHarness harness) =>
        await harness.Services.GetRequiredService<IBookmarkStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);

    private static bool CompletedEndpoint(WorkflowExecutionRun run) =>
        run.ActivityStates.Any(s => s.Execution.ExecutableNodeId == NodeId && s.Status == ActivityExecutionStatus.Completed);

    private static IPayloadSerializer Serializer =>
        TestPayloadSerializers.NewPayloadSerializer();
}
