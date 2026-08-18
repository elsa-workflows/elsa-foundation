using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// The single entry point for making a workflow executable live — and for taking it back out — owning the
/// <b>complete</b> activation lifecycle for every calling path (FR-B-006).
/// </summary>
/// <remarks>
/// <para>
/// The sequence, in order: take the executable's root-write lease (so reference garbage collection cannot race
/// the activation) → mint and save the live source reference → prepare <b>both</b> serving projections (trigger
/// bindings and recurring trigger schedules — a binding-only activation imports timer/cron workflows that never
/// fire) → CAS the slot on <see cref="IWorkflowActivationAuthority"/> → activate both projections → notify every
/// <see cref="IWorkflowTriggerIndexObserver"/> → retire the predecessor's reference with reason
/// <c>"activation-replaced"</c>.
/// </para>
/// <para>
/// On a mid-sequence failure the coordinator compensates: it restores the replaced activation on the authority,
/// re-activates that predecessor's projections unconditionally, removes the candidate's projections, and retires
/// the candidate's reference with reason <c>"activation-failed"</c>. Compensation is best-effort and never
/// masks the original failure — every step that does not converge is appended to the result's diagnostic.
/// </para>
/// <para>
/// <b>Callers request activation; they never implement it.</b> Publishing keeps compilation, publication policy
/// and its <c>IPublicationRecordStore</c> attempt journal; the artifact reconciler keeps closure validation and
/// import logic. Neither may hold a parallel copy of this sequence — that duplicated authority is precisely what
/// this contract exists to remove.
/// </para>
/// <para>
/// A <b>replacement contract</b> (§2.6.2): a second coordinator would mean a second activation lifecycle, so
/// registration is <c>TryAdd</c>, whose first-wins semantics prevent the silent last-write-wins §2.6.2 forbids.
/// </para>
/// </remarks>
public interface IWorkflowActivationCoordinator
{
    /// <summary>Runs the complete activation lifecycle for one candidate.</summary>
    /// <remarks>
    /// Refusals and compensated failures are returned as a <see cref="WorkflowActivationResult"/>, not thrown —
    /// the importer's batch isolation and publishing's per-attempt journal both need the outcome as a value.
    /// Only conditions that prevent the sequence from being attempted at all (an uncomposed trigger spine, an
    /// unavailable retention lease, an infrastructure fault escaping the lease manager) throw, wrapped in
    /// <see cref="Exceptions.WorkflowActivationException"/> per §2.23.5.
    /// </remarks>
    ValueTask<WorkflowActivationResult> ActivateAsync(
        WorkflowActivationCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>Runs the complete deactivation lifecycle for whatever the slot currently serves.</summary>
    /// <remarks>
    /// <para>
    /// The sequence, in order: CAS the slot empty on <see cref="IWorkflowActivationAuthority"/> → remove the
    /// retracted activation's serving projections → notify every <see cref="IWorkflowTriggerIndexObserver"/>. A
    /// slot that already serves nothing is <see cref="WorkflowActivationOutcome.AlreadyInactive"/> and writes
    /// nothing, so a repeated retraction converges.
    /// </para>
    /// <para>
    /// On a post-flip failure the coordinator compensates by re-activating the slot, <b>re-preparing and
    /// re-activating</b> the projections from the artifact — the same preparation, in the same order, that
    /// <see cref="ActivateAsync"/> runs — and reconciling observers afterwards. That shared preparation is the
    /// point of putting deactivation here: a separate retraction path had to know the same ordering invariant
    /// (recurrences materialized and validated before any binding is written), and the two drifted apart once
    /// already.
    /// </para>
    /// <para>
    /// Callers keep what is theirs. Publishing still retires its <c>PublicationRecord</c> and the unpublished
    /// activation's source reference; the runtime owns the slot, the projections and the undo.
    /// </para>
    /// </remarks>
    ValueTask<WorkflowActivationResult> DeactivateAsync(
        WorkflowDeactivationCommand command,
        CancellationToken cancellationToken = default);
}
