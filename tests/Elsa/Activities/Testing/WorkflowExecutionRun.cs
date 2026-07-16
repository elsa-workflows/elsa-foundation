using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Testing;

/// <summary>
/// The outcome of a <see cref="WorkflowExecutionHarness.RunAsync"/> call: every persisted
/// <see cref="ActivityExecutionState"/> plus the final workflow state. Provides helpers to assert which
/// nodes ran, their status and completion outcomes, and iteration/branch identity.
/// </summary>
public sealed class WorkflowExecutionRun(IReadOnlyCollection<ActivityExecutionState> activityStates, WorkflowExecutionState? workflowState)
{
    /// <summary>All activity execution states recorded during the run, in store order.</summary>
    public IReadOnlyCollection<ActivityExecutionState> ActivityStates { get; } = activityStates;

    /// <summary>The final workflow execution state, or <c>null</c> if none was persisted.</summary>
    public WorkflowExecutionState? WorkflowState { get; } = workflowState;

    /// <summary>The executable-node ids of every activity that produced a state (i.e. that ran).</summary>
    public IReadOnlyCollection<string> RanNodeIds { get; } =
        activityStates.Select(state => state.Execution.ExecutableNodeId).ToArray();

    /// <summary>True when the given executable node produced at least one execution state.</summary>
    public bool DidRun(string executableNodeId) =>
        ActivityStates.Any(state => state.Execution.ExecutableNodeId == executableNodeId);

    /// <summary>Returns the single execution state for a node, failing the test if it ran zero or more than once.</summary>
    public ActivityExecutionState State(string executableNodeId) =>
        Assert.Single(ActivityStates, state => state.Execution.ExecutableNodeId == executableNodeId);

    /// <summary>Returns every execution state for a node (e.g. one per loop iteration), in store order.</summary>
    public IReadOnlyList<ActivityExecutionState> States(string executableNodeId) =>
        ActivityStates.Where(state => state.Execution.ExecutableNodeId == executableNodeId).ToList();

    /// <summary>Asserts the node ran exactly once and completed.</summary>
    public ActivityExecutionState AssertCompleted(string executableNodeId)
    {
        var state = State(executableNodeId);
        Assert.True(
            state.Status == ActivityExecutionStatus.Completed,
            $"Activity '{executableNodeId}' ended as {state.Status}/{state.SubStatus}: {state.Fault?.Message}");
        return state;
    }

    /// <summary>Asserts the node ran and the workflow completed; convenience for terminal-node coverage.</summary>
    public void AssertWorkflowCompleted()
    {
        var faults = ActivityStates
            .Where(state => state.Status == ActivityExecutionStatus.Faulted)
            .Select(state => $"{state.Execution.ExecutableNodeId}={state.SubStatus}: {state.Fault?.Message}")
            .ToArray();
        Assert.True(
            WorkflowState?.Status == WorkflowExecutionStatus.Completed,
            $"Workflow ended as {WorkflowState?.Status}/{WorkflowState?.SubStatus}. Faults: {string.Join(" | ", faults)}");
    }

    /// <summary>Asserts the listed nodes ran and the rest of the listed-as-absent nodes did not.</summary>
    public void AssertRan(params string[] executableNodeIds)
    {
        foreach (var nodeId in executableNodeIds)
            Assert.Contains(ActivityStates, state => state.Execution.ExecutableNodeId == nodeId);
    }

    /// <summary>Asserts none of the listed nodes ran.</summary>
    public void AssertDidNotRun(params string[] executableNodeIds)
    {
        foreach (var nodeId in executableNodeIds)
            Assert.DoesNotContain(ActivityStates, state => state.Execution.ExecutableNodeId == nodeId);
    }

    /// <summary>Asserts a node ran once, completed, and emitted exactly the expected completion outcomes.</summary>
    public ActivityExecutionState AssertOutcomes(string executableNodeId, params string[] expectedOutcomes)
    {
        var state = AssertCompleted(executableNodeId);
        Assert.Equal(expectedOutcomes, CompletionOutcomes(state));
        return state;
    }

    /// <summary>Reads the completion outcome names recorded on an execution state (empty when none).</summary>
    public static IReadOnlyCollection<string> CompletionOutcomes(ActivityExecutionState state) =>
        state.Metadata.TryGetValue(RuntimeMetadataKeys.CompletionOutcomeNames, out var serialized)
            ? JsonSerializer.Deserialize<string[]>(serialized) ?? []
            : [];
}
