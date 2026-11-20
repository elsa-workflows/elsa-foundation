using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// The rules every <see cref="Core.Contracts.IWorkflowActivationSwitch"/> applies to the activations a switch moves and
/// their source references (#2230), so that the EF switch, which applies them in its transaction, and the in-memory one,
/// which applies them by compare-and-swap, agree. Each reference rule returns the reference as the switch writes it, or
/// <see langword="null"/> when it writes nothing.
/// </summary>
public static class WorkflowActivationSwitchRules
{
    /// <summary>
    /// How many times a switch reads again after a concurrent writer moved what it read, before it fails with "changed
    /// concurrently" having committed nothing: the EF switch's transactions and the in-memory switch's reference
    /// compare-and-swaps alike.
    /// </summary>
    public const int MaximumAttempts = 16;

    /// <summary>The activation a successful transition switched off: the one it replaced, unless that is the one it names.</summary>
    public static string? ReplacedActivation(WorkflowActivationTransition transition) =>
        transition is { Succeeded: true, ReplacedActivationId: { } replaced } &&
        !StringComparer.Ordinal.Equals(replaced, transition.Slot.ActiveActivationId)
            ? replaced
            : null;

    /// <summary>The reference of an activation a switch replaced, retired as replaced unless it is retired already.</summary>
    public static WorkflowExecutableSourceReference? RetireReplaced(WorkflowExecutableSourceReference current, DateTimeOffset now) =>
        current.DeletedAt is null ? current.Retire(now, WorkflowActivationCoordinator.ReplacedRetireReason) : null;

    /// <summary>
    /// The reference of an activation that was discarded or reverted, retired as failed so that a retry can resume it. Only
    /// the caller's own live reference is retired: one with another identity belongs to another activation payload.
    /// </summary>
    public static WorkflowExecutableSourceReference? RetireFailed(
        WorkflowExecutableSourceReference current,
        WorkflowExecutableSourceReference own,
        DateTimeOffset now) =>
        current.DeletedAt is null && WorkflowExecutableSourceReferenceComparer.SameIdentity(current, own)
            ? current.Retire(now, WorkflowActivationCoordinator.FailedRetireReason)
            : null;

    /// <summary>
    /// The reference of the activation a revert switches back on, made live again. Only a retirement as replaced, which the
    /// reverted switch made, is undone: a reference retired for any other reason, or another activation's, is left alone.
    /// </summary>
    public static WorkflowExecutableSourceReference? RestoreReplaced(WorkflowExecutableSourceReference current, string activationId) =>
        Restore(current, activationId, WorkflowActivationCoordinator.ReplacedRetireReason);

    /// <summary>
    /// The reference of the activation a switch turns on, made live again when a call that shares its activation id
    /// discarded it as failed after this call minted it (#2251). The activation serves from the switch on, so it must
    /// hold its artifact against garbage collection; a reference retired for any other reason is left alone.
    /// </summary>
    public static WorkflowExecutableSourceReference? ResumeFailed(WorkflowExecutableSourceReference current, string activationId) =>
        Restore(current, activationId, WorkflowActivationCoordinator.FailedRetireReason);

    /// <summary>
    /// Whether an activation whose projection stands as <paramref name="states"/> in the projection stores is one a
    /// version before #2230 left on its way to serving: prepared in every store, or switched on in some and prepared in
    /// the others, because it stopped before or between the projection switches. Missing and replaced projections come
    /// only from a fault or a manual change, and are not repaired.
    /// </summary>
    public static bool IsRepairable(IReadOnlyCollection<WorkflowActivationProjectionState> states) =>
        states.Count > 0 &&
        states.All(state => state is WorkflowActivationProjectionState.Prepared or WorkflowActivationProjectionState.Active) &&
        states.Any(state => state == WorkflowActivationProjectionState.Prepared);

    /// <summary>
    /// What one <see cref="Core.Contracts.IWorkflowActivationSwitch.TryRepairAsync"/> works on, refusing arguments it
    /// cannot: the slot names an activation, and the activations serving in its place do not include it.
    /// </summary>
    public static WorkflowActivationRepairPlan RepairPlan(WorkflowActivationSlot slot, IReadOnlyCollection<string> alsoServing)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(alsoServing);
        var activationId = slot.ActiveActivationId ?? throw new ArgumentException("Only a slot that names an activation can be repaired.", nameof(slot));
        if (alsoServing.Contains(activationId, StringComparer.Ordinal))
            throw new ArgumentException("The activations serving in place of the slot's own cannot include it.", nameof(alsoServing));
        return new(slot, activationId, alsoServing.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static WorkflowExecutableSourceReference? Restore(WorkflowExecutableSourceReference current, string activationId, string retiredFor) =>
        current.DeletedAt is not null &&
        StringComparer.Ordinal.Equals(current.DeletedReason, retiredFor) &&
        StringComparer.Ordinal.Equals(current.ActivationId, activationId)
            ? current with { DeletedAt = null, DeletedReason = null }
            : null;
}

/// <summary>
/// One repair of a slot a version before #2230 left on its way to serving (<see cref="WorkflowActivationSwitchRules.RepairPlan"/>):
/// the slot as its caller read it, the activation it names, and the activations serving in that one's place.
/// </summary>
public sealed record WorkflowActivationRepairPlan(WorkflowActivationSlot Slot, string ActivationId, IReadOnlyList<string> ServingInItsPlace)
{
    /// <summary>The activation's own source reference.</summary>
    public string ReferenceId => WorkflowActivationReferenceIdentity.Create(ActivationId);

    /// <summary>
    /// Whether the repair applies to the slot as it now stands, <paramref name="current"/>, and to the activation's
    /// projection as it stands in each store: the slot still names it at the revision read, and it is repairable.
    /// </summary>
    public bool AppliesTo(WorkflowActivationSlot? current, IReadOnlyCollection<WorkflowActivationProjectionState> states) =>
        current is not null &&
        current.Revision == Slot.Revision &&
        StringComparer.Ordinal.Equals(current.ActiveActivationId, ActivationId) &&
        WorkflowActivationSwitchRules.IsRepairable(states);

    /// <summary>The activation's own reference, made live again if a call that shares its id discarded it as failed.</summary>
    public WorkflowExecutableSourceReference? ResumeOwn(WorkflowExecutableSourceReference current) =>
        WorkflowActivationSwitchRules.ResumeFailed(current, ActivationId);

    /// <summary>The references of the activations serving in its place, each with the rule that retires it as replaced.</summary>
    public IEnumerable<(string ReferenceId, Func<WorkflowExecutableSourceReference, WorkflowExecutableSourceReference?> Retire)> Retirements(DateTimeOffset now) =>
        ServingInItsPlace.Select(other => (
            WorkflowActivationReferenceIdentity.Create(other),
            (Func<WorkflowExecutableSourceReference, WorkflowExecutableSourceReference?>)(current => WorkflowActivationSwitchRules.RetireReplaced(current, now))));
}
