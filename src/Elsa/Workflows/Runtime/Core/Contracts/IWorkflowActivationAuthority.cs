using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>The runtime-owned, definition-keyed ledger of active workflow activations.</summary>
public interface IWorkflowActivationAuthority
{
    ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default);

    ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default);

    ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        string workflowDefinitionId,
        string slotName,
        WorkflowActivationSource source,
        long expectedRevision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken = default);
}
