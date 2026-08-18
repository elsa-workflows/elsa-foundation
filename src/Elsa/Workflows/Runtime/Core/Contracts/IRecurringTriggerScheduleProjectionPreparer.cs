using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Writes the activation-scoped <b>recurring-schedule</b> projection for a workflow executable (W16). It is
/// invoked by <see cref="IWorkflowActivationCoordinator"/> — the single writer of activation-relevant serving
/// state (FR-B-006) — beside <see cref="IWorkflowTriggerIndexer"/>, before the activation slot CAS.
/// </summary>
/// <remarks>
/// <para>
/// One contract per projection, deliberately. Until spec 151's T044b this preparation was smuggled into a
/// decorator over <see cref="IWorkflowTriggerIndexer"/>, which made that <b>replacement</b> contract silently own
/// a second projection: a host swapping in its own indexer — a thing the extension-point catalog invites — lost
/// the recurring projection, and the loss surfaced only when the coordinator tried to activate it, i.e. AFTER the
/// slot CAS, landing in compensation instead of failing fast. Split apart, replacing either contract can no
/// longer silently disarm the other.
/// </para>
/// <para>
/// The projection is written in <b>prepared</b> (non-serving) state; the coordinator flips it after the slot
/// transition succeeds and compensates a failed attempt. Preparation is <b>unconditional</b>: an engine that
/// composes the recurring store with no <see cref="IRecurringTriggerScheduleProvider"/> at all still gets an
/// explicit empty projection, so a later activate or compensate has a projection to move rather than silently
/// nothing, and no caller has to read the projection back and re-prepare it.
/// </para>
/// </remarks>
public interface IRecurringTriggerScheduleProjectionPreparer
{
    /// <summary>
    /// Materializes every recurring schedule the pinned <paramref name="executable"/> declares and persists them
    /// in prepared (non-serving) state under <paramref name="activationId"/> / <paramref name="slotId"/>.
    /// </summary>
    /// <remarks>
    /// The whole set is materialized — and every recurrence validated — before anything is written, so an invalid
    /// or exhausted recurrence fails the activation with no projection mutated.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="executable"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="activationId"/> or <paramref name="slotId"/> is blank.</exception>
    /// <exception cref="WorkflowTriggerPreflightException">Recurring preflight fails before projection mutation.</exception>
    ValueTask PrepareActivationAsync(
        WorkflowExecutable executable,
        string activationId,
        string slotId,
        CancellationToken cancellationToken = default);
}
