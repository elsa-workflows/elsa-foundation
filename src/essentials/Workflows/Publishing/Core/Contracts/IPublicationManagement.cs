using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

// Publishing deliberately has no slot store. The activation ledger is owned by Runtime through
// IWorkflowActivationAuthority, and publishing reaches it through IWorkflowActivationCoordinator.

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

public interface IPublicationProjectionPreparer
{
    ValueTask PrepareAsync(PublicationRecord candidate, CancellationToken cancellationToken = default);

    ValueTask ActivateAsync(
        PublicationRecord candidate,
        string? replacedPublicationId,
        CancellationToken cancellationToken = default);

    ValueTask CompensateAsync(
        PublicationRecord candidate,
        string? restoredPublicationId,
        CancellationToken cancellationToken = default);

    ValueTask RestoreAsync(
        PublicationRecord publication,
        CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(PublicationRecord publication, CancellationToken cancellationToken = default);
}

public interface IPublicationActivator
{
    ValueTask<PublicationActivationResult> ActivateAsync(
        PublicationActivationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPublicationProjectionIntentStore
{
    ValueTask SaveAsync(PublicationProjectionIntent intent, CancellationToken cancellationToken = default);
    ValueTask<PublicationProjectionIntent?> FindAsync(string intentId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<PublicationProjectionIntent>> ListByPublicationAsync(string publicationId, CancellationToken cancellationToken = default);
    ValueTask<PublicationProjectionIntentTransitionResult> TryTransitionAsync(
        PublicationProjectionIntent intent,
        PublicationProjectionIntentStatus expectedStatus,
        CancellationToken cancellationToken = default);
}
