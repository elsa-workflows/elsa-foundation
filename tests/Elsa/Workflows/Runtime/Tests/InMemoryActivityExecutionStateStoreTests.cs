using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Parent-scoped read coverage for <see cref="InMemoryActivityExecutionStateStore"/> (#514/#413 item 3): the
/// in-memory default store must return exactly the (workflow, parent) subset of <c>ListAsync</c>, matching the
/// Groundwork bridge so the Parallel join counts the same branches regardless of the selected provider.
/// </summary>
public sealed class InMemoryActivityExecutionStateStoreTests
{
    private readonly InMemoryActivityExecutionStateStore _store = new();

    [Fact]
    public async Task ListByParent_Equals_ListWhereParent()
    {
        await _store.SaveAsync(ChildState("wf-1", "ae-a1", "parent-a"));
        await _store.SaveAsync(ChildState("wf-1", "ae-a2", "parent-a"));
        await _store.SaveAsync(ChildState("wf-1", "ae-b1", "parent-b"));
        await _store.SaveAsync(ChildState("wf-1", "ae-orphan", parentActivityExecutionId: null));
        await _store.SaveAsync(ChildState("wf-2", "ae-a3", "parent-a"));

        var byParent = await _store.ListByParentAsync("wf-1", "parent-a");
        var expected = (await _store.ListAsync("wf-1")).Where(s => s.ParentActivityExecutionId == "parent-a");

        Assert.Equal(
            expected.Select(s => s.Execution.ActivityExecutionId).OrderBy(x => x),
            byParent.Select(s => s.Execution.ActivityExecutionId).OrderBy(x => x));
        Assert.Equal(new[] { "ae-a1", "ae-a2" }, byParent.Select(s => s.Execution.ActivityExecutionId).OrderBy(x => x));
    }

    [Fact]
    public async Task ListByParent_Scopes_By_Workflow_And_Empty_For_Unknown_Parent()
    {
        await _store.SaveAsync(ChildState("wf-1", "ae-a1", "parent-a"));
        await _store.SaveAsync(ChildState("wf-2", "ae-a2", "parent-a")); // same parent id, different workflow

        var byParent = await _store.ListByParentAsync("wf-1", "parent-a");

        Assert.Equal(new[] { "ae-a1" }, byParent.Select(s => s.Execution.ActivityExecutionId));
        Assert.Empty(await _store.ListByParentAsync("wf-1", "parent-missing"));
    }

    private static ActivityExecutionState ChildState(string workflowExecutionId, string activityExecutionId, string? parentActivityExecutionId) => new(
        new ActivityExecution(activityExecutionId, workflowExecutionId, $"node-{activityExecutionId}", "authored", "Elsa.Log", "1.0.0"),
        ActivityExecutionStatus.Running,
        SubStatus: null,
        ScheduledAt: DateTimeOffset.UnixEpoch,
        StartedAt: DateTimeOffset.UnixEpoch,
        CompletedAt: null,
        SchedulingActivityExecutionId: parentActivityExecutionId,
        ParentActivityExecutionId: parentActivityExecutionId,
        BranchId: null,
        IterationId: null,
        CallStackDepth: 0,
        BookmarkIds: [],
        IncidentIds: [],
        FaultCount: 0,
        AggregateFaultCount: 0,
        Metadata: new Dictionary<string, string>());
}
