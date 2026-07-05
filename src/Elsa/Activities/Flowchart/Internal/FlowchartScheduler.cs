using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// The single home for scheduling a Flowchart child activity. This is the one place that both mutates the
/// runtime <see cref="IRuntimeActivityExecutionContext"/> (via <c>ScheduleChildActivity</c>) and records the
/// resulting active child on the execution state — bumping <see cref="FlowchartExecutionState.Sequence"/>
/// exactly once. Keeping that context+state pair in a single method means the join coordinator, the policy
/// applier, and the engine's entry paths all schedule identically, with no risk of a duplicated or dropped
/// active-child bump. Mechanical extraction of the former engine helper; the emitted scheduling metadata and
/// provenance are byte-for-byte unchanged.
/// </summary>
internal static class FlowchartScheduler
{
    public static FlowchartExecutionState ScheduleNode(
        IRuntimeActivityExecutionContext context,
        FlowchartExecutionState state,
        string nodeId,
        string executionPathId,
        string executionScopeId,
        string? iterationKey,
        string schedulingActivityExecutionId,
        string schedulingCause)
    {
        context.ScheduleChildActivity(
            nodeId,
            schedulingActivityExecutionId,
            new Dictionary<string, string>
            {
                [FlowchartExecutionEngine.ParentActivityExecutionIdMetadataKey] = context.ActivityExecutionState.Execution.ActivityExecutionId,
                [FlowchartExecutionEngine.ExecutionPathIdMetadataKey] = executionPathId,
                [FlowchartExecutionEngine.ExecutionScopeIdMetadataKey] = executionScopeId,
                [FlowchartExecutionEngine.SchedulingCauseMetadataKey] = schedulingCause,
                [FlowchartExecutionEngine.TargetNodeIdMetadataKey] = nodeId
            },
            ActivitySchedulingProvenance.From(
                context.WorkflowExecutionId,
                context.ActivityExecutionState.Execution.ActivityExecutionId,
                schedulingActivityExecutionId,
                branchId: context.ActivityExecutionState.BranchId,
                iterationId: iterationKey,
                executionPathId: executionPathId,
                executionScopeId: executionScopeId,
                schedulingCause: schedulingCause,
                metadata: new Dictionary<string, string>
                {
                    [FlowchartExecutionEngine.TargetNodeIdMetadataKey] = nodeId
                }));

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
