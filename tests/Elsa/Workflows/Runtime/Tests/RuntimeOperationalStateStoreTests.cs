using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeOperationalStateStoreTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InMemoryOperationalStateStore_SavesFindsAndListsByWorkflowExecution()
    {
        var store = new InMemoryOperationalStateStore();
        var first = NewOperationalState("operational-1", "wfexec-1", "worker-1");
        var second = NewOperationalState("operational-2", "wfexec-1", "worker-2");
        var otherWorkflow = NewOperationalState("operational-1", "wfexec-2", "worker-3");

        await store.SaveAsync(first);
        await store.SaveAsync(second);
        await store.SaveAsync(otherWorkflow);

        Assert.Same(first, await store.FindAsync("wfexec-1", "operational-1"));
        Assert.Equal(2, (await store.ListAsync("wfexec-1")).Count);
        Assert.Equal(3, (await store.ListAllAsync()).Count);
        Assert.Single(await store.ListAsync("wfexec-2"));
        Assert.Null(await store.FindAsync("wfexec-1", "missing"));
    }

    private OperationalState NewOperationalState(string operationalStateId, string workflowExecutionId, string ownerId) =>
        new(
            operationalStateId: operationalStateId,
            workflowExecutionId: workflowExecutionId,
            executionLease: new RuntimeExecutionLease(
                leaseId: $"lease-{ownerId}",
                workflowExecutionId: workflowExecutionId,
                ownerId: ownerId,
                acquiredAt: _now,
                expiresAt: _now.AddMinutes(5),
                fencingToken: 1),
            heartbeat: new RuntimeHeartbeat(
                heartbeatId: $"heartbeat-{ownerId}",
                workflowExecutionId: workflowExecutionId,
                ownerId: ownerId,
                leaseId: $"lease-{ownerId}",
                recordedAt: _now),
            drain: null,
            interruptedExecution: null,
            pendingPostCommitIntentIds: ["intent-1"],
            metadata: new Dictionary<string, string>());
}
