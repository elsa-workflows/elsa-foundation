using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// The definition-keyed ledger of which activation is live per <c>(DefinitionId, SlotName)</c>, with
/// CAS-guarded transitions and explicit ownership.
/// </summary>
/// <remarks>
/// <para>
/// <b>One ledger per engine, physically as well as contractually.</b> This supersedes publishing's
/// <c>IPublicationSlotStore</c>, which is deleted rather than relocated: the runtime must not become
/// responsible for concepts still named "Publication" (FR-B-006, 2026-08-15 architect review). The
/// publish pipeline and the artifact importer therefore share one ledger on any engine, which is what
/// makes a dual-reconciliation overlap structurally detectable instead of silently double-activating.
/// </para>
/// <para>
/// A <b>replacement contract</b> (§2.6.2): a second implementation would mean two answers to "what is
/// live", which is the exact failure this design exists to remove.
/// </para>
/// <para>
/// This contract owns only the CAS transition and the ownership rule. The same-artifact idempotency
/// check lives in <see cref="IWorkflowActivationCoordinator"/>, which can resolve the active
/// activation's artifact from its minted source reference; by the time a request reaches here it is
/// already known to be a genuine change of activation.
/// </para>
/// </remarks>
public interface IWorkflowActivationAuthority
{
    ValueTask<WorkflowActivationSlot?> FindAsync(
        string workflowDefinitionId,
        string slotName,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(
        string workflowDefinitionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// CAS transition making <c>request.ActivationId</c> the live activation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enforces the FR-B-006 conflict rules: a revision mismatch yields
    /// <see cref="WorkflowActivationConflict.RevisionMismatch"/>; a request from a source that does
    /// not own the definition yields <see cref="WorkflowActivationConflict.ForeignSource"/> with a
    /// diagnostic naming the owning source. Ownership is read from the slot's
    /// <see cref="WorkflowActivationSource"/> field only — never inferred from id prefixes.
    /// </para>
    /// <para>
    /// The one exception is a request carrying
    /// <see cref="WorkflowActivationOwnershipIntent.TakeOver"/>, which claims a foreign-owned slot and becomes its
    /// owner (T118). The rule is <b>generic</b>: the authority honours the declared intent and never inspects
    /// which source declared it, so "publishing outranks reconciliation" is a fact about who passes the intent,
    /// not a fact the runtime knows.
    /// </para>
    /// </remarks>
    ValueTask<WorkflowActivationTransition> TryActivateAsync(
        WorkflowActivationSlotRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Clears the live activation for a slot, subject to the same CAS and ownership rules.</summary>
    ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        string workflowDefinitionId,
        string slotName,
        WorkflowActivationSource source,
        long expectedRevision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
}
