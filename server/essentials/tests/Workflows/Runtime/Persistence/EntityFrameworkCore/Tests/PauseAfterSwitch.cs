using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Tests.Fixtures;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Holds an activation once its switch commits, as a process stopping there would (#2230): the slot, the projections and
/// the replaced activation's reference have all moved, and the trigger observers are not yet told. The call returns when
/// <c>resume</c> completes, and never when none is given. Only the first successful switch pauses; a revert and any
/// later switch pass straight through.
/// </summary>
/// <remarks>Shared by the Runtime contract and the Publishing tests, which link this file.</remarks>
internal sealed class PauseAfterSwitch(IWorkflowActivationSwitch inner, Task? resume = null) : ForwardingActivationSwitch(inner)
{
    private readonly TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Paused => _paused.Task;

    public override async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
    {
        var transition = await base.TryActivateAsync(request, cancellationToken);
        if (transition.Succeeded && _paused.TrySetResult())
            await (resume ?? new TaskCompletionSource().Task);
        return transition;
    }
}
