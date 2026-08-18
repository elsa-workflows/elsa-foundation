using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

// The publication slot store is deliberately absent: publishing no longer owns an activation ledger.
// `IWorkflowActivationAuthority` (Elsa.Workflows.Runtime.Core) is the one ledger per engine, and publishing
// reads and writes it through `IWorkflowActivationCoordinator` (FR-B-006, 2026-08-15 architect review).

public interface IPublicationRecordStore
{
    ValueTask SaveAsync(PublicationRecord publication, CancellationToken cancellationToken = default);
    ValueTask<PublicationRecord?> FindAsync(string publicationId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<PublicationRecord>> ListBySlotAsync(string slotId, CancellationToken cancellationToken = default);
    ValueTask<bool> TryTransitionAsync(
        PublicationRecord publication,
        PublicationStatus expectedStatus,
        CancellationToken cancellationToken = default);
}

public sealed record PublicationPolicyWriteResult(bool Succeeded, PublicationPolicy Policy);

public interface IPublicationPolicyStore
{
    ValueTask<PublicationPolicy?> FindAsync(string? workflowDefinitionId, CancellationToken cancellationToken = default);
    ValueTask<PublicationPolicyWriteResult> TrySaveAsync(
        PublicationPolicy policy,
        long expectedRevision,
        CancellationToken cancellationToken = default);
}

public interface IPublicationPolicyResolver
{
    ResolvedPublicationAction Resolve(
        string workflowDefinitionId,
        string workflowDefinitionVersionId,
        PublicationRequestIntent? request,
        PublicationPolicy? workflowPolicy,
        PublicationPolicy hostPolicy);
}

public interface IPublicationPreflightService
{
    PublicationPreflightResult Evaluate(
        IReadOnlyCollection<PublicationTriggerClaim> candidateClaims,
        IReadOnlyCollection<PublicationAuthoritativeClaimSet> authoritativeClaims);
}

// `IPublicationProjectionPreparer` is deliberately absent (T121). Serving projections have one owner in either
// direction of the lifecycle: `IWorkflowActivationCoordinator` (Elsa.Workflows.Runtime.Core) prepares, activates,
// removes and restores them for publishing and for artifact reconciliation alike. A publishing-side copy of that
// sequence had to stay in step with the runtime's and did not, so it was deleted rather than re-synchronised.

public interface IPublicationActivator
{
    ValueTask<PublicationActivationResult> ActivateAsync(
        PublicationActivationRequest request,
        CancellationToken cancellationToken = default);
}

// `IPublicationProjectionIntentStore` is deliberately absent (T122). It was the delivery-intent ledger of the
// projection reconciler T121 deleted, and it outlived its only consumer: a `public` contract, two models, two
// implementations and a Groundwork document kind that nothing wrote to. `IWorkflowActivationCoordinator`
// carries no delivery-intent ledger by design — the recovery unit is the caller's next attempt, which is safe
// because a compensated failure leaves nothing half-done. Removed rather than left standing, because a
// supported-looking ledger invites composers to write to it.
