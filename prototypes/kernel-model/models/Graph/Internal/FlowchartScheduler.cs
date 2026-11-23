using Kernel.Graph.Models;

namespace Kernel.Graph.Internal;

/// <summary>
/// The single home for scheduling a Flowchart child activity. This is the one place that both mutates the
/// run's <see cref="FlowchartRun"/> (via <c>ScheduleChildActivity</c>) and records the
/// resulting active child on the execution state — bumping <see cref="FlowchartExecutionState.Sequence"/>
/// exactly once. Keeping that context+state pair in a single method means the join coordinator, the policy
/// applier, and the engine's entry paths all schedule identically, with no risk of a duplicated or dropped
/// active-child bump. Mechanical extraction of the former engine helper; the emitted scheduling metadata and
/// provenance are byte-for-byte unchanged.
/// </summary>
internal static class FlowchartScheduler
{
    public static FlowchartExecutionState ScheduleNode(
        FlowchartRun context,
        FlowchartExecutionState state,
        string nodeId,
        string executionPathId,
        string executionScopeId,
        string? iterationKey,
        string schedulingActivityExecutionId,
        string schedulingCause)
    {
        // Main passes command metadata and scheduling provenance to the runtime here; the host gets the same facts
        // as one record, and hands the path and scope back with the child's completion.
        context.ScheduleChildActivity(new FlowchartScheduledNode(nodeId, executionPathId, executionScopeId, iterationKey, schedulingActivityExecutionId, schedulingCause));

        var activeChild = new FlowchartActiveChild(nodeId, executionPathId, executionScopeId, schedulingCause);
        return state with
        {
            ActiveChildren = state.ActiveChildren
                .Where(child => !StringComparer.Ordinal.Equals(child.ExecutionPathId, executionPathId))
                .Append(activeChild)
                .ToArray(),
            Sequence = state.Sequence + 1
        };
    }
}
