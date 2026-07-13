using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowExecutionStateStoreRetentionTests
{
    private readonly DateTimeOffset _now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly IWorkflowExecutionStateStore _store = new InMemoryWorkflowExecutionStateStore();

    [Fact]
    public async Task ListPinnedExecutableArtifactIdsAsync_ReturnsOneRootPerArtifactAcrossAllRetainedStatuses()
    {
        var statuses = Enum.GetValues<WorkflowExecutionStatus>();
        for (var index = 0; index < statuses.Length; index++)
        {
            var artifactId = index % 2 == 0 ? "artifact-a" : "artifact-b";
            await _store.SaveAsync(Execution($"execution-{index}", artifactId, statuses[index]));
        }

        var artifactIds = await _store.ListPinnedExecutableArtifactIdsAsync();

        Assert.Equal(["artifact-a", "artifact-b"], artifactIds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheExecutionAndDropsOnlyItsOwnRetentionRoot()
    {
        await _store.SaveAsync(Execution("execution-1", "artifact-shared", WorkflowExecutionStatus.Completed));
        await _store.SaveAsync(Execution("execution-2", "artifact-shared", WorkflowExecutionStatus.Faulted));

        Assert.True(await _store.DeleteAsync("execution-1"));
        Assert.Null(await _store.FindAsync("execution-1"));
        Assert.Equal(["artifact-shared"], await _store.ListPinnedExecutableArtifactIdsAsync());

        Assert.True(await _store.DeleteAsync("execution-2"));
        Assert.Null(await _store.FindAsync("execution-2"));
        Assert.Empty(await _store.ListPinnedExecutableArtifactIdsAsync());
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseWhenTheExecutionIsNotRetained()
    {
        Assert.False(await _store.DeleteAsync("missing-execution"));
    }

    private WorkflowExecutionState Execution(
        string workflowExecutionId,
        string artifactId,
        WorkflowExecutionStatus status) =>
        new(
            WorkflowExecutionId: workflowExecutionId,
            PinnedExecutable: new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1.0.0", "sha256:test"),
            Status: status,
            SubStatus: null,
            CreatedAt: _now,
            StartedAt: _now,
            UpdatedAt: _now,
            CompletedAt: status.IsTerminal() ? _now : null,
            CorrelationId: null,
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: new Dictionary<string, string>());
}
