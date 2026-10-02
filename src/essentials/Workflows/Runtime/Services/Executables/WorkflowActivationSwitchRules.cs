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

    private static WorkflowExecutableSourceReference? Restore(WorkflowExecutableSourceReference current, string activationId, string retiredFor) =>
        current.DeletedAt is not null &&
        StringComparer.Ordinal.Equals(current.DeletedReason, retiredFor) &&
        StringComparer.Ordinal.Equals(current.ActivationId, activationId)
            ? current with { DeletedAt = null, DeletedReason = null }
            : null;
}
