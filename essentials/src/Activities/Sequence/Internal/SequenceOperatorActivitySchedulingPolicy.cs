using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Sequence.Internal;

/// <summary>Sequence owns the lifecycle rule for an operator-scheduled direct child.</summary>
internal sealed class SequenceOperatorActivitySchedulingPolicy : IOperatorActivitySchedulingPolicy
{
    public string PolicyKey => SequenceOperatorActivitySchedulingCapabilityProvider.PolicyKey;
    public int SchemaVersion => SequenceOperatorActivitySchedulingCapabilityProvider.SchemaVersion;

    public OperatorActivitySchedulingPolicyDecision Evaluate(OperatorActivitySchedulingPolicyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Parent.Status is not (ActivityExecutionStatus.Running or ActivityExecutionStatus.Waiting))
            return OperatorActivitySchedulingPolicyDecision.Rejected("ParentNotActive", "The requested parent activity is not active.");

        var conflictingChild = context.ActivityExecutions.Any(state =>
            StringComparer.Ordinal.Equals(state.ParentActivityExecutionId, context.Parent.Execution.ActivityExecutionId) &&
            StringComparer.Ordinal.Equals(state.Execution.ExecutableNodeId, context.Child.ExecutableNodeId) &&
            state.Status is ActivityExecutionStatus.Scheduled or ActivityExecutionStatus.Running or ActivityExecutionStatus.Waiting or ActivityExecutionStatus.Suspended or ActivityExecutionStatus.Faulted);
        if (conflictingChild)
            return OperatorActivitySchedulingPolicyDecision.Rejected("ConflictingLiveChild", "The requested child relation already has a live execution.");

        var provenance = ActivitySchedulingProvenance.From(
            context.Workflow.WorkflowExecutionId,
            context.Parent.Execution.ActivityExecutionId,
            context.Parent.Execution.ActivityExecutionId,
            context.Parent.BranchId,
            context.Parent.IterationId,
            context.Parent.Provenance.ExecutionPathId,
            context.Parent.ExecutionScopeId ?? context.Parent.Provenance.ExecutionScopeId ?? context.Parent.Execution.ActivityExecutionId,
            "OperatorScheduleActivity");
        return OperatorActivitySchedulingPolicyDecision.Accepted(provenance);
    }
}
