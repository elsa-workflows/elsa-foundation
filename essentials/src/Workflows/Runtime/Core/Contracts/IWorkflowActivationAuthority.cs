using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>The runtime-owned, definition-keyed ledger of active workflow activations.</summary>
public interface IWorkflowActivationAuthority
{
    ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bounded batch of activation slots for the supplied definition ids. The default loops
    /// <see cref="ListByDefinitionAsync"/>; providers with a definition-keyed index override it.
    /// Blank ids are skipped. Definitions with no slots are absent from the result.
    /// </summary>
    async ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionIdsAsync(
        IReadOnlyCollection<string> workflowDefinitionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflowDefinitionIds);
        var slots = new List<WorkflowActivationSlot>();
        foreach (var id in workflowDefinitionIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;
            slots.AddRange(await ListByDefinitionAsync(id, cancellationToken));
        }

        return slots;
    }

    ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default);

    ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        string workflowDefinitionId,
        string slotName,
        WorkflowActivationSource source,
        long expectedRevision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
}
