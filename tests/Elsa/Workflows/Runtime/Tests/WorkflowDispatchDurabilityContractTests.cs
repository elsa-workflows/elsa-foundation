using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
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

    private static WorkflowDispatchRecord NewRecord(
        string parentExecutionId,
        string activityExecutionId,
        DateTimeOffset createdAt,
        string? tenantId = null)
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
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            null,
            tenantId,
            new WorkflowExecutionPartition("partition-1"),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot(parentExecutionId, "initiator-1"),
            [],
            createdAt,
            createdAt);
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
