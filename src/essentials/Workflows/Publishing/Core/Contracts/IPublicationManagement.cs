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

public interface IPublicationActivator
{
    /// <summary>
    /// Activates a candidate through the runtime coordinator and records the outcome in the publication journal. It
    /// first completes the publication the slot already names (see <see cref="CompleteAsync"/>), so a replacement never
    /// starts from a journal that lags the slot.
    /// </summary>
    ValueTask<PublicationActivationResult> ActivateAsync(
        PublicationActivationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes the activation the slot names through <c>IWorkflowActivationCoordinator.CompleteAsync</c>, then brings
    /// the publication journal into line with the slot (#2223). A process that stops after the slot transition leaves
    /// the slot's publication a <see cref="PublicationStatus.Candidate"/> and the one it replaced
    /// <see cref="PublicationStatus.Active"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Once the slot's activation serves, its publication becomes <see cref="PublicationStatus.Active"/> with an
    /// activation time, and every other active publication of the slot whose source reference the runtime has retired is
    /// <see cref="PublicationStatus.Retired"/>. Marking the slot's publication active is the last write, both here and in
    /// <see cref="ActivateAsync"/>, so a process that stops part way leaves it lagging and the next completion finishes
    /// the job; when it is already active, the journal costs one read. Every transition is a status compare-and-swap,
    /// so a repeated or concurrent completion changes nothing more.
    /// </para>
    /// <para>
    /// The journal is left alone when completion fails, when the slot is empty or owned by another activation source,
    /// when the slot moved during completion, and when the slot's publication has failed or its source reference is
    /// retired: none of those is a publication that serves.
    /// </para>
    /// </remarks>
    ValueTask<PublicationCompletionResult> CompleteAsync(
        string workflowDefinitionId,
        string slotName,
        CancellationToken cancellationToken = default);
}

/// <summary>Durable, idempotent prepare/activate/remove intents for one publication's serving projections.</summary>
/// <remarks>
/// Nothing writes intents any more. Their only writer, <c>PublicationProjectionReconciler</c>, had not been registered
/// since activation moved to the runtime <c>IWorkflowActivationCoordinator</c>, and was removed (#2193). The contract
/// and its EF table stay so existing rows remain readable; removing them is a pending schema decision.
/// </remarks>
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
