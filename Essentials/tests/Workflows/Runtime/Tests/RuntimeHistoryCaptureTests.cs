using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Bookmarks;
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
        var folded = end with { CommitId = "physical-segment", StateChanges = RuntimeCheckpointFold.Fold([start.StateChanges, end.StateChanges]), HistoryBatches = [.. start.HistoryBatches!, .. end.HistoryBatches!] };
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Real_coalescing_boundary_and_quiescence_keep_ordered_checkpoint_sidecars(bool quiescence)
    {
        var fixture = new Fixture();
        await fixture.Executions.SaveAsync(Workflow());
        var capture = new RuntimeHistoryCapture(fixture.Executions);
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var decorated = new CoalescingRuntimeCheckpointCommitStore(new(fixture.Store), sessions);
        var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), decorated,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [capture], []) { HistoryStore = fixture.Reader };
        var factory = new RuntimeCoalescingDrainScopeFactory(sessions, committer, new(queue), new(fixture.Store),
            new PersistenceModeOptions(), new FakeTimeProvider(At));
        await using var scope = factory.Begin("run", new(PersistenceMode.Balanced, true));
        var start = await capture.EnrichAsync(Commit("sidecar-start", RuntimeCheckpointNames.ActivityStarted, workflow: Workflow(), activity: Activity(ActivityExecutionStatus.Running)));
        var end = await capture.EnrichAsync(Commit("sidecar-end", RuntimeCheckpointNames.ActivityCompleted, activity: Activity(ActivityExecutionStatus.Completed)));
        await decorated.CommitAsync(start, new(RuntimeCheckpointPersistenceMode.Deferred));
        Assert.Empty(fixture.Store.ListCommits());
        await decorated.CommitAsync(end, new(quiescence ? RuntimeCheckpointPersistenceMode.Deferred : RuntimeCheckpointPersistenceMode.Immediate));
        if (quiescence) await scope.FlushAtQuiescenceAsync();

        var retained = Assert.Single(fixture.Store.ListCommits()).Commit;
        Assert.Equal(["checkpoint-sidecar-start", "checkpoint-sidecar-end"], retained.HistoryBatches!.Select(batch => batch.LogicalCheckpointId));
        Assert.Equal(ActivityExecutionStatus.Completed, Assert.Single(retained.StateChanges.ActivityExecutions).State.Status);
        Assert.Equal(["ActivityStarted", "ActivityCompleted"], (await fixture.Reader.ReadPageAsync(new("run"))).Items.Select(item => item.Name));
        Assert.Null(typeof(RuntimeCheckpointStateChangeSet).GetProperty("HistoryBatches"));
        Assert.Null(scope.Session.FoldHistoryBatchesWith());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void History_enabled_or_disabled_runtime_services_resolve_without_an_admission_scope(bool enabled)
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddRuntimeHistory(options => options.Enabled = enabled);
        var readerResolutions = 0;
        services.AddScoped<IRuntimeHistoryStore>(_ => { readerResolutions++; throw new IOException("History backend unavailable"); });
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IWorkflowStartDispatcher>());
        Assert.NotNull(provider.GetRequiredService<RuntimeCheckpointCommitter>());
        Assert.NotEmpty(provider.GetServices<IRuntimeCheckpointCommitEnricher>());
        Assert.Equal(0, readerResolutions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_uncaptured_checkpoint_never_resolves_or_queries_optional_history_backend(bool disabledModule)
    {
        var fixture = new Fixture();
        await fixture.Executions.SaveAsync(Workflow() with { HistoryCapturePolicy = null });
        var resolutions = 0;
        var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), fixture.Store,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), disabledModule ? [new RuntimeHistoryCapture(fixture.Executions)] : [], [])
        { HistoryStoreResolver = () => { resolutions++; throw new IOException("History backend unavailable"); } };

        await committer.CommitAsync(Commit("ordinary", RuntimeCheckpointNames.ActivityStarted, activity: Activity(ActivityExecutionStatus.Running)));

        Assert.Equal(0, resolutions);
        Assert.Single(fixture.Store.ListCommits());
        Assert.Empty(fixture.State.HistoryHeads);
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
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await fixture.Store.CommitAsync(uncaptured, new(RuntimeCheckpointPersistenceMode.Immediate)));
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
        var events = captured.HistoryBatches!.Single().Candidates;
        Assert.Contains(events, e => e.Event.Name == "BookmarkConsumed" && e.Event.BookmarkId == "selected");
        var retired = Assert.Single(events, e => e.Event.Name == "BookmarkRetired");
        Assert.Equal("sibling", retired.Event.BookmarkId);
        Assert.Equal("sibling-resume-won", retired.BookmarkMutation!.Reason);
        Assert.Contains(events, e => e.Event.Name == "ActivityResumeClaimed");
        var retry = await capture.EnrichAsync(Commit("retry", RuntimeCheckpointNames.ActivityAttemptClaimed, activity: Activity(ActivityExecutionStatus.Running), metadata: metadata));
        Assert.Equal("ActivityResumeRetried", Assert.Single(retry.HistoryBatches!.Single().Candidates).Event.Name);
    }

    [Fact]
    public async Task Terminal_stale_handle_cleanup_is_not_another_consumption_or_activity_completion()
    {
        var fixture = new Fixture(); await fixture.Executions.SaveAsync(Workflow());
        var metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.BookmarkId] = "selected" };
        var source = Commit("stale", RuntimeCheckpointNames.BookmarkConsumed, activity: Activity(ActivityExecutionStatus.Completed), metadata: metadata,
            bookmarks: [new("selected", RuntimeStateChangeOperation.Delete, Bookmark("selected"), metadata)]);
        var captured = await new RuntimeHistoryCapture(fixture.Executions).EnrichAsync(source);
        var events = captured.HistoryBatches!.Single().Candidates;
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
        var captured = source with { HistoryBatches = [batch] };
        var changes = captured.StateChanges;
        var clones = new[] { changes.WithPostCommitOutbox([]), changes.WithActivityExecutions([]), changes.WithWorkflowDispatches([]),
            changes.WithWorkflowDispatchCancellations([]), changes.WithConsumedSchedulerWorkItems([]), changes.WithAlterationJobTerminalChange(null),
            changes.WithBackgroundActivityWork(BackgroundActivityWorkCheckpointChanges.Empty) };
        Assert.All(clones, c => Assert.Same(batch, Assert.Single((captured with { StateChanges = c }).HistoryBatches!)));
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(source), RuntimeCheckpointCommitFingerprint.Compute(source with { HistoryBatches = [] }));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(source), RuntimeCheckpointCommitFingerprint.Compute(captured));
    }

    [Fact]
    public async Task Unrelated_read_store_is_refused_before_effects_even_if_writer_claims_support()
    {
        var fixture = new Fixture(); await fixture.Executions.SaveAsync(Workflow());
        var otherState = new InMemoryRuntimeCheckpointStoreState();
        var other = new InMemoryRuntimeHistoryStore(otherState, fixture.Access, Codec());
        Assert.False(((IRuntimeCheckpointCommitStore)fixture.Store).CanCommitHistory(other));
        var committer = fixture.Committer(new RuntimeHistoryCapture(fixture.Executions), other);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await committer.CommitAsync(Commit("wrong-reader", RuntimeCheckpointNames.WorkflowStarted, workflow: Workflow())));
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
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await wrongScope.CommitAsync(uncaptured, new(RuntimeCheckpointPersistenceMode.Immediate)));
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
        var e = Assert.Single(original.HistoryBatches!.Single().Candidates).Event;
        Assert.Equal("ValueCaptured", e.Name);
        Assert.Equal(At.AddSeconds(-2), e.OccurredAt);
        Assert.Null(e.Payload);
        await inspections.SaveAsync(projection);
        await capture.ValidateFinalAsync(original);
        var changedProjection = projection with { ValueSnapshots = [value, value with { EvidenceId = "late-evidence", CapturedAt = At.AddSeconds(-3) }] };
        var changedSource = original with { StateChanges = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [],
            activityExecutionInspections: [new("activity", RuntimeStateChangeOperation.Upsert, changedProjection, new Dictionary<string, string>())])
            };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await capture.ValidateFinalAsync(changedSource));
        var reprojected = await capture.EnrichAsync(Source("reproject"));
        Assert.Empty(reprojected.HistoryBatches!.Single().Candidates);
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
        var folded = source with { HistoryBatches = [original, conflicting] };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Store.CommitAsync(folded, new(RuntimeCheckpointPersistenceMode.Immediate)));
        Assert.Empty(fixture.State.HistoryEvents);
        await fixture.Store.CommitAsync(source with { HistoryBatches = [original] }, new(RuntimeCheckpointPersistenceMode.Immediate));
        var head = await fixture.Reader.FindHeadAsync("run");
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Store.CommitAsync(source with
            { CommitId = "bookmark-conflict", HistoryBatches = [conflicting] }, new(RuntimeCheckpointPersistenceMode.Immediate)));
        Assert.Equal(head, await fixture.Reader.FindHeadAsync("run"));
        Assert.Null((await fixture.Reader.FindBookmarkAsync("run", "bookmark"))!.Bookmark.ExpiresAt);
    }

    [Fact]
    public async Task Bookmark_only_events_keep_unknown_parent_distinct_from_a_known_root()
    {
        var fixture = new Fixture();
        var owner = Workflow() with { HistoryCapturePolicy = new("runtime-history-v2", 2, true, Policy.SupportedKinds) };
        await fixture.Executions.SaveAsync(owner);
        var source = Commit("bookmark-only", "test", workflow: owner,
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
        var first = source with { HistoryBatches = [new(Policy, "logical", At, false, [new(stable, 0)])] };
        await fixture.Store.CommitAsync(first, new(RuntimeCheckpointPersistenceMode.Immediate));
        var before = await fixture.Reader.FindHeadAsync("run");
        var conflict = source with { CommitId = "conflict", HistoryBatches =
            [new(Policy, "logical", At, false, [new(Event("new"), 0), new(stable, 1)])] };
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

    [Theory]
    [InlineData(0)]
    [InlineData(-20)]
    public async Task Actual_continuation_order_and_value_phases_survive_equal_or_regressing_clocks(int clockShift)
    {
        var fixture = new Fixture();
        var kinds = Policy.SupportedKinds.Concat(new[] { "ActivityContinuationStarted", "ActivityContinuationReturned", "ActivityContinuationCompleted" }).ToArray();
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v2", 2, true, kinds);
        var owner = Workflow() with { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = true };
        await fixture.Executions.SaveAsync(owner);
        var values = new[]
        {
            new ActivityExecutionInspectionValueSnapshot("private-input-name", ActivityExecutionInspectionValueSubject.ActivityInput, default, null,
                At.AddSeconds(5), JsonSerializer.SerializeToElement("private-token"), "test", true, new Dictionary<string, string>(),
                EvaluationId: "evaluation-in", Phase: "resume-input", EvidenceId: "input-evidence"),
            new ActivityExecutionInspectionValueSnapshot("private-output-name", ActivityExecutionInspectionValueSubject.ActivityOutput, default, null,
                At.AddSeconds(-5), JsonSerializer.SerializeToElement("private-output"), "test", true, new Dictionary<string, string>(),
                EvaluationId: "evaluation-out", Phase: "output", EvidenceId: "output-evidence")
        };
        var activity = Activity(ActivityExecutionStatus.Completed);
        var projection = ActivityExecutionInspectionProjection.FromState(activity, "checkpoint-owner", At, valueSnapshots: values);
        var source = Commit("owner", RuntimeCheckpointNames.ActivityCompleted, owner, activity) with
        {
            StateChanges = new(new("run", RuntimeStateChangeOperation.Upsert, owner, new Dictionary<string, string>()), null,
                [new("activity", RuntimeStateChangeOperation.Upsert, activity, new Dictionary<string, string>())], [], [], [], [],
                activityExecutionInspections: [new("activity", RuntimeStateChangeOperation.Upsert, projection, new Dictionary<string, string>())]),
            HistorySourceFacts = [
                new(RuntimeHistorySourceFactKind.ActivityContinuationStarted, "start", At, "activity", "attempt"),
                new(RuntimeHistorySourceFactKind.ActivityContinuationReturned, "returned", At.AddSeconds(clockShift), "activity", "attempt", reasonCode: "Complete"),
                new(RuntimeHistorySourceFactKind.ActivityContinuationCompleted, "done", At.AddSeconds(clockShift * 2), "activity", "attempt")]
        };
        var capture = new RuntimeHistoryCapture(fixture.Executions, new InMemoryActivityExecutionInspectionStore());
        var result = await capture.EnrichAsync(source);
        await capture.ValidateFinalAsync(result);
        var events = result.HistoryBatches!.Single().Candidates.Select(candidate => candidate.Event).ToArray();
        Assert.Equal(["ValueCaptured", "ActivityContinuationStarted", "ActivityContinuationReturned", "ValueCaptured", "ActivityContinuationCompleted", "ActivityCompleted"], events.Select(e => e.Name));
        Assert.Equal("resume-input", events[0].Metadata["history.valuePhase"]);
        Assert.Equal("evaluation-out", events[3].Metadata["history.valueEvaluationId"]);
        Assert.All(events.Where(e => e.ActivityExecutionId is not null), e => Assert.True(e.ActivityAncestryKnown));
        var serialized = JsonSerializer.Serialize(result.HistoryBatches);
        Assert.DoesNotContain("private-token", serialized);
        Assert.DoesNotContain("private-input-name", serialized);
    }

    [Fact]
    public async Task Pinned_v1_replay_does_not_gain_new_source_facts_or_ancestry_fields()
    {
        var fixture = new Fixture(); await fixture.Executions.SaveAsync(Workflow());
        var capture = new RuntimeHistoryCapture(fixture.Executions);
        var source = Commit("v1", RuntimeCheckpointNames.ActivityStarted, activity: Activity(ActivityExecutionStatus.Running));
        var original = await capture.EnrichAsync(source);
        var replay = await capture.EnrichAsync(source with { HistorySourceFacts =
            [new(RuntimeHistorySourceFactKind.ActivityContinuationStarted, "new-source", At, "activity")] });
        Assert.Null(Assert.Single(original.HistoryBatches!.Single().Candidates).Event.ActivityAncestryKnown);
        Assert.Null(replay.HistorySourceFacts);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(original), RuntimeCheckpointCommitFingerprint.Compute(replay));
    }

    [Fact]
    public async Task Missing_pinned_source_support_refuses_as_infrastructure_before_any_commit_or_business_fault()
    {
        var fixture = new Fixture();
        var owner = Workflow() with { HistoryCapturePolicy = new("runtime-history-v2", 2, true,
            Policy.SupportedKinds.Concat(["ActivityContinuationStarted"]).ToArray()) };
        await fixture.Executions.SaveAsync(owner);
        var capture = new RuntimeHistoryCapture(fixture.Executions, sourceSupport: []);
        Assert.False(capture.CanCapture(owner.HistoryCapturePolicy!));
        var unavailable = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () =>
            await fixture.Committer(capture).CommitAsync(Commit("unavailable", RuntimeCheckpointNames.ActivityStarted, activity: Activity(ActivityExecutionStatus.Running))));
        Assert.Equal("runtime.history.capture-unavailable", unavailable.Code);
        Assert.Empty(fixture.State.Commits);
        Assert.Empty(fixture.State.HistoryEvents);
        Assert.Equal(owner, await fixture.Executions.FindAsync("run"));
    }

    [Fact]
    public async Task Owner_observation_ack_loss_and_purge_replay_keep_durable_receipt_without_resurrection()
    {
        var fixture = new Fixture();
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v2", 2, true, Policy.SupportedKinds.Concat(RuntimeHistoryDeliveryFacts.Kinds).ToArray());
        var owner = Workflow() with { HistoryCapturePolicy = policy };
        await fixture.Executions.SaveAsync(owner);
        var initial = await new RuntimeHistoryCapture(fixture.Executions).EnrichAsync(Commit("admitted", RuntimeCheckpointNames.WorkflowStarted, owner));
        await fixture.Store.CommitAsync(initial, new(RuntimeCheckpointPersistenceMode.Immediate));
        var before = (await fixture.Reader.FindHeadAsync("run"))!;
        var writer = new InMemoryRuntimeHistoryWriter(fixture.State, fixture.Access, fixture.Executions, new FakeTimeProvider(At.AddMinutes(1)));
        var observation = RuntimeHistoryDeliveryFacts.Create(policy, "run", "receipt", "DeliveryReceived", At, "safe-stimulus");
        var first = await writer.RecordAsync(observation);
        var retryAfterLostAck = await writer.RecordAsync(observation);
        Assert.True(first.Recorded); Assert.True(retryAfterLostAck.Recorded);
        Assert.Equal(first.Event!.Sequence, retryAfterLostAck.Event!.Sequence);
        var after = (await fixture.Reader.FindHeadAsync("run"))!;
        Assert.Equal(before.LastPhysicalCommitId, after.LastPhysicalCommitId);
        Assert.Equal(before.LastCommittedAt, after.LastCommittedAt);
        Assert.Equal(after.LastSequence, await writer.PurgeThroughAsync("run", long.MaxValue));
        var purgedReplay = await writer.RecordAsync(observation);
        Assert.True(purgedReplay.Recorded); Assert.Null(purgedReplay.Event);
        Assert.Empty((await fixture.Reader.ReadPageAsync(new("run"))).Items);
        Assert.Equal(after.LastSequence, (await fixture.Reader.FindHeadAsync("run"))!.LastSequence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("parent")]
    public async Task Refused_first_checkpoint_retains_admitted_capture_without_inventing_workflow_started(string? parent)
    {
        var fixture = new Fixture();
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v2", 2, true, RuntimeHistoryCapture.Kinds.Concat(RuntimeHistoryCapture.Version2Kinds).ToArray());
        var payload = new WorkflowExecutionStartCommandPayload(Executable, Executable.ArtifactId,
            variables: null, inputs: null, stimulusInput: null, triggerNodeId: null, runKind: WorkflowRunKind.PublishedRun,
            pinnedSource: null, parentWorkflowExecutionId: parent, correlationId: null, tenantId: "scope-a",
            partition: null, authority: null)
        { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = true };
        var envelope = new WorkflowExecutionCommandEnvelope("start-envelope", "run",
            new("start-command", "run", WorkflowExecutionCommandKind.Start, At.AddHours(-1), JsonSerializer.SerializeToElement(payload), new Dictionary<string, string>()),
            "start-key", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, At.AddHours(-1));
        var faulter = new CheckpointRuleViolationWorkflowFaulter(fixture.Executions,
            fixture.Committer(new RuntimeHistoryCapture(fixture.Executions)), DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            new FakeTimeProvider(At), workflowDispatchStore: new InMemoryWorkflowDispatchStore());

        await faulter.FaultIfCheckpointRuleViolatedAsync("run", null,
            new RuntimeCheckpointCommitValidationException("refused first checkpoint"), envelope);

        var retained = (await fixture.Executions.FindAsync("run"))!;
        Assert.Equal(WorkflowExecutionStatus.Faulted, retained.Status);
        Assert.True(retained.HistoryCaptureEnabledAtAdmission);
        Assert.True(policy.IsEquivalentTo(retained.HistoryCapturePolicy));
        var page = await fixture.Reader.ReadPageAsync(new("run"));
        Assert.True(page.Head!.CapturedFromStart);
        Assert.Contains(page.Items, item => item.Name == "WorkflowFaulted");
        Assert.Contains(page.Items, item => item.Name == "IncidentRecorded");
        Assert.DoesNotContain(page.Items, item => item.Name == "WorkflowStarted");
        Assert.True(Assert.Single(fixture.Store.ListCommits()).Commit.Checkpoint.HistoryStartsAtAdmission);
    }

    [Fact]
    public async Task Durable_state_metadata_never_copies_values_names_schema_or_external_locators()
    {
        var fixture = new Fixture();
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v2", 2, true, RuntimeHistoryCapture.Version2Kinds);
        await fixture.Executions.SaveAsync(Workflow() with { HistoryCapturePolicy = policy });
        const string sentinel = "sensitive-durable-sentinel";
        var raw = JsonSerializer.SerializeToElement(new { secret = sentinel });
        var inline = new DurableValueState(sentinel + "-address", "run", sentinel + "-value-name", new(sentinel, sentinel, raw),
            DurableValueLifecycle.Instance, DurableValueStorage.Inline, raw, null, "activity", At.AddSeconds(-10), new Dictionary<string, string> { [sentinel] = sentinel });
        var external = new DurableValueState(sentinel + "-external-address", "run", sentinel + "-external-name", new(sentinel, sentinel, raw),
            DurableValueLifecycle.Audit, DurableValueStorage.External, null, new(sentinel, sentinel, new Dictionary<string, string> { [sentinel] = sentinel }),
            "activity", At.AddSeconds(-5), new Dictionary<string, string> { [sentinel] = sentinel });
        var source = Commit("state-metadata", RuntimeCheckpointNames.ActivityCompleted);
        var changes = new RuntimeCheckpointStateChangeSet(null, null, [], [],
            [new(inline.DurableValueId, RuntimeStateChangeOperation.Upsert, inline, new Dictionary<string, string>()),
             new(external.DurableValueId, RuntimeStateChangeOperation.Delete, external, new Dictionary<string, string>())], [], []);

        var captured = await new RuntimeHistoryCapture(fixture.Executions).EnrichAsync(source with { StateChanges = changes });

        var candidates = captured.HistoryBatches!.Single().Candidates;
        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, item => item.Event.Name == "DurableStateRecorded" && item.Event.OccurredAt == inline.CapturedAt);
        Assert.Contains(candidates, item => item.Event.Name == "DurableStateRemoved" && item.Event.OccurredAt == source.Checkpoint.OccurredAt);
        Assert.All(candidates, item => { Assert.Null(item.Event.Payload); Assert.False(item.Event.Metadata.ContainsKey("history.valueEvidenceIds")); Assert.Equal("activity", item.Event.ActivityExecutionId); });
        Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(candidates), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timer_append_ack_loss_and_metadata_ack_preserve_claim_and_do_not_resurrect_retired_source()
    {
        var fixture = new Fixture();
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v2", 2, true,
            RuntimeHistoryCapture.Kinds.Concat(DurableTimerHistoryFacts.Kinds).ToArray());
        await fixture.Executions.SaveAsync(Workflow() with { HistoryCapturePolicy = policy });
        var timers = new InMemoryDurableTimerStore(new InMemoryDurableTimerStoreState(), fixture.Access, new FakeTimeProvider(At));
        var timer = new DurableTimer("timer", "run", "timer-stimulus", "capability-sentinel", At, At, ActivityExecutionId: "activity");
        var scheduled = DurableTimerHistoryFacts.CreateScheduled(timer, policy)!;
        timer = timer with { HistoryResponsibility = new(policy, [scheduled.Event], false) };
        await timers.SaveAsync(timer);
        var writer = new InMemoryRuntimeHistoryWriter(fixture.State, fixture.Access, fixture.Executions, new FakeTimeProvider(At));
        Assert.False((await writer.RecordAsync(scheduled)).Recorded); // Source exists before the bookmark/native boundary.
        Assert.True(await timers.HasPendingHistoryAsync("run"));
        var initial = await new RuntimeHistoryCapture(fixture.Executions).EnrichAsync(Commit("timer-start", RuntimeCheckpointNames.WorkflowStarted, workflow: Workflow() with { HistoryCapturePolicy = policy }));
        await fixture.Store.CommitAsync(initial, new(RuntimeCheckpointPersistenceMode.Immediate));
        var claim = Assert.Single(await timers.ClaimDueAsync(new("pump", At, TimeSpan.FromMinutes(1), 1)));
        Assert.True((await writer.RecordAsync(scheduled)).Recorded);
        // Simulate process failure between durable append and source acknowledgment.
        Assert.True((await writer.RecordAsync(scheduled)).Recorded);
        Assert.True(await timers.AcknowledgeHistoryAsync("run", "timer", scheduled.Event.HistoryEventId));
        var dispatched = DurableTimerHistoryFacts.CreateDispatched(claim.Timer, At, BookmarkResumeDispatchStatus.Dispatched)!;
        Assert.True((await timers.CompleteClaimWithHistoryAsync(claim, dispatched)).Succeeded);
        Assert.Null(await timers.FindAsync("run", "timer"));
        Assert.True(await timers.HasPendingHistoryAsync("run"));
        Assert.True((await writer.RecordAsync(dispatched)).Recorded);
        Assert.True(await timers.AcknowledgeHistoryAsync("run", "timer", dispatched.Event.HistoryEventId));
        Assert.False(await timers.HasPendingHistoryAsync("run"));
        await timers.SaveAsync(timer); // Late deterministic replay cannot restore the deadline/responsibility.
        Assert.Null(await timers.FindAsync("run", "timer"));
        Assert.Empty(await timers.ClaimDueAsync(new("pump-retry", At.AddHours(1), TimeSpan.FromMinutes(1), 1)));
        var page = await fixture.Reader.ReadPageAsync(new("run"));
        Assert.Single(page.Items, item => item.Name == "TimerScheduled");
        Assert.Single(page.Items, item => item.Name == "TimerDispatched");
        Assert.DoesNotContain("capability-sentinel", JsonSerializer.Serialize(page), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_scope_inventory_retires_bookmark_and_preserves_supersession_successor(bool superseded)
    {
        var fixture = new Fixture();
        var policy = new RuntimeHistoryCapturePolicy("runtime-history-v2", 2, true,
            RuntimeHistoryCapture.Kinds.Concat(RuntimeHistoryCapture.Version2Kinds).ToArray());
        await fixture.Executions.SaveAsync(Workflow() with { HistoryCapturePolicy = policy });
        var capture = new RuntimeHistoryCapture(fixture.Executions);
        var bookmark = Bookmark("scope-bookmark");
        var created = await capture.EnrichAsync(Commit("scope-wait", RuntimeCheckpointNames.ActivitySuspended, workflow: Workflow() with { HistoryCapturePolicy = policy },
            activity: Activity(ActivityExecutionStatus.Suspended), bookmarks: [new(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, new Dictionary<string, string>())]));
        await fixture.Store.CommitAsync(created, new(RuntimeCheckpointPersistenceMode.Immediate));
        var cleanup = await fixture.Cleanup.CaptureAsync("run", "activity", new HashSet<string>(StringComparer.Ordinal) { "activity" });
        Assert.Equal("scope-bookmark", Assert.Single(cleanup.HistoryBookmarks!).Identity.BookmarkId);
        var owner = Activity(superseded ? ActivityExecutionStatus.Superseded : ActivityExecutionStatus.Cancelled) with
        { SupersededByActivityExecutionId = superseded ? "successor" : null, SupersededAt = superseded ? At : null };
        var source = Commit("scope-ended", RuntimeCheckpointNames.ActivityCancelled, activity: owner);
        var changes = new RuntimeCheckpointStateChangeSet(null, null, source.StateChanges.ActivityExecutions, [], [], [], [], activityScopeCleanups: [cleanup]);

        var ended = await capture.EnrichAsync(source with { StateChanges = changes });
        await fixture.Store.CommitAsync(ended, new(RuntimeCheckpointPersistenceMode.Immediate));
        await fixture.Store.CommitAsync(ended, new(RuntimeCheckpointPersistenceMode.Immediate));

        Assert.Null(await fixture.Bookmarks.FindAsync("run", "scope-bookmark"));
        var projection = (await fixture.Reader.FindBookmarkAsync("run", "scope-bookmark"))!;
        Assert.Equal(superseded ? RuntimeHistoryBookmarkStatus.Superseded : RuntimeHistoryBookmarkStatus.Removed, projection.Status);
        Assert.Equal(superseded ? "successor" : null, projection.SupersededByActivityExecutionId);
        var journal = await fixture.Reader.ReadPageAsync(new("run", bookmarkId: "scope-bookmark"));
        Assert.Equal(["BookmarkCreated", superseded ? "BookmarkSuperseded" : "BookmarkRemoved"], journal.Items.Select(item => item.Name));
        Assert.DoesNotContain(journal.Items, item => item.Name == "BookmarkConsumed");
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
        internal InMemoryBookmarkStateStore Bookmarks { get; } = new();
        internal ActivityScopeCleanupStore Cleanup { get; }
        internal InMemoryRuntimeCheckpointCommitStore Store { get; }
        internal InMemoryRuntimeHistoryStore Reader { get; }
        internal Fixture()
        {
            Executions = new(State, null);
            Cleanup = new(Bookmarks, new InMemoryDurableTimerStore(), new InMemoryWorkflowSchedulerWorkQueue(), Executions);
            Store = new(workflowExecutionStateStore: Executions, bookmarkStateStore: Bookmarks, activityScopeCleanupStore: Cleanup,
                state: State, persistenceAccessContextAccessor: Access, rootWriteLeaseManager: new Lease());
            Reader = new(State, Access, Codec());
        }
        internal RuntimeCheckpointCommitter Committer(IRuntimeCheckpointCommitEnricher capture, IRuntimeHistoryStore? reader = null) =>
            new(new ImmediateRuntimeCheckpointPersistencePolicy(), Store, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [capture], []) { HistoryStore = reader ?? Reader };
    }
}
