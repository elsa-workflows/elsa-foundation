using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Bound to the selected Runtime provider and configured canonical issuer; no caller target or lease.</summary>
public interface IWorkflowPreparationEfEnrollmentSettlement
{
    // Native target selection and durable canonical enrollment precede every first Retain launch.
    ValueTask<WorkflowPreparationEfEnrollmentResult> BindTargetAsync(
        WorkflowPreparationOwnerStamp expectedOwner, CancellationToken cancellationToken = default);
    ValueTask<WorkflowPreparationEfEnrollmentResult> SettleAsync(
        WorkflowPreparationOwnerStamp expectedOwner, CancellationToken cancellationToken = default);
}

public interface IWorkflowPreparationEfEnrollmentSettlementFactory
{
    IWorkflowPreparationEfEnrollmentSettlement Bind(IWorkflowPreparationOwnerIssuer configuredIssuer);
}

/// <summary>Optional selected-provider facet. Legacy providers remain unsupported, never false absence.</summary>
public interface IWorkflowPreparationEfEnrollmentParticipant : IWorkflowPreparationEfCommitParticipant
{
    bool CanSettleEnrollment(IWorkflowPreparationOwnerIssuer configuredIssuer) => false;
    ValueTask<WorkflowPreparationEfEnrollmentResult> BindEnrollmentTargetAsync(
        WorkflowPreparationOwnerSession owner, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(WorkflowPreparationEfEnrollmentResult.Unsupported());
    ValueTask<WorkflowPreparationEfEnrollmentResult> SettleEnrollmentAsync(
        WorkflowPreparationOwnerSession owner, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(WorkflowPreparationEfEnrollmentResult.Unsupported());
}

