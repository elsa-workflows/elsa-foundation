using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class StimulusRouterTests
{
    private const string StimulusType = "Event";
    private const string StimulusHash = "sha256:event:hello";
    private readonly DateTimeOffset _now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Route_StartOnly_StartsOneInstancePerMatchingTrigger()
    {
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        await bindingStore.SaveAsync(Binding("artifact-2", "node-a"));
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        var result = await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly));

        Assert.Equal(2, result.StartedCount);
        Assert.Empty(result.Resumes);
        Assert.Equal(["artifact-1", "artifact-2"], startDispatcher.Requests.Select(r => r.ArtifactId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Route_ResumeOnly_FansInToEveryWaitingInstanceAcrossExecutions()
    {
        // Bookmarks are seeded directly into the store (not produced by running published workflows) as a
        // test simplification: W7's Event trigger ships start-only by design, so this suite has no published
        // suspending activity of its own to drive. (Published workflows CAN now suspend/resume — W8's
        // WorkflowExecutableCompiler.BuildResumeTargets closed that gap — but wiring a full compile→publish→
        // suspend cycle here would only add setup, not coverage.) Seeding keeps the focus on the REAL router →
        // GlobalBookmarkStimulusLookup → BookmarkResumeDispatcher → agent fan-in path end-to-end (E3-5).
        var bookmarkStore = new InMemoryBookmarkStateStore();
        await bookmarkStore.SaveAsync(Bookmark("bk-1", "wfexec-1"));
        await bookmarkStore.SaveAsync(Bookmark("bk-2", "wfexec-2"));
        await bookmarkStore.SaveAsync(Bookmark("bk-3", "wfexec-3"));
        var resumeDispatcher = new RecordingResumeDispatcher();
        var router = Router(new InMemoryWorkflowTriggerBindingStore(), bookmarkStore, new RecordingStartDispatcher(), resumeDispatcher);

        var result = await router.RouteAsync(Request(mode: StimulusRoutingMode.ResumeOnly));

        Assert.Equal(3, result.ResumedCount);
        Assert.Empty(result.Starts);
        Assert.Equal(
            ["wfexec-1", "wfexec-2", "wfexec-3"],
            resumeDispatcher.Requests.Select(r => r.WorkflowExecutionId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Route_StartAndResume_DoesBoth()
    {
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var bookmarkStore = new InMemoryBookmarkStateStore();
        await bookmarkStore.SaveAsync(Bookmark("bk-1", "wfexec-1"));
        var router = Router(bindingStore, bookmarkStore, new RecordingStartDispatcher(), new RecordingResumeDispatcher());

        var result = await router.RouteAsync(Request());

        Assert.Equal(1, result.StartedCount);
        Assert.Equal(1, result.ResumedCount);
    }

    [Fact]
    public async Task Route_WithIdempotencyKey_DoesNotDoubleStartOnRedelivery()
    {
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var startDispatcher = new RecordingStartDispatcher();
        var deduplicator = new InMemoryStimulusStartDeduplicator();
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher(), deduplicator);

        var first = await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly, idempotencyKey: "delivery-1"));
        var second = await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly, idempotencyKey: "delivery-1"));

        Assert.Equal(1, first.StartedCount);
        Assert.Equal(0, second.StartedCount);
        Assert.Equal(1, second.SkippedStartCount);
        Assert.Single(startDispatcher.Requests);
    }

    [Fact]
    public async Task Route_SnapshotsResumeSetBeforeStarting_SoFreshBookmarksAreNotImmediatelyResumed()
    {
        // A published trigger AND a waiting instance both match. Starting the new instance synchronously runs it to
        // its own fresh bookmark; the router must not resume that fresh bookmark with the same stimulus.
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var bookmarkStore = new InMemoryBookmarkStateStore();
        await bookmarkStore.SaveAsync(Bookmark("bk-pre", "wfexec-pre"));
        var resumeDispatcher = new RecordingResumeDispatcher();
        // The start dispatcher simulates the just-started instance reaching its first bookmark synchronously.
        var startDispatcher = new RecordingStartDispatcher(onStart: executionId =>
            bookmarkStore.SaveAsync(Bookmark($"bk-{executionId}", executionId)).AsTask().GetAwaiter().GetResult());
        var router = Router(bindingStore, bookmarkStore, startDispatcher, resumeDispatcher);

        var result = await router.RouteAsync(Request());

        Assert.Equal(1, result.StartedCount);
        var resumed = Assert.Single(resumeDispatcher.Requests);
        Assert.Equal("wfexec-pre", resumed.WorkflowExecutionId);
    }

    [Fact]
    public async Task Route_StartOnly_ForwardsStimulusInputOnTheDedicatedChannel()
    {
        // Spec 089 FR-001: the start path delivers the stimulus payload on the first-class StimulusInput field
        // (the start-side counterpart of the resume path's Input) — never through the workflow-inputs bag, so
        // it can neither collide with author-declared inputs nor be forged via caller-facing input maps.
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());
        var input = JsonSerializer.SerializeToElement(new { id = 7 });

        await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly, input: input));

        var dispatched = Assert.Single(startDispatcher.Requests);
        Assert.NotNull(dispatched.StimulusInput);
        Assert.Equal(7, dispatched.StimulusInput!.Value.GetProperty("id").GetInt32());
        Assert.Empty(dispatched.Inputs);
    }

    [Fact]
    public async Task Route_StartOnly_ForwardsMatchedBindingNodeIdPerBinding()
    {
        // Spec 089 D (T003): each start dispatch carries the matched binding's executable node id on the dedicated
        // TriggerNodeId channel, so a mid-flow-capable activity can tell whether it is the node that triggered the
        // run. Two bindings on distinct nodes must each forward their own node id — never the workflow-inputs bag.
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        await bindingStore.SaveAsync(Binding("artifact-2", "node-b"));
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly));

        Assert.Equal(2, startDispatcher.Requests.Count);
        Assert.Equal("node-a", startDispatcher.Requests.Single(request => request.ArtifactId == "artifact-1").TriggerNodeId);
        Assert.Equal("node-b", startDispatcher.Requests.Single(request => request.ArtifactId == "artifact-2").TriggerNodeId);
    }

    [Fact]
    public async Task Route_StartOnly_WithoutInput_CarriesNoStimulusInput()
    {
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly));

        var dispatched = Assert.Single(startDispatcher.Requests);
        Assert.Null(dispatched.StimulusInput);
        Assert.Empty(dispatched.Inputs);
    }

    [Fact]
    public async Task Route_ResumeOnly_StillForwardsStimulusInputToResumeDispatch()
    {
        // Regression pin: the resume path's input delivery predates spec 089 and must stay unchanged.
        var bookmarkStore = new InMemoryBookmarkStateStore();
        await bookmarkStore.SaveAsync(Bookmark("bk-1", "wfexec-1"));
        var resumeDispatcher = new RecordingResumeDispatcher();
        var router = Router(new InMemoryWorkflowTriggerBindingStore(), bookmarkStore, new RecordingStartDispatcher(), resumeDispatcher);
        var input = JsonSerializer.SerializeToElement(new { id = 9 });

        await router.RouteAsync(Request(mode: StimulusRoutingMode.ResumeOnly, input: input));

        var resumed = Assert.Single(resumeDispatcher.Requests);
        Assert.NotNull(resumed.Input);
        Assert.Equal(9, resumed.Input!.Value.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Route_ResumeFanIn_IsScopedByCorrelation()
    {
        var bookmarkStore = new InMemoryBookmarkStateStore();
        await bookmarkStore.SaveAsync(Bookmark("bk-1", "wfexec-1", correlationId: "order-7"));
        await bookmarkStore.SaveAsync(Bookmark("bk-2", "wfexec-2", correlationId: "order-9"));
        var resumeDispatcher = new RecordingResumeDispatcher();
        var router = Router(new InMemoryWorkflowTriggerBindingStore(), bookmarkStore, new RecordingStartDispatcher(), resumeDispatcher);

        var result = await router.RouteAsync(Request(mode: StimulusRoutingMode.ResumeOnly, correlationId: "order-7"));

        Assert.Equal(1, result.ResumedCount);
        Assert.Equal("wfexec-1", Assert.Single(resumeDispatcher.Requests).WorkflowExecutionId);
    }

    [Fact]
    public async Task Route_StartOnly_WithMatchedBindingsSupplied_ReusesThemAndNeverQueriesTheStore()
    {
        // Spec 089 efficiency #7: when the caller already fetched the match set (the HTTP middleware does, for its
        // ambiguity guard + options), the router reuses it instead of issuing its own identical ListByStimulusAsync
        // — one durable read per request, not two. The throwing store proves the router never consults it.
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(new ThrowingTriggerBindingStore(), new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());
        var prefetched = new[] { Binding("artifact-1", "node-a"), Binding("artifact-2", "node-a") };

        var result = await router.RouteAsync(new StimulusDispatchRequest(
            StimulusType, StimulusHash, mode: StimulusRoutingMode.StartOnly, matchedTriggerBindings: prefetched));

        Assert.Equal(2, result.StartedCount);
        Assert.Equal(2, startDispatcher.Requests.Count);
    }

    /// <summary>A binding store whose cross-artifact lookup throws — proves the router used the supplied match set.</summary>
    private sealed class ThrowingTriggerBindingStore : IWorkflowTriggerBindingStore
    {
        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByStimulusAsync(string stimulusType, string stimulusHash, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The router must reuse the supplied match set, not query the store.");
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> ListByStimulusTypeAsync(string stimulusType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private StimulusRouter Router(
        IWorkflowTriggerBindingStore bindingStore,
        InMemoryBookmarkStateStore bookmarkStore,
        RecordingStartDispatcher startDispatcher,
        RecordingResumeDispatcher resumeDispatcher,
        IStimulusStartDeduplicator? deduplicator = null) =>
        new(
            bindingStore,
            new GlobalBookmarkStimulusLookup(bookmarkStore),
            startDispatcher,
            resumeDispatcher,
            deduplicator ?? new InMemoryStimulusStartDeduplicator(),
            new FixedTimeProvider(_now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static StimulusDispatchRequest Request(
        StimulusRoutingMode mode = StimulusRoutingMode.StartAndResume,
        string? correlationId = null,
        string? idempotencyKey = null,
        JsonElement? input = null) =>
        new(StimulusType, StimulusHash, input: input, mode: mode, correlationId: correlationId, idempotencyKey: idempotencyKey);

    private WorkflowTriggerBinding Binding(string artifactId, string nodeId) =>
        new(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(artifactId, nodeId, StimulusHash),
            ArtifactId: artifactId,
            DefinitionId: "definition-1",
            ArtifactVersion: "1.0.0",
            ArtifactHash: "sha256:artifact",
            ExecutableNodeId: nodeId,
            StimulusType: StimulusType,
            StimulusHash: StimulusHash,
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: _now);

    private BookmarkState Bookmark(string bookmarkId, string executionId, string? correlationId = null)
    {
        var metadata = new Dictionary<string, string>();
        if (correlationId is not null)
            metadata[Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.CorrelationId] = correlationId;

        return new BookmarkState(
            BookmarkId: bookmarkId,
            WorkflowExecutionId: executionId,
            ActivityExecutionId: $"actexec-{bookmarkId}",
            ExecutableNodeId: "node-wait",
            ResumeTargetId: "resume-target:event",
            StimulusType: StimulusType,
            StimulusHash: StimulusHash,
            Payload: null,
            Metadata: metadata,
            CreatedAt: _now.AddMinutes(-1),
            ExpiresAt: null);
    }

    private sealed class RecordingStartDispatcher(Action<string>? onStart = null) : IWorkflowStartDispatcher
    {
        private int _counter;
        public List<WorkflowExecutionStartDispatchRequest> Requests { get; } = [];

        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request, WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var executionId = $"wfexec-new-{++_counter}";
            onStart?.Invoke(executionId);
            return new ValueTask<WorkflowExecutionStartDispatchResult>(Result(request.ArtifactId, executionId));
        }

        private static WorkflowExecutionStartDispatchResult Result(string artifactId, string executionId) =>
            new(
                executionId,
                new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:artifact"),
                new WorkflowExecutionCommandDispatchResult(
                    envelopeId: $"envelope-{executionId}",
                    workflowExecutionId: executionId,
                    status: WorkflowExecutionCommandDispatchStatus.Accepted,
                    recordedAt: DateTimeOffset.UnixEpoch),
                new WorkflowExecutionActorDescriptor(
                    workflowExecutionId: executionId,
                    agentId: $"agent-{executionId}",
                    providerName: "recording",
                    status: WorkflowExecutionActorStatus.Active,
                    capabilities: WorkflowExecutionActorCapabilities.InProcessMailbox,
                    activatedAt: DateTimeOffset.UnixEpoch));
    }

    private sealed class RecordingResumeDispatcher : IBookmarkResumeDispatcher
    {
        public List<BookmarkResumeDispatchRequest> Requests { get; } = [];

        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var bookmark = new BookmarkState(
                BookmarkId: $"bk-{request.WorkflowExecutionId}",
                WorkflowExecutionId: request.WorkflowExecutionId,
                ActivityExecutionId: "actexec-1",
                ExecutableNodeId: "node-wait",
                ResumeTargetId: "resume-target:event",
                StimulusType: request.StimulusType,
                StimulusHash: request.StimulusHash,
                Payload: null,
                Metadata: new Dictionary<string, string>(),
                CreatedAt: DateTimeOffset.UnixEpoch,
                ExpiresAt: null);
            var resumeTarget = new WorkflowExecutableResumeTarget("resume-target:event", "node-wait", "event-handler", new Dictionary<string, string>());
            var result = new BookmarkResumeDispatchResult(
                status: BookmarkResumeDispatchStatus.Dispatched,
                workflowExecutionId: request.WorkflowExecutionId,
                bookmark: bookmark,
                resolution: new BookmarkResumeResolution(bookmark, NewNode(), resumeTarget, request.Input),
                commandDispatch: new WorkflowExecutionCommandDispatchResult(
                    envelopeId: $"envelope-{request.WorkflowExecutionId}",
                    workflowExecutionId: request.WorkflowExecutionId,
                    status: WorkflowExecutionCommandDispatchStatus.Accepted,
                    recordedAt: DateTimeOffset.UnixEpoch),
                agent: new WorkflowExecutionActorDescriptor(
                    workflowExecutionId: request.WorkflowExecutionId,
                    agentId: $"agent-{request.WorkflowExecutionId}",
                    providerName: "recording",
                    status: WorkflowExecutionActorStatus.Active,
                    capabilities: WorkflowExecutionActorCapabilities.InProcessMailbox,
                    activatedAt: DateTimeOffset.UnixEpoch));
            return new ValueTask<BookmarkResumeDispatchResult>(result);
        }

        private static ExecutableNode NewNode()
        {
            using var document = JsonDocument.Parse("""{"type":"test"}""");
            return new ExecutableNode(
                executableNodeId: "node-wait",
                authoredActivityId: "authored-node-wait",
                activityType: "Elsa.Event",
                activityTypeVersion: "1.0.0",
                descriptorType: "test",
                descriptorPayload: document.RootElement.Clone(),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
                metadata: new Dictionary<string, string>());
        }
    }
}
