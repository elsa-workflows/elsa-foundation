using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeSchedulerStateStoreTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InMemorySchedulerStateStore_SavesFindsAndListsByWorkflowExecution()
    {
        var store = new InMemorySchedulerStateStore();
        var first = NewSchedulerState("wfexec-1", version: 1, "node-1");
        var otherWorkflow = NewSchedulerState("wfexec-2", version: 1, "node-2");
        var updated = NewSchedulerState("wfexec-1", version: 2, "node-3");

        await store.SaveAsync(first);
        await store.SaveAsync(otherWorkflow);

        Assert.Same(first, await store.FindAsync("wfexec-1"));
        Assert.Same(otherWorkflow, await store.FindAsync("wfexec-2"));
        Assert.Equal(2, (await store.ListAsync()).Count);

        await store.SaveAsync(updated);

        var state = await store.FindAsync("wfexec-1");
        Assert.Same(updated, state);
        Assert.Equal(2, state!.Version);
        Assert.Equal("node-3", Assert.Single(state.PendingWork).ExecutableNodeId);
        Assert.Equal(2, (await store.ListAsync()).Count);
        Assert.Null(await store.FindAsync("wfexec-missing"));
    }

    private SchedulerState NewSchedulerState(string workflowExecutionId, long version, string executableNodeId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            version: version,
            pendingWork:
            [
                new ScheduledActivityWorkItem(
                    WorkItemId: $"work-{workflowExecutionId}-{version}",
                    WorkflowExecutionId: workflowExecutionId,
                    ExecutableNodeId: executableNodeId,
                    ActivityExecutionId: null,
                    SchedulingActivityExecutionId: null,
                    BranchId: "branch-1",
                    IterationId: null,
                    EnqueuedAt: _now,
                    Reason: "test")
            ],
            pendingContinuations: [],
            volatileWaits: []);
}
