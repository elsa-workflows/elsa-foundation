using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowDispatchDurabilityContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lifecycle_AllowsExactPendingToTerminalRepairAndRejectsRegressionOrMutation()
    {
        var pending = NewRecord("parent-1", "activity-1", Now);
        var completed = pending.TransitionTo(WorkflowDispatchStatus.Completed, Now.AddMinutes(1));

        WorkflowDispatchLifecycle.ValidateTransition(pending, completed);
        Assert.Throws<InvalidOperationException>(() => completed.TransitionTo(WorkflowDispatchStatus.Started, Now.AddMinutes(2)));

        var mutated = NewRecord("parent-1", "activity-1", Now, tenantId: "tenant-other")
            .TransitionTo(WorkflowDispatchStatus.Completed, Now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => WorkflowDispatchLifecycle.ValidateTransition(pending, mutated));
    }

    [Fact]
    public async Task InMemoryStore_ValidatesTransitionsAndQueriesBoundedStableIntersections()
    {
        var store = new InMemoryWorkflowDispatchStore();
        var second = NewRecord("parent-1", "activity-2", Now.AddSeconds(1));
        var first = NewRecord("parent-1", "activity-1", Now);
        var other = NewRecord("parent-2", "activity-3", Now.AddSeconds(2));
        await store.SaveAsync(second);
        await store.SaveAsync(other);
        await store.SaveAsync(first);
        await store.SaveAsync(first.TransitionTo(WorkflowDispatchStatus.Started, Now.AddMinutes(1)));

        var parentPending = await store.QueryAsync(new WorkflowDispatchQuery(
            parentWorkflowExecutionId: "parent-1",
            status: WorkflowDispatchStatus.Pending,
            take: 1));
        var childStarted = await store.QueryAsync(new WorkflowDispatchQuery(
            childWorkflowExecutionId: first.ChildWorkflowExecutionId,
            status: WorkflowDispatchStatus.Started));

        Assert.Equal([second.DispatchId], parentPending.Select(x => x.DispatchId));
        Assert.Equal(first.DispatchId, Assert.Single(childStarted).DispatchId);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowDispatchQuery(status: WorkflowDispatchStatus.Pending, take: 101));

        await store.DeleteAsync(first.DispatchId);
        Assert.Null(await store.FindAsync(first.DispatchId));
    }

    [Fact]
    public async Task CheckpointStore_RequiresParentForPendingAndChildForLifecycleProjection()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var dispatchStore = new InMemoryWorkflowDispatchStore(state);
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore(state: state, workflowDispatchStore: dispatchStore);
        var pending = NewRecord("parent-1", "activity-1", Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            checkpointStore.CommitAsync(NewDispatchCommit("wrong-pending", pending.ChildWorkflowExecutionId, pending), ImmediateDecision()).AsTask());
        await checkpointStore.CommitAsync(NewDispatchCommit("pending", pending.ParentWorkflowExecutionId, pending), ImmediateDecision());

        var started = pending.TransitionTo(WorkflowDispatchStatus.Started, Now.AddSeconds(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            checkpointStore.CommitAsync(NewDispatchCommit("wrong-started", pending.ParentWorkflowExecutionId, started), ImmediateDecision()).AsTask());
        await checkpointStore.CommitAsync(NewDispatchCommit("started", pending.ChildWorkflowExecutionId, started), ImmediateDecision());

        Assert.Equal(WorkflowDispatchStatus.Started, (await dispatchStore.FindAsync(pending.DispatchId))!.Status);
    }

    [Fact]
    public void CancellationRequest_ValidatesDeterministicIdentityAndTimestamp()
    {
        var record = NewRecord("parent-1", "activity-1", Now, WorkflowDispatchMode.WaitForCompletion);

        var request = NewCancellationRequest(record, Now.AddMinutes(1));

        Assert.Equal(record.DispatchId, request.DispatchId);
        Assert.Equal(record.ParentWorkflowExecutionId, request.ParentWorkflowExecutionId);
        Assert.Equal(record.ParentActivityExecutionId, request.ParentActivityExecutionId);
        Assert.Equal(record.ChildWorkflowExecutionId, request.ChildWorkflowExecutionId);
        Assert.Throws<ArgumentException>(() => new WorkflowDispatchCancellationRequest(
            "dispatch:wrong", record.ParentWorkflowExecutionId, record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowDispatchCancellationRequest(
            record.DispatchId, record.ParentWorkflowExecutionId, record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId, default));
    }

    [Fact]
    public void StateChangeSet_OrdersRequestsAndRejectsConflictingDuplicates()
    {
        var first = NewRecord("parent-1", "activity-1", Now, WorkflowDispatchMode.WaitForCompletion);
        var second = NewRecord("parent-1", "activity-2", Now, WorkflowDispatchMode.WaitForCompletion);
        var firstRequest = NewCancellationRequest(first, Now.AddMinutes(1));
        var secondRequest = NewCancellationRequest(second, Now.AddMinutes(1));

        var changes = NewStateChanges([secondRequest, firstRequest, firstRequest]);

        Assert.Equal([first.DispatchId, second.DispatchId], changes.WorkflowDispatchCancellations.Select(x => x.DispatchId));
        Assert.Throws<ArgumentException>(() => NewStateChanges(
            [firstRequest, NewCancellationRequest(first, Now.AddMinutes(2))]));
    }

    [Fact]
    public void CheckpointFingerprint_IsRequestOrderIndependentAndConflictSensitive()
    {
        var first = NewRecord("parent-1", "activity-1", Now, WorkflowDispatchMode.WaitForCompletion);
        var second = NewRecord("parent-1", "activity-2", Now, WorkflowDispatchMode.WaitForCompletion);
        var firstRequest = NewCancellationRequest(first, Now.AddMinutes(1));
        var secondRequest = NewCancellationRequest(second, Now.AddMinutes(1));

        var forward = NewCancellationCommit("fingerprint", [firstRequest, secondRequest]);
        var reverse = NewCancellationCommit("fingerprint", [secondRequest, firstRequest]);
        var changed = NewCancellationCommit("fingerprint", [firstRequest, NewCancellationRequest(second, Now.AddMinutes(2))]);

        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(forward), RuntimeCheckpointCommitFingerprint.Compute(reverse));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(forward), RuntimeCheckpointCommitFingerprint.Compute(changed));
    }

    [Fact]
    public async Task InMemoryStore_LinearizesAdmissionAndCancellation()
    {
        var store = new InMemoryWorkflowDispatchStore();
        var cancellationWins = WithCancellationPolicy(NewRecord("parent-1", "activity-1", Now, WorkflowDispatchMode.WaitForCompletion, dispatchNestingDepth: 3), true);
        var admissionWins = WithCancellationPolicy(NewRecord("parent-1", "activity-2", Now, WorkflowDispatchMode.WaitForCompletion, dispatchNestingDepth: 3), true);
        await store.SaveAsync(cancellationWins);
        await store.SaveAsync(admissionWins);

        var cancelled = await store.ApplyCancellationAsync(NewCancellationRequest(cancellationWins, Now.AddMinutes(1)));
        var suppressed = await store.TryAdmitAsync(cancellationWins.DispatchId, Now.AddMinutes(2));
        var admitted = await store.TryAdmitAsync(admissionWins.DispatchId, Now.AddMinutes(1));
        var requested = await store.ApplyCancellationAsync(NewCancellationRequest(admissionWins, Now.AddMinutes(2)));
        var duplicateAdmission = await store.TryAdmitAsync(admissionWins.DispatchId, Now.AddMinutes(3));

        Assert.Equal(WorkflowDispatchCancellationDisposition.AppliedBeforeAdmission, cancelled.Disposition);
        Assert.Equal(WorkflowDispatchAdmissionDisposition.CancelledBeforeAdmission, suppressed.Disposition);
        Assert.Equal(WorkflowDispatchAdmissionDisposition.Admitted, admitted.Disposition);
        Assert.Equal(WorkflowDispatchCancellationDisposition.CancellationRequestedAfterAdmission, requested.Disposition);
        Assert.Equal(WorkflowDispatchAdmissionDisposition.AlreadyAdmitted, duplicateAdmission.Disposition);
        Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(cancelled.Record));
        Assert.True(WorkflowDispatchLifecycle.IsCancellationRequested(requested.Record));
        Assert.Equal(3, cancelled.Record.DispatchNestingDepth);
        Assert.Equal(3, requested.Record.DispatchNestingDepth);
    }

    [Fact]
    public void CancellationMarker_IsAppendOnlyAcrossTerminalProjection()
    {
        var pending = WithCancellationPolicy(
            NewRecord("parent-1", "activity-1", Now, WorkflowDispatchMode.WaitForCompletion),
            true);
        var started = pending.TransitionTo(WorkflowDispatchStatus.Started, Now.AddMinutes(1));
        var marked = WorkflowDispatchLifecycle.MarkCancellationRequested(started, Now.AddMinutes(2));
        var staleTerminal = started.TransitionTo(WorkflowDispatchStatus.Faulted, Now.AddMinutes(3));

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowDispatchLifecycle.ValidateTransition(marked, staleTerminal));

        var currentTerminal = marked.TransitionTo(WorkflowDispatchStatus.Faulted, Now.AddMinutes(3));
        WorkflowDispatchLifecycle.ValidateTransition(marked, currentTerminal);
        Assert.True(WorkflowDispatchLifecycle.IsCancellationRequested(currentTerminal));
    }

    [Fact]
    public async Task InMemoryStore_TerminalStateOutranksLateCancellationAndAdmission()
    {
        var store = new InMemoryWorkflowDispatchStore();
        var pending = WithCancellationPolicy(NewRecord("parent-1", "activity-1", Now, WorkflowDispatchMode.WaitForCompletion), true);
        await store.SaveAsync(pending);
        await store.SaveAsync(pending.TransitionTo(WorkflowDispatchStatus.Completed, Now.AddMinutes(1)));

        var cancellation = await store.ApplyCancellationAsync(NewCancellationRequest(pending, Now.AddMinutes(2)));
        var admission = await store.TryAdmitAsync(pending.DispatchId, Now.AddMinutes(2));

        Assert.Equal(WorkflowDispatchCancellationDisposition.TerminalUnchanged, cancellation.Disposition);
        Assert.Equal(WorkflowDispatchAdmissionDisposition.Terminal, admission.Disposition);
        Assert.Equal(WorkflowDispatchStatus.Completed, cancellation.Record.Status);
        Assert.False(WorkflowDispatchLifecycle.IsCancellationRequested(cancellation.Record));
    }

    [Fact]
    public async Task CheckpointStore_AppliesCancellationWithDurableOutbox()
    {
        var state = new InMemoryRuntimeCheckpointStoreState();
        var dispatchStore = new InMemoryWorkflowDispatchStore(state);
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore(state: state, workflowDispatchStore: dispatchStore);
        var pending = WithCancellationPolicy(NewRecord("parent-1", "activity-1", Now, WorkflowDispatchMode.WaitForCompletion), true);
        await dispatchStore.SaveAsync(pending);
        var identity = new WorkflowDispatchIdentity(pending.ParentWorkflowExecutionId, pending.ParentActivityExecutionId);
        var intent = new RuntimePostCommitIntent(
            identity.ChildCancelIntentId,
            pending.ParentWorkflowExecutionId,
            "Elsa.Activities.DispatchWorkflow.CancelChild",
            Now.AddMinutes(1),
            pending.ParentActivityExecutionId,
            identity.ChildCancelIdempotencyKey,
            JsonSerializer.SerializeToElement(new { pending.DispatchId }));
        var commit = NewCancellationCommit("atomic-cancel", [NewCancellationRequest(pending, Now.AddMinutes(1))]) with
        {
            PostCommitIntents = [intent]
        };
        commit = commit with
        {
            StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(commit))
        };

        await checkpointStore.CommitAsync(commit, ImmediateDecision());

        var persisted = (await dispatchStore.FindAsync(pending.DispatchId))!;
        Assert.True(WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(persisted));
        Assert.NotNull(await checkpointStore.FindAsync(identity.ChildCancelOutboxItemId(commit.CommitId)));
        Assert.Single(checkpointStore.ListCommits());
    }

    [Fact]
    public void CoalescingFold_CarriesCanonicalCancellationRequests()
    {
        var first = NewRecord("parent-1", "activity-1", Now, WorkflowDispatchMode.WaitForCompletion);
        var second = NewRecord("parent-1", "activity-2", Now, WorkflowDispatchMode.WaitForCompletion);

        var folded = RuntimeCheckpointFold.Fold(
        [
            NewStateChanges([NewCancellationRequest(second, Now.AddMinutes(1))]),
            NewStateChanges([NewCancellationRequest(first, Now.AddMinutes(1))])
        ]);

        Assert.Equal([first.DispatchId, second.DispatchId], folded.WorkflowDispatchCancellations.Select(x => x.DispatchId));
    }

    [Fact]
    public void CancellationFoldAndFingerprint_PreserveActivityScopeCleanups()
    {
        var dispatch = NewRecord("parent-1", "activity-1", Now, WorkflowDispatchMode.WaitForCompletion);
        var request = NewCancellationRequest(dispatch, Now.AddMinutes(1));
        var cleanup = new ActivityScopeCleanupRequest("parent-1", "scope-1", ["activity-1"], ["bookmark-1"], ["timer-1"], ["work-1"]);
        var withCleanup = NewCancellationCommit("cleanup", [request]) with
        {
            StateChanges = NewStateChanges([request], [cleanup])
        };
        var withoutCleanup = NewCancellationCommit("cleanup", [request]);

        var folded = RuntimeCheckpointFold.Fold([withCleanup.StateChanges]);

        Assert.Equal(cleanup, Assert.Single(folded.ActivityScopeCleanups));
        Assert.NotEqual(
            RuntimeCheckpointCommitFingerprint.Compute(withoutCleanup),
            RuntimeCheckpointCommitFingerprint.Compute(withCleanup));
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Completed, WorkflowDispatchStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Faulted, WorkflowDispatchStatus.Faulted)]
    [InlineData(WorkflowExecutionStatus.Cancelled, WorkflowDispatchStatus.Cancelled)]
    public async Task CheckpointEnricher_MirrorsChildTerminalStateBeforePersistence(
        WorkflowExecutionStatus executionStatus,
        WorkflowDispatchStatus expectedStatus)
    {
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        var pending = NewRecord("parent-1", "activity-1", Now);
        await dispatchStore.SaveAsync(pending);
        var captureStore = new CaptureCommitStore();
        var enricher = new WorkflowDispatchCheckpointEnricher(dispatchStore);
        var committer = new RuntimeCheckpointCommitter(
            new ImmediateRuntimeCheckpointPersistencePolicy(),
            captureStore,
            ownershipContextAccessor: null,
            tracer: null,
            enrichers: [enricher]);

        await committer.CommitAsync(NewTerminalCommit(pending.ChildWorkflowExecutionId, executionStatus));

        var projected = Assert.Single(captureStore.Commit!.StateChanges.WorkflowDispatches).State;
        Assert.Equal(expectedStatus, projected.Status);
        Assert.Equal(pending.ChildWorkflowExecutionId, captureStore.Commit.WorkflowExecutionId);
    }

    private static RuntimeCheckpointPersistenceDecision ImmediateDecision() =>
        new(RuntimeCheckpointPersistenceMode.Immediate);

    private static RuntimeCheckpointCommit NewDispatchCommit(
        string suffix,
        string workflowExecutionId,
        WorkflowDispatchRecord record) =>
        new(
            $"commit-{suffix}",
            new RuntimeCheckpoint($"checkpoint-{suffix}", "Dispatch", workflowExecutionId, Now, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                null, null, [], [], [], [], [],
                workflowDispatches:
                [
                    new RuntimeStateChange<WorkflowDispatchRecord>(
                        record.DispatchId,
                        RuntimeStateChangeOperation.Upsert,
                        record,
                        new Dictionary<string, string>())
                ]),
            [],
            new Dictionary<string, string>());

    private static RuntimeCheckpointCommit NewTerminalCommit(
        string workflowExecutionId,
        WorkflowExecutionStatus status)
    {
        var terminalAt = Now.AddMinutes(5);
        var execution = new WorkflowExecutionState(
            workflowExecutionId,
            new WorkflowExecutableIdentity("artifact-child", "definition-child", "version-child", "1.0.0", "sha256:child"),
            status,
            null,
            Now,
            Now,
            terminalAt,
            terminalAt,
            null,
            "parent-1",
            null,
            new Dictionary<string, string>());
        return new RuntimeCheckpointCommit(
            "commit-terminal",
            new RuntimeCheckpoint("checkpoint-terminal", "Terminal", workflowExecutionId, terminalAt, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                new RuntimeStateChange<WorkflowExecutionState>(
                    workflowExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    execution,
                    new Dictionary<string, string>()),
                null, [], [], [], [], []),
            [],
            new Dictionary<string, string>());
    }

    private static RuntimeCheckpointStateChangeSet NewStateChanges(
        IReadOnlyCollection<WorkflowDispatchCancellationRequest> requests,
        IReadOnlyCollection<ActivityScopeCleanupRequest>? activityScopeCleanups = null) =>
        new(
            null, null, [], [], [], [], [],
            workflowDispatches: null,
            activityExecutionInspections: null,
            postCommitOutbox: null,
            activityScopeCleanups: activityScopeCleanups,
            workflowDispatchCancellations: requests);

    private static RuntimeCheckpointCommit NewCancellationCommit(
        string suffix,
        IReadOnlyCollection<WorkflowDispatchCancellationRequest> requests) =>
        new(
            $"commit-{suffix}",
            new RuntimeCheckpoint($"checkpoint-{suffix}", "WorkflowCancelled", "parent-1", Now, [], new Dictionary<string, string>()),
            NewStateChanges(requests),
            [],
            new Dictionary<string, string>());

    private static WorkflowDispatchCancellationRequest NewCancellationRequest(
        WorkflowDispatchRecord record,
        DateTimeOffset requestedAt) =>
        new(
            record.DispatchId,
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId,
            requestedAt);

    private static WorkflowDispatchRecord WithCancellationPolicy(WorkflowDispatchRecord record, bool enabled)
    {
        var metadata = record.Metadata.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        WorkflowDispatchLifecycle.SetEffectiveCancellationPolicy(metadata, record.Mode, enabled);
        return new WorkflowDispatchRecord(
            record.DispatchId,
            record.ParentWorkflowExecutionId,
            record.ParentActivityExecutionId,
            record.ChildWorkflowExecutionId,
            record.ChildExecutable,
            record.ChildSource,
            record.Mode,
            record.Status,
            record.CorrelationId,
            record.TenantId,
            record.Partition,
            record.RunKind,
            record.Authority,
            record.InputDescriptors,
            record.CreatedAt,
            record.UpdatedAt,
            metadata,
            record.DispatchNestingDepth);
    }

    private static WorkflowDispatchRecord NewRecord(
        string parentExecutionId,
        string activityExecutionId,
        DateTimeOffset createdAt,
        string? tenantId = null) =>
        NewRecord(parentExecutionId, activityExecutionId, createdAt, WorkflowDispatchMode.FireAndForget, tenantId);

    private static WorkflowDispatchRecord NewRecord(
        string parentExecutionId,
        string activityExecutionId,
        DateTimeOffset createdAt,
        WorkflowDispatchMode mode,
        string? tenantId = null,
        int dispatchNestingDepth = 0)
    {
        var identity = new WorkflowDispatchIdentity(parentExecutionId, activityExecutionId);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parentExecutionId,
            activityExecutionId,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-child", "definition-child", "version-child", "1.0.0", "sha256:child"),
            new WorkflowExecutableSourceProvenance(
                "source-child", "WorkflowDefinitionVersion", "version-child", "1.0.0",
                "definition-child", "version-child", "1.0.0", "publication-child", "slot-child"),
            mode,
            WorkflowDispatchStatus.Pending,
            null,
            tenantId,
            new WorkflowExecutionPartition("partition-1"),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot(parentExecutionId, "initiator-1"),
            [],
            createdAt,
            createdAt,
            metadata: null,
            dispatchNestingDepth: dispatchNestingDepth);
    }

    private sealed class CaptureCommitStore : IRuntimeCheckpointCommitStore
    {
        public RuntimeCheckpointCommit? Commit { get; private set; }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            Commit = commit;
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }
    }
}
