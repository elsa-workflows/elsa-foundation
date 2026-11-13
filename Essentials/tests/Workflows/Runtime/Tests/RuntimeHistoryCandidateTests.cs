using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
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
            IWorkflowExecutionStateStore? executionOverride = null)
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
                [new(Enum.GetValues<RuntimeHistorySourceFactKind>())], new RuntimeHistoryCaptureCapabilities(provider)));
        }
    }
    private sealed class UnusedWriter : IRuntimeCheckpointCommitStore
    {
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Candidate capture must not write a store.");
    }
    private sealed class FailedRead(Exception failure) : IWorkflowExecutionStateStore
    {
        public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw failure;
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
