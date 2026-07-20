using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// The single home for scheduling a BPMN element's bound Elsa child activity. This is the one place
/// that both mutates the runtime <see cref="IRuntimeActivityExecutionContext"/> (via
/// <c>ScheduleChildActivity</c>) and records the resulting active child on the execution state —
/// mirroring <c>FlowchartScheduler</c> so all scheduling paths emit identical metadata and provenance.
/// </summary>
internal static class BpmnScheduler
{
    public static BpmnExecutionState ScheduleChild(
        IRuntimeActivityExecutionContext context,
        BpmnExecutionState state,
        string childNodeId,
        string elementId,
        string tokenId,
        string schedulingActivityExecutionId,
        string schedulingCause)
    {
        context.ScheduleChildActivity(
            childNodeId,
            schedulingActivityExecutionId,
            new Dictionary<string, string>
            {
                [BpmnExecutionEngine.ParentActivityExecutionIdMetadataKey] = context.ActivityExecutionState.Execution.ActivityExecutionId,
                [BpmnExecutionEngine.TokenIdMetadataKey] = tokenId,
                [BpmnExecutionEngine.ElementIdMetadataKey] = elementId,
                [BpmnExecutionEngine.SchedulingCauseMetadataKey] = schedulingCause,
                [BpmnExecutionEngine.TargetNodeIdMetadataKey] = childNodeId
            },
            ActivitySchedulingProvenance.From(
                context.WorkflowExecutionId,
                context.ActivityExecutionState.Execution.ActivityExecutionId,
                schedulingActivityExecutionId,
                branchId: context.ActivityExecutionState.BranchId,
                iterationId: null,
                executionPathId: tokenId,
                executionScopeId: null,
                schedulingCause: schedulingCause,
                metadata: new Dictionary<string, string>
                {
                    [BpmnExecutionEngine.TargetNodeIdMetadataKey] = childNodeId,
                    [BpmnExecutionEngine.ElementIdMetadataKey] = elementId
                }));

        var activeChild = new BpmnActiveChild(childNodeId, elementId, tokenId, schedulingCause);
        return BpmnStateMutator.AddActiveChild(state, activeChild);
    }
}
