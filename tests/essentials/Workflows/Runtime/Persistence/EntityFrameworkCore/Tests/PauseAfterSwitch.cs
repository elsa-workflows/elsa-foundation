using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Holds an activation once its switch commits, as a process stopping there would (#2230): the slot, the projections and
/// the replaced activation's reference have all moved, and the trigger observers are not yet told. The call returns when
/// <c>resume</c> completes, and never when none is given. Only the first successful switch pauses; a revert and any
/// later switch pass straight through.
/// </summary>
/// <remarks>Shared by the Runtime contract and the Publishing tests, which link this file.</remarks>
internal sealed class PauseAfterSwitch(IWorkflowActivationSwitch inner, Task? resume = null) : IWorkflowActivationSwitch
{
    private readonly TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Paused => _paused.Task;

    public async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
    {
        var transition = await inner.TryActivateAsync(request, cancellationToken);
        if (transition.Succeeded && _paused.TrySetResult())
            await (resume ?? new TaskCompletionSource().Task);
        return transition;
    }

    public ValueTask<bool> TryRevertAsync(WorkflowActivationRevert revert, CancellationToken cancellationToken = default) =>
        inner.TryRevertAsync(revert, cancellationToken);

    public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        WorkflowDeactivationSlotRequest request,
        IReadOnlyCollection<string> alsoServing,
        CancellationToken cancellationToken = default) =>
        inner.TryDeactivateAsync(request, alsoServing, cancellationToken);

    public ValueTask<bool> TryDiscardAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) =>
        inner.TryDiscardAsync(reference, cancellationToken);
}
