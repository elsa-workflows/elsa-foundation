using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>Persisted correlation/binding data, never native authority by itself.</summary>
public sealed record WorkflowPreparationEfEnrollmentBinding(
    WorkflowPreparationOwnerStamp Owner, string IntentId, string TargetAssociation);

public enum WorkflowPreparationEfEnrollmentStatus { Bound, Accepted, Aborted, Pending, Conflict, Unsupported }

/// <summary>
/// A selected-provider result. Accepted carries the genuine current/predecessor header. Aborted carries
/// a committed permanent barrier incarnation and no header/token. Unknown commit outcomes throw the
/// existing WorkflowPreparationOutcomeUnknownException; absence/timeout never produces Aborted.
/// </summary>
public sealed record WorkflowPreparationEfEnrollmentResult(
    WorkflowPreparationEfEnrollmentStatus Status,
    WorkflowPreparationEfEnrollmentBinding? Binding = null,
    string? BarrierIncarnation = null,
    WorkflowPreparationSnapshot? Header = null,
    string? Code = null)
{
    public static WorkflowPreparationEfEnrollmentResult Unsupported() => new(WorkflowPreparationEfEnrollmentStatus.Unsupported);
}
