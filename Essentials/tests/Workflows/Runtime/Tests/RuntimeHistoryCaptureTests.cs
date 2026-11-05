using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeHistoryCaptureTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v1", 1, true, RuntimeHistoryCapture.Kinds);
    private static readonly WorkflowExecutableIdentity Executable = new("artifact", "definition", "revision", "1.0.0", "hash");

    [Fact]
    public async Task Logical_transitions_survive_folding_in_one_atomic_history_group_and_replay_once()
    {
        var fixture = new Fixture();
        await fixture.Executions.SaveAsync(Workflow());
        var capture = new RuntimeHistoryCapture(fixture.Executions);
        var start = await capture.EnrichAsync(Commit("start", RuntimeCheckpointNames.ActivityStarted, workflow: Workflow(), activity: Activity(ActivityExecutionStatus.Running)));
        var end = await capture.EnrichAsync(Commit("end", RuntimeCheckpointNames.ActivityCompleted, activity: Activity(ActivityExecutionStatus.Completed)));
        var folded = end with { CommitId = "physical-segment", StateChanges = RuntimeCheckpointFold.Fold([start.StateChanges, end.StateChanges]) };
        await fixture.Store.CommitAsync(folded, new(RuntimeCheckpointPersistenceMode.Immediate));
        var replay = await fixture.Store.CommitAsync(folded, new(RuntimeCheckpointPersistenceMode.Immediate));
        var page = await fixture.Reader.ReadPageAsync(new("run"));
        Assert.Equal(["ActivityStarted", "ActivityCompleted"], page.Items.Select(e => e.Name));
        Assert.Equal([1L, 2L], page.Items.Select(e => e.Sequence));
        Assert.All(page.Items, e => Assert.Equal(0, e.CheckpointOrdinal));
        Assert.All(page.Items, e => Assert.Equal("physical-segment", e.PhysicalCommitId));
        Assert.Equal(["checkpoint-start", "checkpoint-end"], page.Items.Select(e => e.CheckpointId));
        Assert.Equal(2, page.Head!.LastSequence);
        Assert.Empty(replay.PendingPostCommitWorkIds);
    }

    [Fact]
    public async Task History_required_validation_fails_before_other_participants_and_does_not_allocate()
    {
        var fixture = new Fixture();
        await fixture.Executions.SaveAsync(Workflow());
        var capture = new RuntimeHistoryCapture(fixture.Executions);
        var initial = await capture.EnrichAsync(Commit("initial", RuntimeCheckpointNames.WorkflowStarted, workflow: Workflow()));
        await fixture.Store.CommitAsync(initial, new(RuntimeCheckpointPersistenceMode.Immediate));
        var before = await fixture.Reader.FindHeadAsync("run");
        var uncaptured = Commit("missing", RuntimeCheckpointNames.WorkflowCompleted, workflow: Workflow() with { Status = WorkflowExecutionStatus.Completed });
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Store.CommitAsync(uncaptured, new(RuntimeCheckpointPersistenceMode.Immediate)));
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.Executions.FindAsync("run"))!.Status);
        Assert.Equal(before, await fixture.Reader.FindHeadAsync("run"));
        Assert.False(fixture.State.Commits.ContainsKey("missing"));
    }

    [Fact]
    public async Task Enabling_capture_does_not_change_a_legacy_marker_or_queued_checkpoint()
    {
        var fixture = new Fixture();
        await fixture.Executions.SaveAsync(Workflow() with { HistoryCapturePolicy = null });
        var legacy = Commit("legacy", RuntimeCheckpointNames.ActivityStarted);
        await fixture.Store.CommitAsync(legacy, new(RuntimeCheckpointPersistenceMode.Immediate));
        var before = RuntimeCheckpointCommitFingerprint.Compute(legacy);
        var after = await new RuntimeHistoryCapture(fixture.Executions).EnrichAsync(legacy);
        Assert.Same(legacy, after);
        Assert.Equal(before, RuntimeCheckpointCommitFingerprint.Compute(after));
        await fixture.Store.CommitAsync(after, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Null(await fixture.Reader.FindHeadAsync("run"));
        var attemptedUpgrade = Commit("upgrade", RuntimeCheckpointNames.WorkflowStarted, workflow: Workflow());
        Assert.Same(attemptedUpgrade, await new RuntimeHistoryCapture(fixture.Executions).EnrichAsync(attemptedUpgrade));
    }

    [Fact]
    public async Task Existing_legacy_state_does_not_inherit_policy_from_a_newer_checkpoint_payload()
    {
        var fixture = new Fixture();
        await fixture.Executions.SaveAsync(Workflow() with { HistoryCapturePolicy = null,
            RootVariableFrame = new VariableFrameFactory().CreateRoot("run", "workflow", new Dictionary<string, ValueEnvelope>()) });
        var committer = fixture.Committer(new RuntimeHistoryCapture(fixture.Executions));
        var handler = new WorkflowCheckpointSchedulerWorkHandler(new InMemoryActivityExecutionStateStore(), committer,
            null, new FakeTimeProvider(At), fixture.Executions);
        var payload = new RuntimeCheckpointCommandPayload(Executable, RuntimeCheckpointNames.WorkflowStarted, [], "start") { HistoryCapturePolicy = Policy };
        await handler.HandleAsync(new("work", "run", "command", WorkflowExecutionCommandKind.Checkpoint,
            "envelope", "key", At, At, payload: JsonSerializer.SerializeToElement(payload)));
        Assert.Null((await fixture.Executions.FindAsync("run"))!.HistoryCapturePolicy);
        Assert.Null(await fixture.Reader.FindHeadAsync("run"));
    }

    [Fact]
    public async Task Initial_claim_consumes_only_selected_handle_and_retires_siblings_while_redelivery_consumes_none()
    {
        var fixture = new Fixture(); await fixture.Executions.SaveAsync(Workflow());
        var capture = new RuntimeHistoryCapture(fixture.Executions);
        var metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.BookmarkId] = "selected", [RuntimeMetadataKeys.ResumeTargetId] = "continue" };
        var selected = Bookmark("selected"); var sibling = Bookmark("sibling");
        var source = Commit("claim", RuntimeCheckpointNames.ActivityAttemptClaimed, activity: Activity(ActivityExecutionStatus.Running), metadata: metadata,
            bookmarks: [new("selected", RuntimeStateChangeOperation.Delete, selected, metadata), new("sibling", RuntimeStateChangeOperation.Delete, sibling, metadata)]);
        var captured = await capture.EnrichAsync(source);
        var events = captured.StateChanges.HistoryBatches!.Single().Candidates;
        Assert.Contains(events, e => e.Event.Name == "BookmarkConsumed" && e.Event.BookmarkId == "selected");
        var retired = Assert.Single(events, e => e.Event.Name == "BookmarkRetired");
        Assert.Equal("sibling", retired.Event.BookmarkId);
        Assert.Equal("sibling-resume-won", retired.BookmarkMutation!.Reason);
        Assert.Contains(events, e => e.Event.Name == "ActivityResumeClaimed");
        var retry = await capture.EnrichAsync(Commit("retry", RuntimeCheckpointNames.ActivityAttemptClaimed, activity: Activity(ActivityExecutionStatus.Running), metadata: metadata));
        Assert.Equal("ActivityResumeRetried", Assert.Single(retry.StateChanges.HistoryBatches!.Single().Candidates).Event.Name);
    }

    [Fact]
    public async Task Terminal_stale_handle_cleanup_is_not_another_consumption_or_activity_completion()
    {
        var fixture = new Fixture(); await fixture.Executions.SaveAsync(Workflow());
        var metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.BookmarkId] = "selected" };
        var source = Commit("stale", RuntimeCheckpointNames.BookmarkConsumed, activity: Activity(ActivityExecutionStatus.Completed), metadata: metadata,
            bookmarks: [new("selected", RuntimeStateChangeOperation.Delete, Bookmark("selected"), metadata)]);
        var captured = await new RuntimeHistoryCapture(fixture.Executions).EnrichAsync(source);
        var events = captured.StateChanges.HistoryBatches!.Single().Candidates;
        Assert.Equal("BookmarkStaleRemoved", Assert.Single(events).Event.Name);
        Assert.Equal(RuntimeHistoryBookmarkStatus.Removed, events[0].BookmarkMutation!.Status);
    }

    [Fact]
    public void Every_clone_preserves_history_and_empty_history_keeps_the_legacy_fingerprint()
    {
        var source = Commit("clone", "test");
        var e = new RuntimeHistoryEvent("event", "run", 0, RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", At, checkpointId: source.Checkpoint.CheckpointId)
            { Authority = RuntimeHistoryAuthority.CommittedTransition };
        var batch = new RuntimeHistoryBatch(Policy, source.Checkpoint.CheckpointId, At, false, [new(e, 0)]);
        var changes = source.StateChanges.WithHistoryBatches([batch]);
        var clones = new[] { changes.WithPostCommitOutbox([]), changes.WithActivityExecutions([]), changes.WithWorkflowDispatches([]),
            changes.WithWorkflowDispatchCancellations([]), changes.WithConsumedSchedulerWorkItems([]), changes.WithAlterationJobTerminalChange(null),
            changes.WithBackgroundActivityWork(BackgroundActivityWorkCheckpointChanges.Empty) };
        Assert.All(clones, c => Assert.Same(batch, Assert.Single(c.HistoryBatches!)));
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(source), RuntimeCheckpointCommitFingerprint.Compute(source with { StateChanges = source.StateChanges.WithHistoryBatches([]) }));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(source), RuntimeCheckpointCommitFingerprint.Compute(source with { StateChanges = changes }));
    }

    [Fact]
    public async Task Unrelated_read_store_is_refused_before_effects_even_if_writer_claims_support()
    {
        var fixture = new Fixture(); await fixture.Executions.SaveAsync(Workflow());
        var otherState = new InMemoryRuntimeCheckpointStoreState();
        var other = new InMemoryRuntimeHistoryStore(otherState, fixture.Access, Codec());
        Assert.False(((IRuntimeCheckpointCommitStore)fixture.Store).CanCommitHistory(other));
        var committer = fixture.Committer(new RuntimeHistoryCapture(fixture.Executions), other);
        await Assert.ThrowsAsync<NotSupportedException>(async () => await committer.CommitAsync(Commit("wrong-reader", RuntimeCheckpointNames.WorkflowStarted, workflow: Workflow())));
        Assert.Empty(fixture.State.Commits);
        Assert.Null(await fixture.Reader.FindHeadAsync("run"));
    }

    [Fact]
    public async Task History_alone_does_not_defeat_ephemeral_no_record_completion()
    {
        var fixture = new Fixture(); await fixture.Executions.SaveAsync(Workflow());
        var capture = new RuntimeHistoryCapture(fixture.Executions);
        var deferred = await capture.EnrichAsync(Commit("hot", RuntimeCheckpointNames.ActivityCompleted, activity: Activity(ActivityExecutionStatus.Completed)));
        var completion = await capture.EnrichAsync(Commit("completed", RuntimeCheckpointNames.WorkflowCompleted, workflow: Workflow() with { Status = WorkflowExecutionStatus.Completed }));
        var session = new RuntimeCoalescingSession("run", new InMemoryWorkflowSchedulerWorkQueue(),
            new PersistenceModeOptions { Mode = PersistenceMode.Ephemeral, RecordCompletion = false }, mode: new(PersistenceMode.Ephemeral, false));
        session.BufferDeferred(deferred);
        Assert.True(session.CanDiscardCompletion(completion, []));
        Assert.Null(await fixture.Reader.FindHeadAsync("run"));
    }

    [Fact]
    public async Task Redelivery_keeps_the_first_admitted_queued_policy_after_history_is_enabled()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        RuntimeSchedulerWorkItem Work(RuntimeHistoryCapturePolicy? policy) => new("start-work", "run", "command", WorkflowExecutionCommandKind.Start,
            "envelope", "key", At, At, payload: JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(Executable, "artifact") { HistoryCapturePolicy = policy }));
        var admitted = await queue.EnqueueAsync(Work(null));
        var duplicate = await queue.EnqueueAsync(Work(Policy));
        Assert.Same(admitted, duplicate);
        Assert.Null(duplicate.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!.HistoryCapturePolicy);
    }

    [Fact]
    public async Task Removed_capture_module_cannot_skip_required_history_through_another_scope()
    {
        var fixture = new Fixture();
        var initial = await new RuntimeHistoryCapture(fixture.Executions).EnrichAsync(Commit("admission", RuntimeCheckpointNames.WorkflowStarted, workflow: Workflow()));
        await fixture.Store.CommitAsync(initial, new(RuntimeCheckpointPersistenceMode.Immediate));
        var before = await fixture.Reader.FindHeadAsync("run");
        var wrongScope = new InMemoryRuntimeCheckpointCommitStore(workflowExecutionStateStore: fixture.Executions, state: fixture.State,
            persistenceAccessContextAccessor: new Access("scope-b"), rootWriteLeaseManager: new Lease());
        var uncaptured = Commit("uncaptured-other-scope", RuntimeCheckpointNames.ActivityStarted, activity: Activity(ActivityExecutionStatus.Running));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await wrongScope.CommitAsync(uncaptured, new(RuntimeCheckpointPersistenceMode.Immediate)));
        Assert.Equal(before, await fixture.Reader.FindHeadAsync("run"));
        Assert.False(fixture.State.Commits.ContainsKey("uncaptured-other-scope"));
        Assert.False(fixture.State.HistoryHeads.ContainsKey(("scope-b", "run")));
    }

    [Fact]
    public async Task History_scope_mismatch_is_refused_before_state_or_history_mutation()
    {
        var fixture = new Fixture();
        await fixture.Executions.SaveAsync(Workflow());
        var captured = await new RuntimeHistoryCapture(fixture.Executions).EnrichAsync(Commit("wrong-scope", RuntimeCheckpointNames.WorkflowCompleted,
            workflow: Workflow() with { Status = WorkflowExecutionStatus.Completed }));
        var wrongScope = new InMemoryRuntimeCheckpointCommitStore(workflowExecutionStateStore: fixture.Executions, state: fixture.State,
            persistenceAccessContextAccessor: new Access("scope-b"), rootWriteLeaseManager: new Lease());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await wrongScope.CommitAsync(captured, new(RuntimeCheckpointPersistenceMode.Immediate)));
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.Executions.FindAsync("run"))!.Status);
        Assert.Empty(fixture.State.HistoryHeads);
        Assert.Empty(fixture.State.Commits);
    }

    [Fact]
    public async Task Value_links_use_explicit_new_evidence_even_when_the_clock_advances_before_checkpoint()
    {
        var fixture = new Fixture();
        await fixture.Executions.SaveAsync(Workflow());
        var inspections = new InMemoryActivityExecutionInspectionStore();
        var capture = new RuntimeHistoryCapture(fixture.Executions, inspections);
        var value = new ActivityExecutionInspectionValueSnapshot("input", ActivityExecutionInspectionValueSubject.ActivityInput,
            default, null, At.AddSeconds(-2), null, "metadata-only", false, new Dictionary<string, string>(), EvidenceId: "evidence");
        var projection = ActivityExecutionInspectionProjection.FromState(Activity(ActivityExecutionStatus.Running), "checkpoint-value", At, valueSnapshots: [value]);
        RuntimeCheckpointCommit Source(string id) => Commit(id, "values") with { StateChanges = new(null, null, [], [], [], [], [],
            activityExecutionInspections: [new("activity", RuntimeStateChangeOperation.Upsert, projection, new Dictionary<string, string>())]) };

        var original = await capture.EnrichAsync(Source("value"));
        var e = Assert.Single(original.StateChanges.HistoryBatches!.Single().Candidates).Event;
        Assert.Equal("ValueCaptured", e.Name);
        Assert.Equal(At.AddSeconds(-2), e.OccurredAt);
        Assert.Null(e.Payload);
        await inspections.SaveAsync(projection);
        await capture.ValidateFinalAsync(original);
        var changedProjection = projection with { ValueSnapshots = [value, value with { EvidenceId = "late-evidence", CapturedAt = At.AddSeconds(-3) }] };
        var changedSource = original with { StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [],
            activityExecutionInspections: [new("activity", RuntimeStateChangeOperation.Upsert, changedProjection, new Dictionary<string, string>())])
            .WithHistoryBatches(original.StateChanges.HistoryBatches) };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await capture.ValidateFinalAsync(changedSource));
        var reprojected = await capture.EnrichAsync(Source("reproject"));
        Assert.Empty(reprojected.StateChanges.HistoryBatches!.Single().Candidates);
    }

    [Fact]
    public async Task Folded_and_retained_bookmark_mutation_conflicts_are_rejected_atomically()
    {
        var fixture = new Fixture();
        var source = Commit("bookmark-first", "test", workflow: Workflow());
        var e = new RuntimeHistoryEvent("bookmark-event", "run", 0, RuntimeHistoryEventCategory.BookmarkLifecycle,
            "BookmarkCreated", At, "activity", checkpointId: "logical") { Authority = RuntimeHistoryAuthority.CommittedTransition, BookmarkId = "bookmark" };
        var summary = ActivityExecutionBookmarkSummary.From(Bookmark("bookmark"));
        RuntimeHistoryBatch Batch(DateTimeOffset? expiresAt) => new(Policy, "logical", At, false,
            [new(e, 0, new(summary with { ExpiresAt = expiresAt }, RuntimeHistoryBookmarkStatus.Active))]);
        var original = Batch(null);
        var conflicting = Batch(At.AddHours(1));
        var folded = source with { StateChanges = source.StateChanges.WithHistoryBatches([original, conflicting]) };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Store.CommitAsync(folded, new(RuntimeCheckpointPersistenceMode.Immediate)));
        Assert.Empty(fixture.State.HistoryEvents);
        await fixture.Store.CommitAsync(source with { StateChanges = source.StateChanges.WithHistoryBatches([original]) }, new(RuntimeCheckpointPersistenceMode.Immediate));
        var head = await fixture.Reader.FindHeadAsync("run");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Store.CommitAsync(source with
            { CommitId = "bookmark-conflict", StateChanges = source.StateChanges.WithHistoryBatches([conflicting]) }, new(RuntimeCheckpointPersistenceMode.Immediate)));
        Assert.Equal(head, await fixture.Reader.FindHeadAsync("run"));
        Assert.Null((await fixture.Reader.FindBookmarkAsync("run", "bookmark"))!.Bookmark.ExpiresAt);
    }

    [Fact]
    public async Task Bookmark_only_events_keep_unknown_parent_distinct_from_a_known_root()
    {
        var fixture = new Fixture();
        await fixture.Executions.SaveAsync(Workflow());
        var source = Commit("bookmark-only", "test", workflow: Workflow(),
            bookmarks: [new("bookmark", RuntimeStateChangeOperation.Upsert, Bookmark("bookmark"), new Dictionary<string, string>())]);
        var captured = await new RuntimeHistoryCapture(fixture.Executions).EnrichAsync(source);
        await fixture.Store.CommitAsync(captured, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.False(fixture.State.HistoryParents.ContainsKey(("scope-a", "run", "activity")));
        var lifecycle = await new RuntimeHistoryCapture(fixture.Executions).EnrichAsync(Commit("lifecycle", RuntimeCheckpointNames.ActivityStarted, activity: Activity(ActivityExecutionStatus.Running)));
        await fixture.Store.CommitAsync(lifecycle, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.True(fixture.State.HistoryParents.TryGetValue(("scope-a", "run", "activity"), out var parent));
        Assert.Null(parent);
    }

    [Fact]
    public async Task Retained_identity_rejects_conflicting_logical_ordinal_without_publishing_partial_rows()
    {
        var fixture = new Fixture();
        var source = Commit("first", "test", workflow: Workflow());
        RuntimeHistoryEvent Event(string id) => new(id, "run", 0, RuntimeHistoryEventCategory.ActivityLifecycle,
            "ActivityStarted", At, checkpointId: "logical") { Authority = RuntimeHistoryAuthority.CommittedTransition };
        var stable = Event("stable");
        var first = source with { StateChanges = source.StateChanges.WithHistoryBatches([new(Policy, "logical", At, false, [new(stable, 0)])]) };
        await fixture.Store.CommitAsync(first, new(RuntimeCheckpointPersistenceMode.Immediate));
        var before = await fixture.Reader.FindHeadAsync("run");
        var conflict = source with { CommitId = "conflict", StateChanges = source.StateChanges.WithHistoryBatches(
            [new(Policy, "logical", At, false, [new(Event("new"), 0), new(stable, 1)])]) };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Store.CommitAsync(conflict, new(RuntimeCheckpointPersistenceMode.Immediate)));
        Assert.Equal(before, await fixture.Reader.FindHeadAsync("run"));
        Assert.Null(await fixture.Reader.FindEventAsync("run", "new"));
        Assert.False(fixture.State.Commits.ContainsKey("conflict"));
    }

    [Fact]
    public void History_identity_preserves_opaque_strings_and_does_not_include_provider_positions()
    {
        Assert.NotEqual(RuntimeHistoryIdentity.CreateId("a\0b", "c", "d"), RuntimeHistoryIdentity.CreateId("a", "b\0c", "d"));
        Assert.NotEqual(RuntimeHistoryIdentity.CreateId("a", "b", "\ud800"), RuntimeHistoryIdentity.CreateId("a", "b", "\ud801"));
        var e = new RuntimeHistoryEvent("event", "run", 0, RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", At);
        Assert.Equal(RuntimeHistoryIdentity.Compute(e), RuntimeHistoryIdentity.Compute(RuntimeHistoryIdentity.Position(e, 7, "commit", At.AddMinutes(1))));
    }

    [Fact]
    public void Batch_rejects_unadmitted_kind_and_arbitrary_sensitive_metadata()
    {
        var unadmitted = new RuntimeHistoryEvent("event", "run", 0, RuntimeHistoryEventCategory.ActivityLifecycle, "SecretKind", At, checkpointId: "checkpoint") { Authority = RuntimeHistoryAuthority.CommittedTransition };
        Assert.Throws<InvalidOperationException>(() => new RuntimeHistoryBatch(Policy, "checkpoint", At, false, [new(unadmitted, 0)]));
        var unsafeMetadata = new RuntimeHistoryEvent("event", "run", 0, RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", At,
            checkpointId: "checkpoint", metadata: new Dictionary<string, string> { ["authorization"] = "private-token" }) { Authority = RuntimeHistoryAuthority.CommittedTransition };
        Assert.Throws<InvalidOperationException>(() => new RuntimeHistoryBatch(Policy, "checkpoint", At, false, [new(unsafeMetadata, 0)]));
    }

    [Fact]
    public void Explicit_history_registration_is_opt_in_and_reorderable()
    {
        var plain = new ServiceCollection(); plain.AddWorkflowRuntime();
        Assert.DoesNotContain(plain, d => d.ServiceType == typeof(RuntimeHistoryOptions));
        plain.AddRuntimeHistory(); plain.AddRuntimeHistory();
        Assert.Single(plain, d => d.ServiceType == typeof(RuntimeHistoryOptions));
        Assert.Single(plain, d => d.ServiceType == typeof(IRuntimeHistoryStore));
        Assert.Single(plain, d => d.ImplementationType == typeof(RuntimeHistoryCapture));
    }

    private static WorkflowExecutionState Workflow() => new("run", Executable, WorkflowExecutionStatus.Running, null, At, At, At, null, null, null,
        "scope-a", new Dictionary<string, string>()) { HistoryCapturePolicy = Policy };
    private static ActivityExecutionState Activity(ActivityExecutionStatus status) => new(new("activity", "run", "node", "authored", "Test", "1"), status, null,
        1, At, At, status == ActivityExecutionStatus.Completed ? At : null, null, null, null, null,
        ActivitySchedulingProvenance.From("run", null, null, null, null, null, null, "test"), 0, [], [], 0, 0, new Dictionary<string, string>());
    private static BookmarkState Bookmark(string id) => new(id, "run", "activity", "node", "continue", "task", "safe-hash", null, new Dictionary<string, string>(), At, null);
    private static RuntimeCheckpointCommit Commit(string id, string name, WorkflowExecutionState? workflow = null, ActivityExecutionState? activity = null,
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>>? bookmarks = null, IReadOnlyDictionary<string, string>? metadata = null)
    {
        metadata ??= new Dictionary<string, string>();
        return new(id, new("checkpoint-" + id, name, "run", At, activity is null ? [] : [activity.InvocationId], metadata),
            new(workflow is null ? null : new("run", RuntimeStateChangeOperation.Upsert, workflow, metadata), null,
                activity is null ? [] : [new(activity.InvocationId, RuntimeStateChangeOperation.Upsert, activity, metadata)], bookmarks ?? [], [], [], []), [], metadata);
    }
    private static HmacRuntimeRecoveryContinuationCodec Codec() => new(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "history-capture-tests-signing-key-at-least-32-bytes" }));
    private sealed class Access(string scope = "scope-a") : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(scope)); }
    private sealed class Lease : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) => write(cancellationToken);
    }
    private sealed class Fixture
    {
        internal InMemoryRuntimeCheckpointStoreState State { get; } = new();
        internal Access Access { get; } = new();
        internal InMemoryWorkflowExecutionStateStore Executions { get; }
        internal InMemoryRuntimeCheckpointCommitStore Store { get; }
        internal InMemoryRuntimeHistoryStore Reader { get; }
        internal Fixture()
        {
            Executions = new(State, null);
            Store = new(workflowExecutionStateStore: Executions, state: State, persistenceAccessContextAccessor: Access, rootWriteLeaseManager: new Lease());
            Reader = new(State, Access, Codec());
        }
        internal RuntimeCheckpointCommitter Committer(IRuntimeCheckpointCommitEnricher capture, IRuntimeHistoryStore? reader = null) =>
            new(new ImmediateRuntimeCheckpointPersistencePolicy(), Store, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [capture], []) { HistoryStore = reader ?? Reader };
    }
}
