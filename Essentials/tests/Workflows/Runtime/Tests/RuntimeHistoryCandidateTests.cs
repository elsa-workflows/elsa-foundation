using Elsa.Primitives.Models;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Candidate/source validation only. The writer throws if called; atomic/provider proof belongs to its own slice.</summary>
public sealed class RuntimeHistoryCandidateTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static RuntimeHistoryCapturePolicy Policy(params string[] kinds) => new("runtime-history-v3", 3, true, kinds);

    [Fact]
    public async Task Retained_legacy_absence_wins_over_proposed_capture_and_drops_unadmitted_source_inputs()
    {
        using var fixture = await Fixture.Create(null);
        var proposed = Workflow(Policy("WorkflowStarted"));
        var raw = Commit("legacy", RuntimeCheckpointNames.WorkflowStarted, workflow: proposed) with
        { HistorySourceFacts = [new(RuntimeHistorySourceFactKind.ControlApplied, "unadmitted", At)] };
        var result = await fixture.Capture.EnrichAsync(raw);
        Assert.Null(result.HistoryBatches);
        Assert.Null(result.HistorySourceFacts);
    }

    [Fact]
    public async Task Missing_native_row_uses_the_actual_proposed_start_policy_and_creates_one_unpositioned_group()
    {
        using var fixture = await Fixture.Create(null, saveNative: false);
        var raw = Commit("start", RuntimeCheckpointNames.WorkflowStarted, workflow: Workflow(Policy("WorkflowStarted")));
        var captured = await fixture.Capture.EnrichAsync(raw);
        await fixture.Capture.ValidateFinalAsync(captured);
        var batch = Assert.Single(captured.HistoryBatches!);
        var candidate = Assert.Single(batch.Candidates);
        Assert.True(batch.StartsAtRunStart);
        Assert.Equal("WorkflowStarted", candidate.Event.Name);
        Assert.Equal(0, candidate.Event.Sequence);
        Assert.Null(candidate.Event.PhysicalCommitId);
        Assert.Null(candidate.Event.RecordedAt);
        Assert.Equal(RuntimeHistoryAuthority.CommittedTransition, candidate.Event.Authority);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Final_validation_refuses_changed_source_or_a_removed_required_batch(bool removeBatch)
    {
        using var fixture = await Fixture.Create(Policy("ActivityStarted"));
        var captured = await fixture.Capture.EnrichAsync(Commit("source", RuntimeCheckpointNames.ActivityStarted, activity: Activity(ActivityExecutionStatus.Running)));
        var changed = removeBatch ? captured with { HistoryBatches = null } : captured with { Metadata = new Dictionary<string, string> { ["changed"] = "source" } };
        if (removeBatch)
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await fixture.Capture.ValidateFinalAsync(changed));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Capture.ValidateFinalAsync(changed));
    }

    [Theory]
    [InlineData("coverage")]
    [InlineData("time")]
    [InlineData("empty-checkpoint")]
    [InlineData("offset")]
    public async Task Final_validation_refuses_replaced_batch_headers_even_when_candidate_content_is_unchanged(string changed)
    {
        var empty = changed == "empty-checkpoint";
        using var fixture = await Fixture.Create(Policy("ActivityStarted"));
        var captured = await fixture.Capture.EnrichAsync(Commit("headers", empty ? "no-transition" : RuntimeCheckpointNames.ActivityStarted,
            activity: empty ? null : Activity(ActivityExecutionStatus.Running)));
        var original = Assert.Single(captured.HistoryBatches!);
        var replacement = new RuntimeHistoryBatch(original.Policy,
            empty ? "different-checkpoint" : original.LogicalCheckpointId,
            changed == "time" ? original.OccurredAt.AddMinutes(1) : changed == "offset" ? original.OccurredAt.ToOffset(TimeSpan.FromHours(1)) : original.OccurredAt,
            changed == "coverage" || original.StartsAtRunStart, original.Candidates) { SourceFingerprint = original.SourceFingerprint };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Capture.ValidateFinalAsync(captured with { HistoryBatches = [replacement] }));
    }

    [Theory]
    [InlineData("coverage")] [InlineData("missing")] [InlineData("false-marker")]
    public async Task Unprepared_flush_metadata_cannot_bypass_final_history_proof(string changed)
    {
        using var fixture = await Fixture.Create(Policy("ActivityStarted"));
        var captured = await fixture.Capture.EnrichAsync(Commit("spoofed-flush", RuntimeCheckpointNames.ActivityStarted,
            activity: Activity(ActivityExecutionStatus.Running)));
        var original = Assert.Single(captured.HistoryBatches!);
        var replacement = new RuntimeHistoryBatch(original.Policy, original.LogicalCheckpointId, original.OccurredAt,
            true, original.Candidates) { SourceFingerprint = original.SourceFingerprint };
        var metadata = new Dictionary<string, string>(captured.Checkpoint.Metadata)
            { ["runtime.coalescing.flush"] = changed == "false-marker" ? "false" : "true" };
        var spoofed = captured with
        {
            Checkpoint = captured.Checkpoint with { Metadata = metadata },
            HistoryBatches = changed == "missing" ? null : [replacement]
        };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Capture.ValidateFinalAsync(spoofed));
    }

    [Theory]
    [InlineData("valid")] [InlineData("coverage")] [InlineData("missing")]
    [InlineData("unprepared")] [InlineData("false-marker")]
    public async Task Prepared_flush_keeps_its_actual_original_history_groups(string changed)
    {
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        using var fixture = await Fixture.Create(Policy("ActivityStarted"), coalescing: accessor);
        var logical = await fixture.Capture.EnrichAsync(Commit("buffered", RuntimeCheckpointNames.ActivityStarted,
            activity: Activity(ActivityExecutionStatus.Running)));
        var session = new RuntimeCoalescingSession("run", new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions());
        session.BufferDeferred(logical);
        var flush = Commit("prepared", RuntimeCoalescingMetadataKeys.FlushCheckpointName) with
        {
            StateChanges = session.FoldBufferedStateChanges(),
            HistoryBatches = session.FoldHistoryBatchesWith()
        };
        flush = flush with { Checkpoint = flush.Checkpoint with
        {
            Metadata = new Dictionary<string, string> { [RuntimeCoalescingMetadataKeys.CoalescedFlush] = changed == "false-marker" ? "false" : "true" }
        } };
        if (changed != "unprepared") session.PrepareQuiescenceFlush(flush);
        using var current = accessor.Push(session);
        if (changed == "coverage")
        {
            var original = Assert.Single(flush.HistoryBatches!);
            flush = flush with { HistoryBatches = [new RuntimeHistoryBatch(original.Policy, original.LogicalCheckpointId,
                original.OccurredAt, true, original.Candidates) { SourceFingerprint = original.SourceFingerprint }] };
        }
        else if (changed == "missing") flush = flush with { HistoryBatches = null };
        if (changed == "valid")
        {
            Assert.Same(flush, await fixture.Capture.EnrichAsync(flush));
            await fixture.Capture.ValidateFinalAsync(flush);
        }
        else await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Capture.ValidateFinalAsync(flush));
    }

    [Theory]
    [InlineData("empty-state")] [InlineData("late-value")]
    public async Task Prepared_flush_refuses_native_payload_replacement(string change)
    {
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        using var fixture = await Fixture.Create(Policy("ActivityStarted", "ValueCaptured"), coalescing: accessor);
        var logical = await fixture.Capture.EnrichAsync(Commit("buffered-native", RuntimeCheckpointNames.ActivityStarted,
            activity: Activity(ActivityExecutionStatus.Running)));
        var session = new RuntimeCoalescingSession("run", new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions());
        session.BufferDeferred(logical);
        var flush = Commit("prepared-native", RuntimeCoalescingMetadataKeys.FlushCheckpointName) with
        {
            StateChanges = session.FoldBufferedStateChanges(),
            HistoryBatches = session.FoldHistoryBatchesWith()
        };
        flush = flush with { Checkpoint = flush.Checkpoint with { Metadata = new Dictionary<string, string>
            { [RuntimeCoalescingMetadataKeys.CoalescedFlush] = "true" } } };
        session.PrepareQuiescenceFlush(flush);
        using var current = accessor.Push(session);
        if (change == "empty-state") flush = flush with { StateChanges = new(null, null, [], [], [], [], []) };
        else
        {
            var state = Assert.Single(flush.StateChanges.ActivityExecutions).State;
            var evidence = new ActivityExecutionInspectionValueSnapshot("late-evidence", ActivityExecutionInspectionValueSubject.ActivityInput,
                default, null, At, JsonSerializer.SerializeToElement("private"), "test", false,
                new Dictionary<string, string>(), EvidenceId: "late-evidence");
            var projection = ActivityExecutionInspectionProjection.FromState(state, logical.Checkpoint.CheckpointId, At,
                valueSnapshots: [evidence]) with { CheckpointValueCapture = new(logical.Checkpoint.CheckpointId, "work", ["late-evidence"]) };
            flush = flush with { StateChanges = new(null, null, flush.StateChanges.ActivityExecutions, [], [], [], [],
                activityExecutionInspections: [new("activity", RuntimeStateChangeOperation.Upsert, projection, new Dictionary<string, string>())]) };
        }
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Capture.ValidateFinalAsync(flush));
        Assert.True(session.HasBufferedChanges);
        Assert.True(session.IsActive);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Prepared_flush_preserves_exact_native_outcome_code_units(bool changed)
    {
        var original = new string((char)0xd800, 1);
        var replacement = new string((char)0xd801, 1);
        var accessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        using var fixture = await Fixture.Create(Policy("ActivityCompleted", "ActivityContinuationCompleted"), coalescing: accessor);
        ActivityCompletion Completion(string outcome) => new("activity", "attempt",
            ValueEnvelope.Null(new ValueTypeDescriptor("test/result"), ValueProtectionPolicy.InstanceInline), outcome, At.AddSeconds(5), "contract");
        var state = Activity(ActivityExecutionStatus.Completed) with { Completion = Completion(original) };
        var logical = await fixture.Capture.EnrichAsync(Commit("buffered-exact", RuntimeCheckpointNames.ActivityCompleted, activity: state) with
        {
            HistorySourceFacts = [new(RuntimeHistorySourceFactKind.ActivityContinuationCompleted, "activity", At.AddSeconds(5),
                activityExecutionId: "activity", outcomeNames: [original])]
        });
        var session = new RuntimeCoalescingSession("run", new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions());
        session.BufferDeferred(logical);
        var flush = Commit("prepared-exact", RuntimeCoalescingMetadataKeys.FlushCheckpointName) with
        { StateChanges = session.FoldBufferedStateChanges(), HistoryBatches = session.FoldHistoryBatchesWith() };
        flush = flush with { Checkpoint = flush.Checkpoint with { Metadata = new Dictionary<string, string>
            { [RuntimeCoalescingMetadataKeys.CoalescedFlush] = "true" } } };
        session.PrepareQuiescenceFlush(flush);
        using var current = accessor.Push(session);
        if (changed)
        {
            var native = Assert.Single(flush.StateChanges.ActivityExecutions);
            flush = flush with { StateChanges = new(null, null,
                [native with { State = native.State with { Completion = Completion(replacement) } }], [], [], [], []) };
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Capture.ValidateFinalAsync(flush));
        }
        else await fixture.Capture.ValidateFinalAsync(flush);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Logical_checkpoint_preserves_exact_native_outcome_code_units(bool changed)
    {
        var original = new string((char)0xd800, 1);
        var replacement = new string((char)0xd801, 1);
        using var fixture = await Fixture.Create(Policy("ActivityCompleted", "ActivityContinuationCompleted"));
        ActivityCompletion Completion(string outcome) => new("activity", "attempt",
            ValueEnvelope.Null(new ValueTypeDescriptor("test/result"), ValueProtectionPolicy.InstanceInline), outcome, At.AddSeconds(5), "contract");
        var state = Activity(ActivityExecutionStatus.Completed) with { Completion = Completion(original) };
        var captured = await fixture.Capture.EnrichAsync(Commit("logical-exact", RuntimeCheckpointNames.ActivityCompleted, activity: state) with
        {
            HistorySourceFacts = [new(RuntimeHistorySourceFactKind.ActivityContinuationCompleted, "activity", At.AddSeconds(5),
                activityExecutionId: "activity", outcomeNames: [original])]
        });
        if (changed)
        {
            var native = Assert.Single(captured.StateChanges.ActivityExecutions);
            captured = captured with { StateChanges = new(null, null,
                [native with { State = native.State with { Completion = Completion(replacement) } }], [], [], [], []) };
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Capture.ValidateFinalAsync(captured));
        }
        else await fixture.Capture.ValidateFinalAsync(captured);
    }

    [Fact]
    public async Task An_undeclared_kind_or_different_selected_source_bridge_cannot_qualify_capture()
    {
        using var fixture = await Fixture.Create(Policy("Future.Kind"));
        Assert.False(fixture.Capture.CanCapture(Policy("Future.Kind")));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await fixture.Capture.EnrichAsync(Commit("unsupported", "test")));
        using var mismatched = await Fixture.Create(Policy("WorkflowStarted"), matchedSource: false);
        Assert.False(mismatched.Capture.CanCapture(Policy("WorkflowStarted")));
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await mismatched.Capture.EnrichAsync(Commit("mismatch", "test")));
    }

    [Fact]
    public async Task Value_owner_order_and_container_observation_follow_the_actual_callback_boundary_without_payload_copying()
    {
        using var fixture = await Fixture.Create(Policy("ValueCaptured", "ActivityContinuationStarted", "ActivityContinuationReturned", "ActivityContinuationCompleted"));
        var state = Activity(ActivityExecutionStatus.Completed);
        var raw = Commit("values", RuntimeCheckpointNames.ActivityCompleted, activity: state);
        ActivityExecutionInspectionValueSnapshot Value(string id, ActivityExecutionInspectionValueSubject subject, int second) =>
            new(id, subject, default, null, At.AddSeconds(second), JsonSerializer.SerializeToElement("never-copy"), "test", false,
                new Dictionary<string, string>(), EvidenceId: id);
        var projection = ActivityExecutionInspectionProjection.FromState(state, raw.Checkpoint.CheckpointId, At,
            valueSnapshots: [Value("z-first", ActivityExecutionInspectionValueSubject.ActivityInput, 0),
                Value("a-second", ActivityExecutionInspectionValueSubject.ActivityInput, 0),
                Value("variable", ActivityExecutionInspectionValueSubject.ContainerVariable, 3)]) with
        { CheckpointValueCapture = new(raw.Checkpoint.CheckpointId, "work", ["z-first", "a-second", "variable"]) };
        raw = raw with
        {
            StateChanges = new(null, null, raw.StateChanges.ActivityExecutions, [], [], [], [],
                activityExecutionInspections: [new("activity", RuntimeStateChangeOperation.Upsert, projection, new Dictionary<string, string>())]),
            HistorySourceFacts = [Fact(RuntimeHistorySourceFactKind.ActivityContinuationStarted, 1), Fact(RuntimeHistorySourceFactKind.ActivityContinuationReturned, 2),
                Fact(RuntimeHistorySourceFactKind.ActivityContinuationCompleted, 4)]
        };
        var events = Assert.Single((await fixture.Capture.EnrichAsync(raw)).HistoryBatches!).Candidates.Select(candidate => candidate.Event).ToArray();
        Assert.Equal(new[] { "ValueCaptured", "ValueCaptured", "ActivityContinuationStarted", "ActivityContinuationReturned", "ValueCaptured", "ActivityContinuationCompleted" }, events.Select(e => e.Name));
        Assert.Equal(new[] { "z-first", "a-second", "variable" }, events.Where(e => e.Name == "ValueCaptured")
            .Select(e => JsonSerializer.Deserialize<string[]>(e.Metadata["history.valueEvidenceIds"])![0]));
        Assert.All(events, e => { Assert.Null(e.Payload); Assert.DoesNotContain("never-copy", string.Join(" ", e.Metadata.Values)); });
    }

    [Fact]
    public async Task Metadata_only_policy_does_not_demand_unselected_value_evidence_from_a_legacy_inspection_owner()
    {
        using var fixture = await Fixture.Create(Policy("ActivityStarted"), values: false);
        var state = Activity(ActivityExecutionStatus.Running);
        var raw = Commit("metadata", RuntimeCheckpointNames.ActivityStarted, activity: state);
        var projection = ActivityExecutionInspectionProjection.FromState(state, raw.Checkpoint.CheckpointId, At,
            valueSnapshots: [new("legacy", ActivityExecutionInspectionValueSubject.ActivityInput, default, null, At, null, "metadata", false, new Dictionary<string, string>())]);
        raw = raw with { StateChanges = new(null, null, raw.StateChanges.ActivityExecutions, [], [], [], [],
            activityExecutionInspections: [new("activity", RuntimeStateChangeOperation.Upsert, projection, new Dictionary<string, string>())]) };
        Assert.Equal("ActivityStarted", Assert.Single(Assert.Single((await fixture.Capture.EnrichAsync(raw)).HistoryBatches!).Candidates).Event.Name);
    }

    [Fact]
    public async Task Value_capture_refuses_a_missing_or_dangling_original_delta_instead_of_querying_newer_values()
    {
        using var fixture = await Fixture.Create(Policy("ValueCaptured"));
        var state = Activity(ActivityExecutionStatus.Running);
        var raw = Commit("missing", "test", activity: state);
        var projection = ActivityExecutionInspectionProjection.FromState(state, raw.Checkpoint.CheckpointId, At,
            valueSnapshots: [new("input", ActivityExecutionInspectionValueSubject.ActivityInput, default, null, At, null, "metadata", false, new Dictionary<string, string>())]);
        RuntimeCheckpointCommit With(ActivityExecutionInspectionProjection value) => raw with
        { StateChanges = new(null, null, raw.StateChanges.ActivityExecutions, [], [], [], [],
            activityExecutionInspections: [new("activity", RuntimeStateChangeOperation.Upsert, value, new Dictionary<string, string>())]) };
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await fixture.Capture.EnrichAsync(With(projection)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Capture.EnrichAsync(With(projection with
            { CheckpointValueCapture = new(raw.Checkpoint.CheckpointId, "work", ["missing-evidence"]) })));
    }

    [Fact]
    public async Task Reconciliation_preserves_unknown_occurrence_in_the_candidate_and_positioned_event()
    {
        using var fixture = await Fixture.Create(Policy("IncidentProjectionReconciled"));
        var incident = new IncidentState("incident", "run", "activity", "node", IncidentSeverity.Error, IncidentStatus.Blocking,
            null, "failure", "private exception", At, null);
        var raw = Commit("repair", RuntimeCheckpointNames.IncidentRecorded, activity: Activity(ActivityExecutionStatus.Running)) with
        { HistorySourceFacts = [RuntimeHistoryIncidentFacts.ProjectionReconciled(incident, At)] };
        var e = Assert.Single(Assert.Single((await fixture.Capture.EnrichAsync(raw)).HistoryBatches!).Candidates).Event;
        Assert.False(e.OccurredAtKnown);
        Assert.False(RuntimeHistoryIdentity.Position(e, 1, "physical", At.AddMinutes(5)).OccurredAtKnown);
        Assert.DoesNotContain("private exception", string.Join(" ", e.Metadata.Values));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_requires_and_snapshots_its_exact_bookmark_inventory_when_disposition_is_selected(bool supplied)
    {
        using var fixture = await Fixture.Create(Policy("BookmarkRemoved"));
        var summary = new ActivityExecutionBookmarkSummary(new("bookmark", "run", "activity", "continue"), "task", "hash", At, null);
        var suppliedList = new List<ActivityExecutionBookmarkSummary> { summary };
        var cleanup = new ActivityScopeCleanupRequest("run", "activity", ["activity"], ["bookmark"], [], [])
        { HistoryBookmarks = supplied ? suppliedList : null };
        suppliedList.Clear();
        var raw = Commit("cleanup", "test") with { StateChanges = new(null, null, [], [], [], [], [], activityScopeCleanups: [cleanup]) };
        if (!supplied)
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await fixture.Capture.EnrichAsync(raw));
        else
        {
            var candidate = Assert.Single(Assert.Single((await fixture.Capture.EnrichAsync(raw)).HistoryBatches!).Candidates);
            Assert.Equal("BookmarkRemoved", candidate.Event.Name);
            Assert.Equal("bookmark", candidate.BookmarkMutation!.Bookmark.Identity.BookmarkId);
            Assert.Single(cleanup.HistoryBookmarks!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Incident_projection_requires_actual_owner_decision_and_validates_its_native_time(bool wrongTime)
    {
        using var fixture = await Fixture.Create(Policy("IncidentRecorded"));
        var incident = new IncidentState("incident", "run", "activity", "node", IncidentSeverity.Error, IncidentStatus.Blocking,
            null, "failure", "private exception", At, null);
        var raw = Commit("incident", RuntimeCheckpointNames.IncidentRecorded) with
        { StateChanges = new(null, null, [], [], [], [new("incident", RuntimeStateChangeOperation.Upsert, incident, new Dictionary<string, string>())], []) };
        if (!wrongTime)
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(async () => await fixture.Capture.EnrichAsync(raw));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Capture.EnrichAsync(raw with
            { HistorySourceFacts = [new(RuntimeHistorySourceFactKind.IncidentCreated, "incident", At.AddMinutes(1), "activity", incidentId: "incident")] }));
    }

    [Fact]
    public async Task An_omitted_over_budget_native_action_label_still_matches_its_actual_decision()
    {
        using var fixture = await Fixture.Create(Policy("IncidentStrategyEvaluated"));
        var incident = new IncidentState("incident", "run", "activity", "node", IncidentSeverity.Error, IncidentStatus.Blocking,
            new(new string('A', 512), At, null, "test"), "failure", "private exception", At, null);
        var raw = Commit("decision", RuntimeCheckpointNames.IncidentResolutionBatchApplied) with
        {
            StateChanges = new(null, null, [], [], [], [new("incident", RuntimeStateChangeOperation.Upsert, incident, new Dictionary<string, string>())], []),
            HistorySourceFacts = [RuntimeHistoryIncidentFacts.StrategyEvaluated(incident)]
        };
        var e = Assert.Single(Assert.Single((await fixture.Capture.EnrichAsync(raw)).HistoryBatches!).Candidates).Event;
        Assert.Equal("IncidentStrategyEvaluated", e.Name);
        Assert.False(e.Metadata.ContainsKey("history.reasonCode"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task First_fault_start_marker_requires_the_actual_known_admission_shape(bool known)
    {
        var policy = Policy("WorkflowFaulted");
        using var fixture = await Fixture.Create(policy, saveNative: false);
        var raw = Commit("first-fault", RuntimeCheckpointNames.WorkflowFaulted,
            workflow: Workflow(policy) with { Status = WorkflowExecutionStatus.Faulted, HistoryCaptureEnabledAtAdmission = known, CompletedAt = At.AddSeconds(5) });
        raw = raw with { Checkpoint = raw.Checkpoint with { HistoryStartsAtAdmission = true } };
        if (!known)
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Capture.EnrichAsync(raw));
        else Assert.True(Assert.Single((await fixture.Capture.EnrichAsync(raw)).HistoryBatches!).StartsAtRunStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Normalized_native_policy_read_keeps_unavailability_distinct_from_corruption(bool malformed)
    {
        var cause = new InvalidOperationException("native context", malformed ? new InvalidDataException("corrupt") : new IOException("unavailable"));
        using var fixture = await Fixture.Create(null, saveNative: false, executionOverride: new FailedRead(cause));
        var caught = await Record.ExceptionAsync(async () => await fixture.Capture.EnrichAsync(Commit("read", "test")));
        if (malformed) Assert.Same(cause, Assert.IsType<RuntimeHistoryIntegrityException>(caught).InnerException);
        else Assert.Same(cause, Assert.IsType<RuntimeSchedulerWorkUnavailableException>(caught).InnerException);
    }

    [Theory]
    [InlineData(ActivityExecutionStatus.Faulted)] [InlineData(ActivityExecutionStatus.Completed)] [InlineData(ActivityExecutionStatus.Cancelled)]
    public async Task Parent_completion_ignores_terminal_child_marker_or_inbox_updates_but_retains_real_subtree_cancellation(ActivityExecutionStatus status)
    {
        using var fixture = await Fixture.Create(Policy("ActivityCompleted", "ActivityFaulted", "ActivityCancelled"));
        var parent = Activity(ActivityExecutionStatus.Completed) with { Execution = new("parent", "run", "node", "authored", "test", "1") };
        var metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.ActivityExecutionId] = "parent", [RuntimeMetadataKeys.SchedulerWorkItemId] = "work" };
        var child = Activity(status) with
        {
            Execution = new("child", "run", "child-node", "child-authored", "test", "1"), ParentActivityExecutionId = "parent",
            CompletedAt = status == ActivityExecutionStatus.Cancelled ? At.AddSeconds(5) : At,
            Metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId] = "work" },
            ChildControlInbox = status == ActivityExecutionStatus.Completed
                ? new(1, new("run", "child", "parent", Workflow(null).PinnedExecutable, "root", "consumer", 1), 1, true) : null
        };
        var raw = Commit("parent-completion", RuntimeCheckpointNames.ActivityCompleted);
        raw = raw with
        {
            Checkpoint = raw.Checkpoint with { ActivityExecutionIds = ["parent", "child"], Metadata = metadata }, Metadata = metadata,
            StateChanges = new(null, null,
                [new("parent", RuntimeStateChangeOperation.Upsert, parent, metadata), new("child", RuntimeStateChangeOperation.Upsert, child, metadata)], [], [], [], [])
        };
        var captured = await fixture.Capture.EnrichAsync(raw); await fixture.Capture.ValidateFinalAsync(captured);
        var events = Assert.Single(captured.HistoryBatches!).Candidates.Select(candidate => candidate.Event).ToArray();
        Assert.Single(events, item => item.Name == "ActivityCompleted" && item.ActivityExecutionId == "parent");
        var childEvents = events.Where(item => item.ActivityExecutionId == "child").ToArray();
        if (status == ActivityExecutionStatus.Cancelled) Assert.Equal("ActivityCancelled", Assert.Single(childEvents).Name);
        else Assert.Empty(childEvents);
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, true)]
    public async Task Evidence_reference_preserves_exact_valid_identity_or_refuses_an_unrepresentable_identifier(int identity, bool representable)
    {
        // Construct opaque UTF-16 inside execution; test-discovery JSON also normalizes invalid surrogates.
        var evidence = identity < 2 ? new string((char)(0xd800 + identity), 1) : "evidence-\U0001F9EA";
        using var fixture = await Fixture.Create(Policy("ValueCaptured"));
        var state = Activity(ActivityExecutionStatus.Running);
        var raw = Commit("exact-value-reference", "values", activity: state);
        var snapshot = new ActivityExecutionInspectionValueSnapshot("input", ActivityExecutionInspectionValueSubject.ActivityInput,
            default, null, At, JsonSerializer.SerializeToElement("private"), "test", false, new Dictionary<string, string>(), EvidenceId: evidence);
        var projection = ActivityExecutionInspectionProjection.FromState(state, raw.Checkpoint.CheckpointId, At, valueSnapshots: [snapshot]) with
            { CheckpointValueCapture = new(raw.Checkpoint.CheckpointId, "work", [evidence]) };
        raw = raw with { StateChanges = new(null, null, raw.StateChanges.ActivityExecutions, [], [], [], [],
            activityExecutionInspections: [new("activity", RuntimeStateChangeOperation.Upsert, projection, new Dictionary<string, string>())]) };
        if (!representable)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Capture.EnrichAsync(raw));
            return;
        }
        var captured = await fixture.Capture.EnrichAsync(raw); await fixture.Capture.ValidateFinalAsync(captured);
        var value = Assert.Single(Assert.Single(captured.HistoryBatches!).Candidates).Event;
        Assert.Equal(evidence, Assert.Single(JsonSerializer.Deserialize<string[]>(value.Metadata["history.valueEvidenceIds"])!));
    }

    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, true)] [InlineData(3, true)]
    public async Task Outcome_metadata_preserves_exact_names_or_explicitly_omits_unrepresentable_content(int identity, bool representable)
    {
        // Construct malformed UTF-16 after discovery, which otherwise substitutes these distinct code units.
        var name = identity < 2 ? new string((char)(0xd800 + identity), 1) : identity == 2 ? "done-\U0001F9EA" : "Approved";
        using var fixture = await Fixture.Create(Policy("ActivityContinuationCompleted"));
        var raw = Commit("outcome", RuntimeCheckpointNames.ActivityCompleted, activity: Activity(ActivityExecutionStatus.Completed)) with
        {
            HistorySourceFacts = [new(RuntimeHistorySourceFactKind.ActivityContinuationCompleted, "activity", At.AddSeconds(5),
                activityExecutionId: "activity", outcomeNames: [name])]
        };
        var captured = await fixture.Capture.EnrichAsync(raw); await fixture.Capture.ValidateFinalAsync(captured);
        var batch = Assert.Single(captured.HistoryBatches!); var candidate = Assert.Single(batch.Candidates); var e = candidate.Event;
        if (representable)
            Assert.Equal(name, Assert.Single(JsonSerializer.Deserialize<string[]>(e.Metadata["history.outcomeNames"])!));
        else
        {
            Assert.False(e.Metadata.ContainsKey("history.outcomeNames"));
            Assert.Equal("unrepresentable", e.Metadata["history.outcomeCaptureState"]);
            Assert.Equal("1", e.Metadata["history.outcomeCount"]);
            Assert.Equal(RuntimeHistoryIdentity.CreateBinding([name]), e.Metadata["history.outcomeContentHash"]);
            var serialized = JsonSerializer.SerializeToNode(e)!;
            serialized["Metadata"]!["history.outcomeNames"] = JsonSerializer.Serialize(new[] { "\uFFFD" });
            var replacement = new RuntimeHistoryCandidate(serialized.Deserialize<RuntimeHistoryEvent>()!, candidate.CheckpointOrdinal);
            var changed = new RuntimeHistoryBatch(batch.Policy, batch.LogicalCheckpointId, batch.OccurredAt, batch.StartsAtRunStart, [replacement])
                { SourceFingerprint = batch.SourceFingerprint };
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await fixture.Capture.ValidateFinalAsync(captured with { HistoryBatches = [changed] }));
        }
    }

    private static RuntimeHistorySourceFact Fact(RuntimeHistorySourceFactKind kind, int second) =>
        new(kind, kind.ToString(), At.AddSeconds(second), "activity", "attempt", new(SchedulerWorkItemId: "work"));
    private static WorkflowExecutionState Workflow(RuntimeHistoryCapturePolicy? policy) => new("run", new("artifact", "definition", "version", "1", "hash"),
        WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, null, new Dictionary<string, string>()) { HistoryCapturePolicy = policy };
    private static ActivityExecutionState Activity(ActivityExecutionStatus status) => new(new("activity", "run", "node", "authored", "test", "1"),
        status, null, 1, At, At, status == ActivityExecutionStatus.Completed ? At.AddSeconds(5) : null, null, null, null, null,
        ActivitySchedulingProvenance.From("run", null, null, null, null, null, null, "test"), 0, [], [], 0, 0, new Dictionary<string, string>());
    private static RuntimeCheckpointCommit Commit(string id, string name, WorkflowExecutionState? workflow = null, ActivityExecutionState? activity = null)
    {
        var metadata = new Dictionary<string, string> { [RuntimeMetadataKeys.SchedulerWorkItemId] = "work" };
        return new(id, new("checkpoint-" + id, name, "run", At.AddSeconds(5), activity is null ? [] : [activity.InvocationId], metadata),
            new(workflow is null ? null : new("run", RuntimeStateChangeOperation.Upsert, workflow, metadata), null,
                activity is null ? [] : [new("activity", RuntimeStateChangeOperation.Upsert, activity, metadata)], [], [], [], []), [], metadata);
    }

    private sealed class Fixture(ServiceProvider provider, RuntimeHistoryCapture capture) : IDisposable
    {
        internal RuntimeHistoryCapture Capture => capture;
        public void Dispose() => provider.Dispose();
        internal static async Task<Fixture> Create(RuntimeHistoryCapturePolicy? policy, bool saveNative = true, bool values = true, bool matchedSource = true,
            IWorkflowExecutionStateStore? executionOverride = null, IRuntimeCoalescingSessionAccessor? coalescing = null)
        {
            var executions = executionOverride ?? new InMemoryWorkflowExecutionStateStore();
            if (saveNative) await executions.SaveAsync(Workflow(policy));
            var source = new DefaultRuntimeHistorySourceExtension(executions);
            var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), new UnusedWriter(),
                new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []) { HistorySourceResolver = () => matchedSource ? source : new DefaultRuntimeHistorySourceExtension(executions) };
            var services = new ServiceCollection();
            services.AddSingleton<IRuntimeHistorySourceExtension>(source);
            services.AddSingleton(committer);
            if (values) services.AddSingleton<IRuntimeActivityExecutionInspectionAccumulator>(new RuntimeActivityExecutionInspectionAccumulator(
                new InMemoryActivityExecutionInspectionStore(), executions, new RuntimeHistoryOptions()));
            var provider = services.BuildServiceProvider();
            return new(provider, new RuntimeHistoryCapture(executions,
                [new(Enum.GetValues<RuntimeHistorySourceFactKind>())], new RuntimeHistoryCaptureCapabilities(provider), coalescing));
        }
    }
    private sealed class UnusedWriter : IRuntimeCheckpointCommitStore
    {
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Candidate capture must not write a store.");
    }
    [Theory]
    [InlineData("io", false)] [InlineData("io", true)]
    [InlineData("wrapped-cancel", false)] [InlineData("wrapped-cancel", true)]
    [InlineData("json", false)] [InlineData("json", true)]
    [InlineData("success", false)] [InlineData("success", true)]
    public async Task Late_policy_read_honors_the_actual_caller_token_before_capture_or_final_validation(string shape, bool finalValidation)
    {
        using var cancellation = new CancellationTokenSource();
        Exception? failure = shape switch
        {
            "io" => new IOException("constructed dependency failure"),
            "wrapped-cancel" => new InvalidOperationException("constructed provider cancellation", new OperationCanceledException(cancellation.Token)),
            "json" => new JsonException("constructed malformed document"),
            _ => null
        };
        using var fixture = await Fixture.Create(null, saveNative: false,
            executionOverride: new FailedRead(failure, cancellation.Cancel, Workflow(Policy("WorkflowStarted"))));
        var raw = Commit("cancelled-capture", RuntimeCheckpointNames.WorkflowStarted, workflow: Workflow(Policy("WorkflowStarted")));
        var caught = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (finalValidation) await fixture.Capture.ValidateFinalAsync(raw, cancellation.Token);
            else await fixture.Capture.EnrichAsync(raw, cancellation.Token);
        });
        Assert.Equal(cancellation.Token, caught.CancellationToken);
    }

    private sealed class FailedRead(Exception? failure, Action? beforeReturn = null, WorkflowExecutionState? result = null) : IWorkflowExecutionStateStore
    {
        public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        { beforeReturn?.Invoke(); return failure is null ? ValueTask.FromResult(result) : ValueTask.FromException<WorkflowExecutionState?>(failure); }
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
