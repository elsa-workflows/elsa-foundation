using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Writes the activation-scoped trigger projection for a workflow executable (W7, E3-1). It is invoked by
/// <see cref="IWorkflowActivationCoordinator"/> — the single writer of activation-relevant serving state
/// (FR-B-006) — between storing the executable and flipping the activation slot.
/// </summary>
/// <remarks>
/// <para>
/// The projection is written in <b>prepared</b> (non-serving) state and only becomes visible to the stimulus
/// router when the coordinator activates it after the slot transition succeeds. A failure fails the activation:
/// there is no silently unindexed serving trigger, and the coordinator's compensation removes whatever a
/// partially-written attempt left behind.
/// </para>
/// <para>
/// <b>There is deliberately no artifact-scoped write path.</b> An earlier <c>IndexAsync(executable)</c> member —
/// delete-every-binding-of-the-artifact then write rows born active — bypassed the prepare/activate lifecycle
/// entirely and wiped the projections of every other activation sharing the artifact. It was removed with its
/// default-interface fallback (spec 151, FR-B-006 writer census), so an implementation that provides only the
/// legacy shape now fails to satisfy this contract instead of being silently routed into that wipe.
/// </para>
/// </remarks>
public interface IWorkflowTriggerIndexer
{
    /// <summary>
    /// Extracts and validates the activation-scoped projection for <paramref name="executable"/>, then persists
    /// it in prepared (non-serving) state under <paramref name="activationId"/> / <paramref name="slotId"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="executable"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="activationId"/> or <paramref name="slotId"/> is blank.</exception>
    /// <exception cref="WorkflowTriggerPreflightException">Trigger preflight fails before index mutation.</exception>
    ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(
        WorkflowExecutable executable,
        string activationId,
        string slotId,
        CancellationToken cancellationToken = default);
}
