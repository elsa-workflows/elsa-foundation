using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeWorkflowExecutionStateStoreTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WorkflowExecutionStateStore_SaveAsync_UpdatesExistingState()
    {
        var store = new InMemoryWorkflowExecutionStateStore();
        var running = NewState(WorkflowExecutionStatus.Running);
        await store.SaveAsync(running);

        var saved = await store.SaveAsync(running with
        {
            Status = WorkflowExecutionStatus.Completed,
            UpdatedAt = _now.AddMinutes(5),
            CompletedAt = _now.AddMinutes(5)
        });

        Assert.Equal(WorkflowExecutionStatus.Completed, saved.Status);
        var state = await store.FindAsync("wfexec-1");
        Assert.NotNull(state);
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.Equal(_now.AddMinutes(5), state.CompletedAt);
        Assert.Single(await store.ListAsync());
    }

    private WorkflowExecutionState NewState(WorkflowExecutionStatus status) =>
        new(
            WorkflowExecutionId: "wfexec-1",
            PinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            Status: status,
            SubStatus: null,
            CreatedAt: _now,
            StartedAt: _now,
            UpdatedAt: _now,
            CompletedAt: status == WorkflowExecutionStatus.Completed ? _now : null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());
}
