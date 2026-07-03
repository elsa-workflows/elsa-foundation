using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeWorkflowHoldStateStoreTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InMemoryControlPlaneStateStore_SavesFindsAndListsByWorkflowExecution()
    {
        var store = new InMemoryWorkflowHoldStateStore();
        var first = NewWorkflowState("control-1", "wfexec-1", "pause-1");
        var second = NewWorkflowState("control-2", "wfexec-1", "pause-2");
        var otherWorkflow = NewWorkflowState("control-3", "wfexec-2", "pause-3");
        var global = NewHostDrainState();

        await store.SaveAsync(first);
        await store.SaveAsync(second);
        await store.SaveAsync(otherWorkflow);
        await store.SaveAsync(global);

        Assert.Same(first, await store.FindAsync("control-1"));
        Assert.Equal(2, (await store.ListForWorkflowExecutionAsync("wfexec-1")).Count);
        Assert.Single(await store.ListForWorkflowExecutionAsync("wfexec-2"));
        Assert.Equal(4, (await store.ListAllAsync()).Count);
        Assert.Null(await store.FindAsync("missing"));
    }

    [Fact]
    public async Task InMemoryControlPlaneStateStore_UpsertsByControlPlaneStateId()
    {
        var store = new InMemoryWorkflowHoldStateStore();
        var original = NewWorkflowState("control-1", "wfexec-1", "pause-1");
        var replacement = NewWorkflowState("control-1", "wfexec-2", "pause-2");

        await store.SaveAsync(original);
        await store.SaveAsync(replacement);

        Assert.Same(replacement, await store.FindAsync("control-1"));
        Assert.Empty(await store.ListForWorkflowExecutionAsync("wfexec-1"));
        Assert.Single(await store.ListForWorkflowExecutionAsync("wfexec-2"));
        Assert.Single(await store.ListAllAsync());
    }

    [Fact]
    public async Task InMemoryControlPlaneStateStore_ListsGlobalRecordContainingWorkflowScopedHold()
    {
        var store = new InMemoryWorkflowHoldStateStore();
        var state = new WorkflowHoldState(
            controlPlaneStateId: "control-global",
            activeHolds:
            [
                WorkflowHold.ForWorkflowExecution(
                    holdId: "pause-1",
                    workflowExecutionId: "wfexec-1",
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "maintenance")
            ]);

        await store.SaveAsync(state);

        Assert.Same(state, Assert.Single(await store.ListForWorkflowExecutionAsync("wfexec-1")));
    }

    private WorkflowHoldState NewWorkflowState(string controlPlaneStateId, string workflowExecutionId, string holdId) =>
        new(
            controlPlaneStateId: controlPlaneStateId,
            workflowExecutionId: workflowExecutionId,
            activeHolds:
            [
                WorkflowHold.ForWorkflowExecution(
                    holdId: holdId,
                    workflowExecutionId: workflowExecutionId,
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "maintenance")
            ]);

    private WorkflowHoldState NewHostDrainState() =>
        new(
            controlPlaneStateId: "control-host",
            activeHolds:
            [
                WorkflowHold.ForHostDrain(
                    holdId: "drain-1",
                    hostId: "host-1",
                    requestedAt: _now,
                    requestedBy: "operator",
                    reason: "shutdown")
            ]);
}
