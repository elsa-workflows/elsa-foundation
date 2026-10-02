using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Moves an activation slot and switches its serving projections in one commit (#2230): the slot, the trigger bindings
/// and recurring schedules of the activations it names, and their source references never disagree, whoever reads them
/// and wherever a process stops.
/// </summary>
/// <remarks>
/// <para>
/// The property every operation keeps: the activation a slot names serves through every projection store, and no other
/// activation minted for that slot does. An operation is one commit. It writes nothing when the slot is not where the
/// caller expects it, and nothing when a projection cannot be switched, so a refusal or a failure leaves everything as
/// it was, and a process that stops after it leaves everything as it committed.
/// </para>
/// <para>
/// One backend owns every store an operation writes, so a switch is composed with the slot authority and the projection
/// stores of that backend and refuses any other: <c>EfWorkflowActivationSwitch</c> commits them in one
/// <c>RuntimeDbContext</c> transaction, and <c>InMemoryWorkflowActivationSwitch</c> holds the in-memory stores' locks
/// together. There is no switch for a mixed composition, such as EF slots beside in-memory projections, because nothing
/// could commit them together.
/// </para>
/// <para>
/// Only <see cref="IWorkflowActivationCoordinator"/> calls it. The slot authority's own transitions and the projection
/// stores' own switches remain for their contracts and their tests, but nothing that serves a slot calls them separately.
/// </para>
/// </remarks>
public interface IWorkflowActivationSwitch
{
    /// <summary>
    /// Moves the slot to <paramref name="request"/>'s activation by compare-and-swap, and in the same commit switches that
    /// activation's prepared projections on, switches the projections of the activation the slot named off, and retires
    /// that activation's source reference as replaced. The activation's own reference is made live again if a call sharing
    /// its activation id discarded it as failed meanwhile, so an activation never serves with a retired reference.
    /// </summary>
    /// <returns>
    /// The transition, naming the activation it replaced; or a refusal, which changed nothing, when the slot is not at the
    /// expected revision or another source owns it.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// A projection could not be switched, for instance because the activation's projection is not prepared in every
    /// store, or the activation the slot names does not serve. Nothing was committed.
    /// </exception>
    ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes <paramref name="revert"/>'s transition, which its caller made itself, in one commit: moves the slot back to
    /// the activation the transition replaced (or empties it when there was none) by compare-and-swap on the revision the
    /// transition produced, switches that activation's projections back on and restores its source reference, deletes
    /// the projections of the activation the transition switched on, and retires that one's reference as failed.
    /// </summary>
    /// <returns><see langword="false"/>, having changed nothing, when the slot has moved on since the transition.</returns>
    ValueTask<bool> TryRevertAsync(WorkflowActivationRevert revert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Empties the slot by compare-and-swap, and in the same commit deletes the projections of the activation it named and
    /// of each of <paramref name="alsoServing"/>: activations an earlier version left serving beside it.
    /// </summary>
    /// <returns>The transition, naming the activation it cleared; or a refusal, which changed nothing.</returns>
    ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        WorkflowDeactivationSlotRequest request,
        IReadOnlyCollection<string> alsoServing,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards an activation that did not reach the slot, in one commit: deletes its projections from every projection
    /// store and retires its source reference as failed, unless it serves. An activation that serves was switched on with
    /// the slot by a call that shares its activation id, so it is left to the slot: compensating it would leave the slot
    /// naming an activation that serves nothing (#2251).
    /// </summary>
    /// <param name="reference">The activation's source reference as its caller minted or resumed it; a reference with another identity is not retired.</param>
    /// <returns><see langword="false"/>, having changed nothing, when the activation serves.</returns>
    ValueTask<bool> TryDiscardAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default);
}
