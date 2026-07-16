using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowDispatchRetentionCollectorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Sweep_retains_terminal_dispatch_while_either_linked_execution_exists(
        bool retainParent,
        bool retainChild)
    {
        var dispatchStore = await NewTerminalDispatchStoreAsync();
        var record = Assert.Single(await dispatchStore.QueryAsync(new WorkflowDispatchQuery(status: WorkflowDispatchStatus.Completed)));
        var executions = new InMemoryWorkflowExecutionStateStore();
        if (retainParent)
            await executions.SaveAsync(NewExecution(record.ParentWorkflowExecutionId));
        if (retainChild)
            await executions.SaveAsync(NewExecution(record.ChildWorkflowExecutionId));
        var collector = NewCollector(dispatchStore, executions);

        var result = await collector.SweepAsync();

        Assert.Equal(0, result.DeletedCount);
        Assert.NotNull(await dispatchStore.FindAsync(record.DispatchId));
    }

    [Fact]
    public async Task Sweep_deletes_terminal_dispatch_only_after_both_guarded_reads_are_absent()
    {
        var dispatchStore = await NewTerminalDispatchStoreAsync();
        var record = Assert.Single(await dispatchStore.QueryAsync(new WorkflowDispatchQuery(status: WorkflowDispatchStatus.Completed)));
        var collector = NewCollector(dispatchStore, new InMemoryWorkflowExecutionStateStore());

        var result = await collector.SweepAsync();

        Assert.Equal(1, result.DeletedCount);
        Assert.Null(await dispatchStore.FindAsync(record.DispatchId));
    }

    [Fact]
    public async Task Sweep_retains_when_execution_appears_at_final_recheck()
    {
        var dispatchStore = await NewTerminalDispatchStoreAsync();
        var record = Assert.Single(await dispatchStore.QueryAsync(new WorkflowDispatchQuery(status: WorkflowDispatchStatus.Completed)));
        var collector = NewCollector(dispatchStore, new RacingExecutionStore(record.ParentWorkflowExecutionId));

        var result = await collector.SweepAsync();

        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(1, result.RetainedCount);
        Assert.NotNull(await dispatchStore.FindAsync(record.DispatchId));
    }

    [Fact]
    public async Task Sweep_retains_on_execution_read_failure()
    {
        var dispatchStore = await NewTerminalDispatchStoreAsync();
        var collector = NewCollector(dispatchStore, new ThrowingExecutionStore());

        var result = await collector.SweepAsync();

        Assert.Equal(0, result.DeletedCount);
        Assert.Equal(1, result.UncertainCount);
    }

    [Fact]
    public async Task Repeated_sweeps_advance_past_a_full_retained_prefix()
    {
        var dispatchStore = new InMemoryWorkflowDispatchStore();
        var executions = new InMemoryWorkflowExecutionStateStore();
        WorkflowDispatchRecord? eligible = null;
        for (var index = 0; index <= WorkflowDispatchQuery.MaximumTake; index++)
        {
            var parentId = $"parent-{index:D3}";
            var activityId = $"activity-{index:D3}";
            var identity = new WorkflowDispatchIdentity(parentId, activityId);
            var createdAt = Now.AddSeconds(index);
            var pending = NewPending(identity, parentId, activityId, createdAt);
            await dispatchStore.SaveAsync(pending);
            await dispatchStore.SaveAsync(pending.TransitionTo(WorkflowDispatchStatus.Completed, createdAt.AddMilliseconds(1)));
            if (index < WorkflowDispatchQuery.MaximumTake)
                await executions.SaveAsync(NewExecution(parentId));
            else
                eligible = pending;
        }
        var collector = NewCollector(dispatchStore, executions);

        var first = await collector.SweepAsync();
        var second = await collector.SweepAsync();

        Assert.Equal(0, first.DeletedCount);
        Assert.Equal(1, second.DeletedCount);
        Assert.Null(await dispatchStore.FindAsync(eligible!.DispatchId));
    }

    private static WorkflowDispatchRetentionCollector NewCollector(
        InMemoryWorkflowDispatchStore dispatchStore,
        IWorkflowExecutionStateStore executionStore) =>
        new(dispatchStore, dispatchStore, executionStore, NullLogger<WorkflowDispatchRetentionCollector>.Instance);

    private static async ValueTask<InMemoryWorkflowDispatchStore> NewTerminalDispatchStoreAsync()
    {
        var store = new InMemoryWorkflowDispatchStore();
        var identity = new WorkflowDispatchIdentity("parent-retention", "activity-retention");
        var pending = NewPending(identity, "parent-retention", "activity-retention", Now);
        await store.SaveAsync(pending);
        await store.SaveAsync(pending.TransitionTo(WorkflowDispatchStatus.Completed, Now.AddMinutes(1)));
        return store;
    }

    private static WorkflowDispatchRecord NewPending(
        WorkflowDispatchIdentity identity,
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        DateTimeOffset createdAt) =>
        new(
            identity.DispatchId,
            parentWorkflowExecutionId,
            parentActivityExecutionId,
            identity.ChildWorkflowExecutionId,
            NewExecutableIdentity(),
            NewSource(),
            WorkflowDispatchMode.FireAndForget,
            WorkflowDispatchStatus.Pending,
            null,
            null,
            new WorkflowExecutionPartition("default"),
            WorkflowRunKind.PublishedRun,
            WorkflowExecutionAuthoritySnapshot.CreateRoot("test"),
            [],
            createdAt,
            createdAt);

    private static WorkflowExecutionState NewExecution(string workflowExecutionId) => new(
        workflowExecutionId,
        NewExecutableIdentity(),
        WorkflowExecutionStatus.Completed,
        null,
        Now,
        Now,
        Now,
        Now,
        null,
        null,
        null,
        new Dictionary<string, string>());

    private static WorkflowExecutableIdentity NewExecutableIdentity() =>
        new("artifact-retention", "definition-1", "version-1", "1.0.0", "sha256:test");

    private static WorkflowExecutableSourceProvenance NewSource() =>
        new("source-1", "WorkflowDefinitionVersion", "version-1", "1.0.0", "definition-1", "version-1", "1.0.0", "publication-1", null);

    private abstract class ExecutionStoreStub : IWorkflowExecutionStateStore
    {
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public abstract ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default);
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ThrowingExecutionStore : ExecutionStoreStub
    {
        public override ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("provider unavailable");
    }

    private sealed class RacingExecutionStore(string parentWorkflowExecutionId) : ExecutionStoreStub
    {
        private int _parentReads;

        public override ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            if (!StringComparer.Ordinal.Equals(workflowExecutionId, parentWorkflowExecutionId))
                return ValueTask.FromResult<WorkflowExecutionState?>(null);

            _parentReads++;
            return ValueTask.FromResult<WorkflowExecutionState?>(_parentReads == 1 ? null : NewExecution(workflowExecutionId));
        }
    }
}
