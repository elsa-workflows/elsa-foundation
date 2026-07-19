using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Resolves a query-independent parent-cancellation request at the provider's atomic dispatch boundary.</summary>
public interface IWorkflowDispatchCancellationStore
{
    ValueTask<WorkflowDispatchCancellationResult> ApplyCancellationAsync(
        WorkflowDispatchCancellationRequest request,
        CancellationToken cancellationToken = default);
}
