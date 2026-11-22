using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Api.Contracts.Alterations;

/// <summary>Seals request-scoped tenant and operator facts before an alteration plan is admitted.</summary>
public interface IWorkflowAlterationRequestContext
{
    WorkflowAlterationAuthorityScope AuthorityScope { get; }
    WorkflowAlterationOperatorProvenance Operator { get; }
    bool CanAccess(WorkflowAlterationPlanState plan);

    /// <summary>
    /// Decides whether the caller may manage one explicitly named run, with the run as the permission resource, as run
    /// deletion does. A refused run is recorded as not found, never reported as refused.
    /// </summary>
    ValueTask<bool> CanManageAsync(WorkflowExecutionState execution, CancellationToken cancellationToken = default);
}

/// <summary>Optional pre-admission capacity gate. A rejection occurs before an idempotency key is consumed.</summary>
public interface IWorkflowAlterationAdmissionGate
{
    ValueTask<WorkflowAlterationAdmissionDecision> EvaluateAsync(CancellationToken cancellationToken = default);
}

public sealed record WorkflowAlterationAdmissionDecision(bool IsAccepted, TimeSpan? RetryAfter = null)
{
    public static WorkflowAlterationAdmissionDecision Accepted { get; } = new(true);
}
