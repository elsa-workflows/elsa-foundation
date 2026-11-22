using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Moves an activation slot and switches its serving projections in one commit (#2230). This is where the activation
/// commit is described; the coordinator, the stores and Publishing refer here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Invariant.</b> The activation a slot names serves through every projection store, no other activation of that slot
/// serves, the activation it replaced has a retired source reference, and no activation serves with a retired reference.
/// Every operation keeps it by being one commit. It writes nothing when the slot is not where its caller expects it, or
/// when a projection cannot be switched, so a refusal or a failure leaves everything as it was, and a process that stops
/// after it leaves everything as it committed.
/// </para>
/// <para>
/// <b>Mechanism.</b> <c>EfWorkflowActivationSwitch</c> makes each operation one transaction of the shared
/// <c>RuntimeDbContext</c>, in which every row it writes carries its revision as a concurrency token, so two operations
/// that write one slot or one activation's projection serialize: the one that commits second reads everything again.
/// <c>InMemoryWorkflowActivationSwitch</c> holds the slot authority's lock and each projection store's together.
/// </para>
/// <para>
/// <b>Cancellation.</b> The commit is the point of no return. A cancelled operation that has not committed changes
/// nothing; one cancelled as it commits may have committed, so its caller reads what it did rather than assume either;
/// and a commit stands whatever its caller observes afterwards.
/// </para>
/// <para>
/// <b>Revert and discard.</b> Nothing infers which transition is whose. Only the caller that made a transition reverts it,
/// and only while the slot still stands where it left it (<see cref="TryRevertAsync"/>). A caller that stopped short of its
/// own switch discards its activation (<see cref="TryDiscardAsync"/>), which is refused while the activation serves: calls
/// that share an activation id, such as two nodes reconciling one mounted set, share its source reference and projections,
/// and the one whose switch committed owns them (#2251). A discard that commits first leaves a concurrent switch of the
/// same activation nothing to switch on, so that switch fails loudly and leaves the slot as it was, unless its caller
/// prepared again after the discard; a discard that would commit after the switch is refused, and its caller answers as
/// the slot stands. Neither leaves the slot naming an activation that serves nothing, and neither hands a slot back.
/// </para>
/// <para>
/// <b>Composition.</b> One backend owns every store an operation writes, so a switch is composed with the slot authority
/// and the projection stores of its backend and refuses any other when it is constructed. Shell start constructs it once
/// and fails on that refusal, so a mixed composition, such as EF slots beside in-memory projections, never starts.
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
    /// its activation id discarded it as failed meanwhile.
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

    /// <summary>
    /// Repairs a slot a version before #2230 left on its way to serving. That version committed the slot transition before
    /// the projection switches, one store after the other, so a process that stopped in between left the slot naming an
    /// activation that is prepared in every projection store, or switched on in some and prepared in the others
    /// (<c>WorkflowActivationSwitchRules.IsRepairable</c>). In one commit the repair switches
    /// that activation on where it is prepared, deletes the projections of each of <paramref name="alsoServing"/> and
    /// retires their references as replaced, and makes the activation's own reference live again if it was retired as
    /// failed. The slot itself is not written: it already says what serves, so a caller's expected revision stays valid.
    /// </summary>
    /// <param name="slot">The slot as its caller read it; the repair is made only while the slot still stands so.</param>
    /// <param name="alsoServing">The other activations a projection store lists as serving the slot; not the slot's own.</param>
    /// <returns>
    /// <see langword="false"/>, having changed nothing, when the slot has moved since <paramref name="slot"/> was read, or
    /// the activation it names is not on its way to serving: serving everywhere already, or missing or replaced anywhere.
    /// </returns>
    ValueTask<bool> TryRepairAsync(
        WorkflowActivationSlot slot,
        IReadOnlyCollection<string> alsoServing,
        CancellationToken cancellationToken = default);
}
