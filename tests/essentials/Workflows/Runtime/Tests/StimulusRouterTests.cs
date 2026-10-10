using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;
using Microsoft.Extensions.Time.Testing;

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
    public async Task Route_StartOnly_ClassifiesPublishedTriggerStartsAsPublishedRuns()
    {
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly));

        Assert.Equal(WorkflowRunKind.PublishedRun, Assert.Single(startDispatcher.Requests).RunKind);
    }

    [Fact]
    public async Task Route_StartOnly_SelectsThePublicationOwnedByTheMatchedBinding()
    {
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a", "publication-7", "slot-production"));
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly));

        var selection = Assert.Single(startDispatcher.Requests).SourceSelection;
        Assert.Equal("publication-7", selection!.ActivationId);
        Assert.Equal("slot-production", selection.SlotId);
        Assert.Null(selection.SourceReferenceId);
    }

    [Fact]
    public async Task Route_StartOnly_DoesNotFilterBindingsByCorrelationScope()
    {
        // #1001 scopes only existing bookmark resumes. A published Event binding's authored scope is not a
        // start-selector: the matching type/hash binding must still start even when its scope differs.
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a", correlationScope: "order-9"));
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        var result = await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly, correlationId: "order-7"));

        Assert.Equal(1, result.StartedCount);
        Assert.Equal("artifact-1", Assert.Single(startDispatcher.Requests).ArtifactId);
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
    public async Task Route_WithIdempotencyKey_RedeliveryResolvesToTheSameExecution_AndReportsTheDuplicate()
    {
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        await bindingStore.SaveAsync(Binding("artifact-2", "node-a"));
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        var first = await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly, idempotencyKey: "delivery-1"));
        var second = await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly, idempotencyKey: "delivery-1"));

        // Durable, not process-local (#2195): every delivery names the same keyed execution per matched artifact, so the
        // dispatcher, which reads durable execution state, recognizes the redelivery on any node and after any restart.
        Assert.Equal(2, first.StartedCount);
        Assert.Equal(0, second.StartedCount);
        Assert.Equal(2, second.SkippedStartCount);
        var expected = new[]
        {
            KeyedWorkflowStartIdentity.For("delivery-1", "artifact-1"),
            KeyedWorkflowStartIdentity.For("delivery-1", "artifact-2")
        };
        Assert.Equal(
            expected.Concat(expected).Select(identity => (identity.WorkflowExecutionId, identity.StartKey)),
            startDispatcher.Requests.Select(request => (request.WorkflowExecutionId!, request.IdempotencyKey!)));
        Assert.Equal(expected.Select(identity => identity.WorkflowExecutionId), first.Starts.Select(start => start.WorkflowExecutionId!));
    }

    [Fact]
    public async Task Route_WithIdempotencyKey_RetriesAStartWhoseFirstDeliveryFailed()
    {
        // The direction that looks like success: a process-local "already begun" record taken before the dispatch made the
        // retry of a failed keyed start report a skipped duplicate, so the delivery was acknowledged and nothing started.
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var startDispatcher = new RecordingStartDispatcher { FailNext = new InvalidOperationException("transient") };
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly, idempotencyKey: "delivery-1")));
        var retry = await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly, idempotencyKey: "delivery-1"));

        Assert.Equal(1, retry.StartedCount);
        Assert.Equal(0, retry.SkippedStartCount);
    }

    [Fact]
    public async Task Route_ReportsAnAdmissionShedStartAsShed_NeverAsStarted()
    {
        // #2548: a shed start wrote nothing. Reporting it as started handed callers an execution id that never exists.
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var startDispatcher = new RecordingStartDispatcher { ShedNext = true };
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        var result = await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly));

        Assert.Equal(0, result.StartedCount);
        Assert.Equal(1, result.ShedStartCount);
        var shed = Assert.Single(result.Starts);
        Assert.Equal(StimulusStartStatus.Shed, shed.Status);
        Assert.Null(shed.WorkflowExecutionId);
        Assert.Equal(TimeSpan.FromSeconds(3), shed.RetryAfter);
        Assert.Equal(TimeSpan.FromSeconds(3), result.ShedRetryAfter);
    }

    [Fact]
    public async Task Route_WithIdempotencyKey_RetriesAShedStart_AsAFreshStart()
    {
        // A shed start leaves its key unconsumed, so the retry starts the workflow rather than answering a duplicate.
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var startDispatcher = new RecordingStartDispatcher { ShedNext = true };
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        var shed = await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly, idempotencyKey: "delivery-1"));
        var retry = await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly, idempotencyKey: "delivery-1"));

        Assert.Equal(1, shed.ShedStartCount);
        Assert.Equal(1, retry.StartedCount);
        Assert.Equal(0, retry.ShedStartCount);
        Assert.Equal(0, retry.SkippedStartCount);
    }

    [Fact]
    public async Task Route_ADeferredStartWithoutTheShedMarker_IsStarted()
    {
        // Deferred alone is not backpressure: the distributed leaf answers it for a start forwarded to its owning node.
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var startDispatcher = new RecordingStartDispatcher { ForwardNext = true };
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        var result = await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly));

        Assert.Equal(1, result.StartedCount);
        Assert.Equal(0, result.ShedStartCount);
        Assert.NotNull(Assert.Single(result.Starts).WorkflowExecutionId);
    }

    [Fact]
    public async Task Route_WithAnOccurrenceKey_NamesOneStartWhicheverArtifactServesIt()
    {
        // #2198: a recurring occurrence keyed by its trigger starts under the artifact-free identity, so the replacement
        // publication's fire of an occurrence converges on the replaced publication's start of it.
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(new ThrowingTriggerBindingStore(), new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        var replaced = await router.RouteAsync(OccurrenceRequest(Binding("artifact-1", "node-a", "publication-a", "slot-1")));
        var replacement = await router.RouteAsync(OccurrenceRequest(Binding("artifact-2", "node-a", "publication-b", "slot-1")));

        var identity = KeyedWorkflowStartIdentity.ForOccurrence("recurring:occurrence-1");
        Assert.Equal(1, replaced.StartedCount);
        Assert.Equal(1, replacement.SkippedStartCount);
        Assert.All(startDispatcher.Requests, request => Assert.Equal((identity.WorkflowExecutionId, identity.StartKey), (request.WorkflowExecutionId!, request.IdempotencyKey!)));
        Assert.Equal(["artifact-1", "artifact-2"], startDispatcher.Requests.Select(request => request.ArtifactId));
    }

    [Fact]
    public async Task Route_WithAnOccurrenceKey_RefusesMoreThanOneBinding_RatherThanStartingOnlyOne()
    {
        // The direction that could pass for success: two bindings under one occurrence key would collapse onto one
        // execution, so the second would silently never start. The router refuses before it starts either.
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(new ThrowingTriggerBindingStore(), new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await router.RouteAsync(
            OccurrenceRequest(Binding("artifact-1", "node-a", "publication-a", "slot-1"), Binding("artifact-2", "node-a", "publication-b", "slot-1"))));

        Assert.Empty(startDispatcher.Requests);
    }

    [Fact]
    public void An_occurrence_key_requires_a_start_only_request_with_its_key_and_its_pre_matched_binding()
    {
        var binding = Binding("artifact-1", "node-a");

        Assert.Throws<ArgumentException>(() => new StimulusDispatchRequest(StimulusType, StimulusHash, mode: StimulusRoutingMode.StartOnly,
            matchedTriggerBindings: [binding], startKeyScope: StimulusStartKeyScope.Occurrence));
        Assert.Throws<ArgumentException>(() => new StimulusDispatchRequest(StimulusType, StimulusHash, mode: StimulusRoutingMode.StartAndResume,
            idempotencyKey: "recurring:occurrence-1", matchedTriggerBindings: [binding], startKeyScope: StimulusStartKeyScope.Occurrence));
        Assert.Throws<ArgumentException>(() => new StimulusDispatchRequest(StimulusType, StimulusHash, mode: StimulusRoutingMode.StartOnly,
            idempotencyKey: "recurring:occurrence-1", startKeyScope: StimulusStartKeyScope.Occurrence));
    }

    [Fact]
    public async Task Route_WithoutIdempotencyKey_StartsUnkeyedExecutions()
    {
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());

        await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly));

        var request = Assert.Single(startDispatcher.Requests);
        Assert.Null(request.WorkflowExecutionId);
        Assert.Null(request.IdempotencyKey);
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

    [Fact]
    public async Task Route_ForwardsDispatchOptionsToStartDispatcher()
    {
        // Spec 089 E-D4 (FR-019): the router forwards the request's DispatchOptions verbatim to the start dispatcher,
        // so an in-process inline drain of the started instance can build activity execution contexts from the
        // caller's ambient scope. Same instance — never a copy.
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var startDispatcher = new RecordingStartDispatcher();
        var router = Router(bindingStore, new InMemoryBookmarkStateStore(), startDispatcher, new RecordingResumeDispatcher());
        var options = new WorkflowExecutionCommandDispatchOptions();

        await router.RouteAsync(Request(mode: StimulusRoutingMode.StartOnly, dispatchOptions: options));

        Assert.Same(options, Assert.Single(startDispatcher.DispatchOptions));
    }

    [Fact]
    public async Task Route_ForwardsDispatchOptionsToResumeDispatcher()
    {
        // Spec 089 E-D4 (FR-019 / scenario 5.5): the same request scope serves resumes too, so a sync-authored
        // resume gets the caller's ambient services on its same-exchange reply. Same instance — never a copy.
        var bookmarkStore = new InMemoryBookmarkStateStore();
        await bookmarkStore.SaveAsync(Bookmark("bk-1", "wfexec-1"));
        var resumeDispatcher = new RecordingResumeDispatcher();
        var router = Router(new InMemoryWorkflowTriggerBindingStore(), bookmarkStore, new RecordingStartDispatcher(), resumeDispatcher);
        var options = new WorkflowExecutionCommandDispatchOptions();

        await router.RouteAsync(Request(mode: StimulusRoutingMode.ResumeOnly, dispatchOptions: options));

        Assert.Same(options, Assert.Single(resumeDispatcher.DispatchOptions));
    }

    [Fact]
    public async Task Route_WithoutDispatchOptions_ForwardsNullToBothDispatchers()
    {
        // Absent options ⇒ the router forwards null; each dispatcher then falls back to
        // WorkflowExecutionCommandDispatchOptions.Default, pinning the pre-089 single-arg behavior.
        var bindingStore = new InMemoryWorkflowTriggerBindingStore();
        await bindingStore.SaveAsync(Binding("artifact-1", "node-a"));
        var bookmarkStore = new InMemoryBookmarkStateStore();
        await bookmarkStore.SaveAsync(Bookmark("bk-1", "wfexec-1"));
        var startDispatcher = new RecordingStartDispatcher();
        var resumeDispatcher = new RecordingResumeDispatcher();
        var router = Router(bindingStore, bookmarkStore, startDispatcher, resumeDispatcher);

        await router.RouteAsync(Request());

        Assert.Null(Assert.Single(startDispatcher.DispatchOptions));
        Assert.Null(Assert.Single(resumeDispatcher.DispatchOptions));
    }

    /// <summary>A binding store whose cross-artifact lookup throws — proves the router used the supplied match set.</summary>
    private sealed class ThrowingTriggerBindingStore : IWorkflowTriggerBindingStore
    {
        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(WorkflowTriggerBindingPageQuery query, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The router must reuse the supplied match set, not query the store.");
        public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(WorkflowTriggerBindingArtifactPageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(
            WorkflowTriggerBindingTypePageQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListActiveStimulusHashesAsync(
            string stimulusType,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowActivationProjectionState> FindActivationStateAsync(
            string activationId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<IReadOnlyCollection<string>> ListServingActivationIdsAsync(
            string slotId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private StimulusRouter Router(
        IWorkflowTriggerBindingStore bindingStore,
        InMemoryBookmarkStateStore bookmarkStore,
        RecordingStartDispatcher startDispatcher,
        RecordingResumeDispatcher resumeDispatcher) =>
        new(
            bindingStore,
            new GlobalBookmarkStimulusLookup(bookmarkStore),
            startDispatcher,
            resumeDispatcher,
            new FakeTimeProvider(_now));

    private static StimulusDispatchRequest Request(
        StimulusRoutingMode mode = StimulusRoutingMode.StartAndResume,
        string? correlationId = null,
        string? idempotencyKey = null,
        JsonElement? input = null,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null) =>
        new(StimulusType, StimulusHash, input: input, mode: mode, correlationId: correlationId, idempotencyKey: idempotencyKey, dispatchOptions: dispatchOptions);

    private static StimulusDispatchRequest OccurrenceRequest(params WorkflowTriggerBinding[] bindings) =>
        new(StimulusType, StimulusHash, mode: StimulusRoutingMode.StartOnly, idempotencyKey: "recurring:occurrence-1",
            matchedTriggerBindings: bindings, startKeyScope: StimulusStartKeyScope.Occurrence);

    private WorkflowTriggerBinding Binding(
        string artifactId,
        string nodeId,
        string? activationId = null,
        string? slotId = null,
        string? correlationScope = null) =>
        new(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(artifactId, nodeId, StimulusHash),
            ArtifactId: artifactId,
            DefinitionId: "definition-1",
            ArtifactVersion: "1.0.0",
            ArtifactHash: "sha256:artifact",
            ExecutableNodeId: nodeId,
            StimulusType: StimulusType,
            StimulusHash: StimulusHash,
            CorrelationScope: correlationScope,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: _now,
            ActivationId: activationId,
            SlotId: slotId);

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

    /// <summary>Starts every request, answering a caller-named execution it already started as a duplicate, as the real dispatcher does.</summary>
    private sealed class RecordingStartDispatcher(Action<string>? onStart = null) : IWorkflowStartDispatcher
    {
        private readonly HashSet<string> _started = new(StringComparer.Ordinal);
        private int _counter;
        public List<WorkflowExecutionStartDispatchRequest> Requests { get; } = [];
        public List<WorkflowExecutionCommandDispatchOptions?> DispatchOptions { get; } = [];
        public Exception? FailNext { get; set; }

        /// <summary>Answers the next dispatch as runtime admission would at capacity: Deferred, shed, nothing written.</summary>
        public bool ShedNext { get; set; }

        /// <summary>Answers the next dispatch Deferred without the shed marker, as the distributed leaf does when forwarding.</summary>
        public bool ForwardNext { get; set; }

        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request, WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published, WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            DispatchOptions.Add(dispatchOptions);
            if (FailNext is { } failure)
            {
                FailNext = null;
                throw failure;
            }

            var executionId = request.WorkflowExecutionId ?? $"wfexec-new-{++_counter}";
            if (ShedNext)
            {
                ShedNext = false;
                return new ValueTask<WorkflowExecutionStartDispatchResult>(Result(
                    request.ArtifactId,
                    executionId,
                    WorkflowExecutionCommandDispatchStatus.Deferred,
                    "Runtime dispatch admission is at capacity.",
                    new Dictionary<string, string>
                    {
                        [RuntimeMetadataKeys.DispatchShed] = "true",
                        [RuntimeMetadataKeys.DispatchRetryAfterSeconds] = "3"
                    }));
            }

            if (ForwardNext)
            {
                ForwardNext = false;
                _started.Add(executionId);
                return new ValueTask<WorkflowExecutionStartDispatchResult>(Result(
                    request.ArtifactId, executionId, WorkflowExecutionCommandDispatchStatus.Deferred, "Forwarded to the owning node."));
            }

            var status = _started.Add(executionId)
                ? WorkflowExecutionCommandDispatchStatus.Accepted
                : WorkflowExecutionCommandDispatchStatus.Duplicate;
            if (status == WorkflowExecutionCommandDispatchStatus.Accepted)
                onStart?.Invoke(executionId);
            return new ValueTask<WorkflowExecutionStartDispatchResult>(Result(request.ArtifactId, executionId, status));
        }

        private static WorkflowExecutionStartDispatchResult Result(
            string artifactId,
            string executionId,
            WorkflowExecutionCommandDispatchStatus status,
            string? reason = null,
            IReadOnlyDictionary<string, string>? metadata = null) =>
            new(
                executionId,
                new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:artifact"),
                new WorkflowExecutionCommandDispatchResult(
                    envelopeId: $"envelope-{executionId}",
                    workflowExecutionId: executionId,
                    status: status,
                    recordedAt: DateTimeOffset.UnixEpoch,
                    reason: reason,
                    metadata: metadata),
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
        public List<WorkflowExecutionCommandDispatchOptions?> DispatchOptions { get; } = [];

        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request, WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            DispatchOptions.Add(dispatchOptions);
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
                descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
                inputBindings: new Dictionary<string, RuntimeInputBinding>(),
                metadata: new Dictionary<string, string>());
        }
    }
}
