using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Forwards every <see cref="IWorkflowActivationSwitch"/> operation to <c>inner</c>; a test double derives from it and
/// overrides only the operation it intercepts.
/// </summary>
/// <remarks>Shared by the Runtime and Publishing tests, which link this file.</remarks>
internal abstract class ForwardingActivationSwitch(IWorkflowActivationSwitch inner) : IWorkflowActivationSwitch
{
    public virtual ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) =>
        inner.TryActivateAsync(request, cancellationToken);

    public virtual ValueTask<bool> TryRevertAsync(WorkflowActivationRevert revert, CancellationToken cancellationToken = default) =>
        inner.TryRevertAsync(revert, cancellationToken);

    public virtual ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        WorkflowDeactivationSlotRequest request,
        IReadOnlyCollection<string> alsoServing,
        CancellationToken cancellationToken = default) =>
        inner.TryDeactivateAsync(request, alsoServing, cancellationToken);

    public virtual ValueTask<bool> TryDiscardAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) =>
        inner.TryDiscardAsync(reference, cancellationToken);

    public virtual ValueTask<bool> TryRepairAsync(
        WorkflowActivationSlot slot,
        IReadOnlyCollection<string> alsoServing,
        CancellationToken cancellationToken = default) =>
        inner.TryRepairAsync(slot, alsoServing, cancellationToken);
}
