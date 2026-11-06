using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;

/// <summary>
/// An actual selected provider's private active frame, verified by RequireActiveContext. Implementing
/// these methods is a native qualification obligation, not proof from a string, GUID, path or schema stamp.
/// No existing SQLite/native-R2 frame is silently admitted by this additive contract.
/// </summary>
public interface IWorkflowPreparationEfEnrollmentCommitContext : IWorkflowPreparationEfCommitContext
{
    ValueTask RequireEnrollmentProtocolReadyAsync(CancellationToken cancellationToken = default);
    ValueTask RequireTargetContinuityAsync(WorkflowPreparationEfEnrollmentBinding enrollment,
        CancellationToken cancellationToken = default);
    // The provider must verify the real roots/protection written by all of its acceptance entries.
    ValueTask RequireAcceptedHeaderProtectionAsync(WorkflowPreparationSnapshot header,
        CancellationToken cancellationToken = default);
    ValueTask RequirePredecessorHeaderCustodyAsync(WorkflowPreparationSnapshot header,
        CancellationToken cancellationToken = default);
}

/// <summary>Fresh canonical owner records, read only after genuine native Join.</summary>
public interface IWorkflowPreparationEfEnrollmentOwnerLease : IWorkflowPreparationEfOwnerLease
{
    // Null is unknown/unarmed observation, never evidence no prior independent COMMIT can succeed.
    WorkflowPreparationEfEnrollmentBinding? RequireCommittedEnrollment();
}
