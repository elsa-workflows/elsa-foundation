using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Holds an activation once its slot transition commits, as a process stopping there would (#2193). The call returns
/// when <c>resume</c> completes, and never when none is given, so meanwhile the coordinator neither switches the
/// projections nor compensates. Only the first successful transition pauses; a compensating one passes straight through.
/// </summary>
/// <remarks>Shared by the Runtime contract and the Publishing tests, which link this file.</remarks>
internal sealed class PauseAfterSlotTransition(IWorkflowActivationAuthority inner, Task? resume = null) : IWorkflowActivationAuthority
{
    private readonly TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Paused => _paused.Task;

    public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) =>
        inner.FindAsync(workflowDefinitionId, slotName, cancellationToken);

    public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
        inner.ListByDefinitionAsync(workflowDefinitionId, cancellationToken);

    public async ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default)
    {
        var transition = await inner.TryActivateAsync(request, cancellationToken);
        if (transition.Succeeded && _paused.TrySetResult())
            await (resume ?? new TaskCompletionSource().Task);
        return transition;
    }

    public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        string workflowDefinitionId,
        string slotName,
        WorkflowActivationSource source,
        long expectedRevision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default) =>
        inner.TryDeactivateAsync(workflowDefinitionId, slotName, source, expectedRevision, updatedAt, cancellationToken);
}
