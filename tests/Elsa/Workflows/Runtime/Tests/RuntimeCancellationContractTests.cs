using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// TS-9 (elsa-4-review-remediation, W15): first-class cancellation contract suite for the runtime.
///
/// Cancellation is a <see cref="WorkflowExecutionCommandKind.Cancel"/> work item handled by
/// <see cref="WorkflowCancelSchedulerWorkHandler"/>, which builds a terminal
/// <see cref="RuntimeCheckpointNames.ActivityCancelled"/> checkpoint and commits it through the real
/// <see cref="RuntimeCheckpointCommitter"/> → <see cref="InMemoryRuntimeCheckpointCommitStore"/> path (no mocks).
///
/// This suite proves the scenarios the review flagged as having no single home — cancel-while-suspended,
/// cancel-while-draining, cancel idempotency, and terminal-state preservation — and asserts that cancellation
/// composes with the durable boundaries owned by other work units: the coalescing persistence policy (W9,
/// a cancel checkpoint must flush immediately and can never be coalesced away) and single-writer fencing
/// (W5, a stale writer's cancel commit is rejected before any state is persisted). It deliberately does not
/// re-test the pipeline/inline dispatch split (RuntimeCheckpointSlotDecompositionTests), the coalescing
/// terminal-equivalence/crash scenarios (RuntimeCheckpointCoalescingTests), or the ownership fencing primitive
/// in isolation (RuntimeExecutionOwnershipTests) — it exercises those seams only through cancellation.
///
/// Self-contained by design (own helpers, no shared base) so it stays stable while the runtime source is
/// restructured in parallel.
/// </summary>
public sealed class RuntimeCancellationContractTests
{
    private const string WorkflowExecutionId = "wf-1";
    private readonly DateTimeOffset _now = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    // ── cancel-while-suspended ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_WhileWorkflowSuspended_CommitsCancelledTerminalState()
    {
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Suspended);
        await harness.SeedActivity(ActivityExecutionStatus.Suspended, "actexec-suspended");

        await harness.Handler().HandleAsync(harness.CancelWorkItem());

        var workflow = await harness.FindWorkflow();
        Assert.NotNull(workflow);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, workflow.Status);
        Assert.Equal(_now, workflow.CompletedAt);
        Assert.Equal(_now, workflow.UpdatedAt);

        var activity = await harness.FindActivity("actexec-suspended");
        Assert.NotNull(activity);
        Assert.Equal(ActivityExecutionStatus.Cancelled, activity.Status);
        Assert.Equal(_now, activity.CompletedAt);
    }

    // ── cancel-while-draining (in-flight activities) ────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_WhileDraining_CancelsRunningScheduledAndWaitingActivities()
    {
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Running);
        await harness.SeedActivity(ActivityExecutionStatus.Running, "actexec-running");
        await harness.SeedActivity(ActivityExecutionStatus.Scheduled, "actexec-scheduled");
        await harness.SeedActivity(ActivityExecutionStatus.Waiting, "actexec-waiting");

        await harness.Handler().HandleAsync(harness.CancelWorkItem());

        Assert.Equal(ActivityExecutionStatus.Cancelled, (await harness.FindActivity("actexec-running"))!.Status);
        Assert.Equal(ActivityExecutionStatus.Cancelled, (await harness.FindActivity("actexec-scheduled"))!.Status);
        Assert.Equal(ActivityExecutionStatus.Cancelled, (await harness.FindActivity("actexec-waiting"))!.Status);
    }

    // ── compose with the W1 fault path: cancel must never rewrite an already-terminal activity ──────

    [Fact]
    public async Task Cancel_PreservesAlreadyTerminalActivityStates()
    {
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Running);
        await harness.SeedActivity(ActivityExecutionStatus.Faulted, "actexec-faulted");
        await harness.SeedActivity(ActivityExecutionStatus.Completed, "actexec-completed");
        await harness.SeedActivity(ActivityExecutionStatus.Recovered, "actexec-recovered");
        await harness.SeedActivity(ActivityExecutionStatus.Cancelled, "actexec-already-cancelled");
        await harness.SeedActivity(ActivityExecutionStatus.Running, "actexec-running");

        await harness.Handler().HandleAsync(harness.CancelWorkItem());

        // The faulted state (W1) and other terminal states are left exactly as they were.
        Assert.Equal(ActivityExecutionStatus.Faulted, (await harness.FindActivity("actexec-faulted"))!.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, (await harness.FindActivity("actexec-completed"))!.Status);
        Assert.Equal(ActivityExecutionStatus.Recovered, (await harness.FindActivity("actexec-recovered"))!.Status);
        Assert.Equal(ActivityExecutionStatus.Cancelled, (await harness.FindActivity("actexec-already-cancelled"))!.Status);
        // Only the live activity transitions.
        Assert.Equal(ActivityExecutionStatus.Cancelled, (await harness.FindActivity("actexec-running"))!.Status);
    }

    // ── the emitted checkpoint is a terminal cancel checkpoint with the expected shape ──────────────

    [Fact]
    public async Task Cancel_EmitsTerminalActivityCancelledCheckpoint_WithReasonMetadata()
    {
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Running);
        await harness.SeedActivity(ActivityExecutionStatus.Running, "actexec-running");

        await harness.Handler().HandleAsync(harness.CancelWorkItem());

        var commit = Assert.Single(harness.CommitStore.ListCommits()).Commit;
        Assert.Equal(RuntimeCheckpointNames.ActivityCancelled, commit.Checkpoint.Name);
        Assert.Equal(WorkflowExecutionId, commit.Checkpoint.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, commit.StateChanges.WorkflowExecution!.State.Status);
        Assert.True(commit.Checkpoint.Metadata.ContainsKey(RuntimeMetadataKeys.CheckpointReason));
        Assert.Equal(WorkflowExecutionCommandKind.Cancel.ToString(), commit.Metadata[RuntimeMetadataKeys.CommandKind]);
    }

    // ── compose with W9 coalescing: a cancel checkpoint is a mandatory-flush boundary ───────────────

    [Fact]
    public async Task Cancel_Checkpoint_IsMandatoryFlush_AndIsNeverDeferredByCoalescingPolicy()
    {
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Running);
        await harness.SeedActivity(ActivityExecutionStatus.Running, "actexec-running");
        await harness.Handler().HandleAsync(harness.CancelWorkItem());
        var cancelCheckpoint = Assert.Single(harness.CommitStore.ListCommits()).Commit.Checkpoint;

        // The real coalescing policy classifies the handler-produced cancel checkpoint as an immediate flush,
        // so a burst-coalescing runtime can never fold a cancellation into a deferred segment and lose it.
        var decision = await new CoalescingRuntimeCheckpointPersistencePolicy().DecideAsync(cancelCheckpoint);

        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, decision.Mode);
        Assert.Contains(RuntimeCheckpointNames.ActivityCancelled, CoalescingRuntimeCheckpointPersistencePolicy.MandatoryFlushCheckpointNames);
        Assert.Contains(RuntimeCheckpointNames.WorkflowCancelled, CoalescingRuntimeCheckpointPersistencePolicy.MandatoryFlushCheckpointNames);
    }

    [Fact]
    public async Task Cancel_ThroughCommitter_WithCoalescingPolicy_PersistsImmediately()
    {
        // Driven through the real committer wired with the coalescing policy: the cancel is persisted, not deferred.
        var harness = new Harness(_now, new CoalescingRuntimeCheckpointPersistencePolicy());
        await harness.SeedWorkflow(WorkflowExecutionStatus.Running);
        await harness.SeedActivity(ActivityExecutionStatus.Running, "actexec-running");

        await harness.Handler().HandleAsync(harness.CancelWorkItem());

        Assert.Single(harness.CommitStore.ListCommits());
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await harness.FindWorkflow())!.Status);
    }

    // ── compose with W5 single-writer fencing ───────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_WithStaleFencingToken_IsRejected_AndPersistsNothing()
    {
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Running);
        await harness.SeedActivity(ActivityExecutionStatus.Running, "actexec-running");
        var (ownership, accessor) = harness.EnableOwnershipFencing();

        var staleWriter = await ownership.AcquireAsync(WorkflowExecutionId);
        var currentWriter = await ownership.AcquireAsync(WorkflowExecutionId);
        Assert.True(currentWriter.FencingToken > staleWriter.FencingToken);

        using (accessor.Push(staleWriter))
        {
            await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(
                () => harness.Handler().HandleAsync(harness.CancelWorkItem()).AsTask());
        }

        Assert.Empty(harness.CommitStore.ListCommits());
        Assert.Equal(WorkflowExecutionStatus.Running, (await harness.FindWorkflow())!.Status);
    }

    [Fact]
    public async Task Cancel_WithCurrentFencingToken_IsAdmitted_WhenOwnershipScopeActive()
    {
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Running);
        await harness.SeedActivity(ActivityExecutionStatus.Running, "actexec-running");
        var (ownership, accessor) = harness.EnableOwnershipFencing();

        var currentWriter = await ownership.AcquireAsync(WorkflowExecutionId);

        using (accessor.Push(currentWriter))
        {
            await harness.Handler().HandleAsync(harness.CancelWorkItem());
        }

        Assert.Single(harness.CommitStore.ListCommits());
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await harness.FindWorkflow())!.Status);
    }

    // ── cancel idempotency (covers a redelivered / re-driven cancel and a cancel racing a resume) ───

    [Fact]
    public async Task Cancel_IsIdempotent_WhenSameWorkItemHandledTwice()
    {
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Running);
        await harness.SeedActivity(ActivityExecutionStatus.Running, "actexec-running");
        var workItem = harness.CancelWorkItem();

        await harness.Handler().HandleAsync(workItem);
        await harness.Handler().HandleAsync(workItem);

        // The deterministic commit id makes the redelivered cancel a no-op at the store: one terminal transition.
        var commit = Assert.Single(harness.CommitStore.ListCommits());
        Assert.Equal($"commit:{workItem.WorkItemId}:cancel", commit.Commit.CommitId);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await harness.FindWorkflow())!.Status);
    }

    [Fact]
    public async Task Cancel_LeavesWorkflowInTerminalStatus_SoResumeWouldBeRejected()
    {
        // A cancel racing a resume: once cancelled the execution is terminal, so any later resume must be guarded off.
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Suspended);
        await harness.SeedActivity(ActivityExecutionStatus.Waiting, "actexec-waiting");

        await harness.Handler().HandleAsync(harness.CancelWorkItem());

        var workflow = await harness.FindWorkflow();
        Assert.NotNull(workflow);
        Assert.True(workflow.Status.IsTerminal());
        Assert.Equal(WorkflowExecutionStatus.Cancelled, workflow.Status);
    }

    // ── cancel is a self-contained terminal commit: no post-commit work is stranded ─────────────────

    [Fact]
    public async Task Cancel_Commit_DeclaresNoPostCommitIntents_AndSucceeds()
    {
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Running);
        await harness.SeedActivity(ActivityExecutionStatus.Running, "actexec-running");

        await harness.Handler().HandleAsync(harness.CancelWorkItem());

        var commit = Assert.Single(harness.CommitStore.ListCommits()).Commit;
        Assert.Empty(commit.PostCommitIntents);
        Assert.Empty(commit.StateChanges.PostCommitOutbox);
    }

    // ── terminal-state guard (#412 item 5): a Cancel must never clobber an already-terminal workflow ─

    [Fact]
    public async Task Cancel_OnAlreadyCompletedWorkflow_PreservesTerminalOutcome_AndEmitsNoCommit()
    {
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Completed);
        await harness.SeedActivity(ActivityExecutionStatus.Completed, "actexec-completed");

        await harness.Handler().HandleAsync(harness.CancelWorkItem());

        // The completed outcome is preserved verbatim — Cancel does not overwrite Status/CompletedAt.
        var workflow = await harness.FindWorkflow();
        Assert.NotNull(workflow);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflow.Status);
        Assert.Equal(DateTimeOffset.UnixEpoch, workflow.UpdatedAt);
        Assert.Null(workflow.CompletedAt);
        // No clobbering commit was produced.
        Assert.Empty(harness.CommitStore.ListCommits());
    }

    [Fact]
    public async Task Cancel_OnAlreadyFaultedWorkflow_PreservesTerminalOutcome_AndEmitsNoCommit()
    {
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Faulted);
        await harness.SeedActivity(ActivityExecutionStatus.Faulted, "actexec-faulted");

        await harness.Handler().HandleAsync(harness.CancelWorkItem());

        var workflow = await harness.FindWorkflow();
        Assert.NotNull(workflow);
        Assert.Equal(WorkflowExecutionStatus.Faulted, workflow.Status);
        Assert.Equal(DateTimeOffset.UnixEpoch, workflow.UpdatedAt);
        Assert.Null(workflow.CompletedAt);
        Assert.Empty(harness.CommitStore.ListCommits());
    }

    [Fact]
    public async Task Cancel_OnAlreadyCancelledWorkflow_IsNoOp_AndEmitsNoCommit()
    {
        // A redelivered cancel arriving after the workflow is already Cancelled is a no-op: the terminal
        // guard short-circuits before any commit is built, so idempotency holds without re-committing.
        var harness = new Harness(_now);
        await harness.SeedWorkflow(WorkflowExecutionStatus.Cancelled);
        await harness.SeedActivity(ActivityExecutionStatus.Cancelled, "actexec-cancelled");

        await harness.Handler().HandleAsync(harness.CancelWorkItem());

        var workflow = await harness.FindWorkflow();
        Assert.NotNull(workflow);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, workflow.Status);
        Assert.Empty(harness.CommitStore.ListCommits());
    }

    // ── missing execution guard ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancel_Throws_When_WorkflowExecution_Is_Missing()
    {
        var harness = new Harness(_now); // no workflow seeded

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Handler().HandleAsync(harness.CancelWorkItem()).AsTask());

        Assert.Contains(WorkflowExecutionId, exception.Message, StringComparison.Ordinal);
        Assert.Empty(harness.CommitStore.ListCommits());
    }

    // ── test harness (self-contained; real handler → committer → in-memory store) ────────────────────

    private sealed class Harness
    {
        private readonly DateTimeOffset _now;
        private readonly TimeProvider _timeProvider;
        private readonly InMemoryWorkflowExecutionStateStore _workflowStore = new();
        private readonly InMemoryActivityExecutionStateStore _activityStore = new();
        private readonly InMemoryActivityExecutionInspectionStore _inspectionStore = new();
        private readonly IRuntimeCheckpointPersistencePolicy _persistencePolicy;
        private IRuntimeExecutionOwnershipContextAccessor? _ownershipAccessor;
        private IRuntimeExecutionOwnershipService? _ownershipService;

        public Harness(DateTimeOffset now, IRuntimeCheckpointPersistencePolicy? persistencePolicy = null)
        {
            _now = now;
            _timeProvider = new FixedTimeProvider(now);
            _persistencePolicy = persistencePolicy ?? new ImmediateRuntimeCheckpointPersistencePolicy();
            CommitStore = new InMemoryRuntimeCheckpointCommitStore(
                _workflowStore, _activityStore, null, null, null, null, null, _inspectionStore);
        }

        public InMemoryRuntimeCheckpointCommitStore CommitStore { get; }

        public (RuntimeExecutionOwnershipService Ownership, AsyncLocalRuntimeExecutionOwnershipContextAccessor Accessor) EnableOwnershipFencing()
        {
            var options = new RuntimeExecutionOwnershipOptions
            {
                OwnerId = "owner-under-test",
                LeaseDuration = TimeSpan.FromMinutes(1)
            };
            var ownership = new RuntimeExecutionOwnershipService(new InMemoryExecutionLivenessStateStore(), _timeProvider, options);
            var accessor = new AsyncLocalRuntimeExecutionOwnershipContextAccessor();
            _ownershipService = ownership;
            _ownershipAccessor = accessor;
            return (ownership, accessor);
        }

        public WorkflowCancelSchedulerWorkHandler Handler() =>
            new(
                _workflowStore,
                _activityStore,
                new RuntimeCheckpointCommitter(_persistencePolicy, CommitStore, _ownershipAccessor, _ownershipService),
                new RuntimeActivityExecutionInspectionAccumulator(_inspectionStore),
                _timeProvider);

        public async Task SeedWorkflow(WorkflowExecutionStatus status) =>
            await _workflowStore.SaveAsync(new WorkflowExecutionState(
                WorkflowExecutionId: WorkflowExecutionId,
                PinnedExecutable: NewIdentity(),
                Status: status,
                SubStatus: null,
                CreatedAt: DateTimeOffset.UnixEpoch,
                StartedAt: DateTimeOffset.UnixEpoch,
                UpdatedAt: DateTimeOffset.UnixEpoch,
                CompletedAt: null,
                CorrelationId: null,
                ParentWorkflowExecutionId: null,
                TenantId: null,
                SystemMetadata: new Dictionary<string, string>()));

        public async Task SeedActivity(ActivityExecutionStatus status, string activityExecutionId) =>
            await _activityStore.SaveAsync(new ActivityExecutionState(
                Execution: new ActivityExecution(
                    ActivityExecutionId: activityExecutionId,
                    WorkflowExecutionId: WorkflowExecutionId,
                    ExecutableNodeId: "node-a",
                    AuthoredActivityId: "authored-a",
                    ActivityType: "Elsa.Test",
                    ActivityTypeVersion: "1.0.0"),
                Status: status,
                SubStatus: null,
                ExecutionSequence: 1,
                ScheduledAt: DateTimeOffset.UnixEpoch,
                StartedAt: DateTimeOffset.UnixEpoch,
                CompletedAt: status == ActivityExecutionStatus.Running ? null : DateTimeOffset.UnixEpoch,
                SchedulingActivityExecutionId: null,
                ParentActivityExecutionId: null,
                BranchId: null,
                IterationId: null,
                Provenance: ActivitySchedulingProvenance.From(
                    WorkflowExecutionId,
                    parentActivityExecutionId: null,
                    schedulingActivityExecutionId: null,
                    branchId: null,
                    iterationId: null,
                    executionPathId: null,
                    executionScopeId: null,
                    schedulingCause: "test"),
                CallStackDepth: null,
                BookmarkIds: [],
                IncidentIds: [],
                FaultCount: 0,
                AggregateFaultCount: 0,
                Metadata: new Dictionary<string, string>()));

        public ValueTask<WorkflowExecutionState?> FindWorkflow() => _workflowStore.FindAsync(WorkflowExecutionId);

        public ValueTask<ActivityExecutionState?> FindActivity(string activityExecutionId) =>
            _activityStore.FindAsync(WorkflowExecutionId, activityExecutionId);

        public RuntimeSchedulerWorkItem CancelWorkItem() =>
            new(
                workItemId: "cancel-work",
                workflowExecutionId: WorkflowExecutionId,
                commandId: "command-cancel",
                commandKind: WorkflowExecutionCommandKind.Cancel,
                envelopeId: "envelope-cancel",
                idempotencyKey: $"{WorkflowExecutionId}:cancel",
                enqueuedAt: _now,
                recordedAt: _now,
                sequence: 10,
                payload: null,
                commandMetadata: new Dictionary<string, string>(),
                envelopeMetadata: new Dictionary<string, string>());

        private static WorkflowExecutableIdentity NewIdentity() =>
            new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
