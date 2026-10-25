using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Resolves an explicit control boundary from committed workflow, dispatch and activity identities.</summary>
/// <remarks>
/// Single-implementation replacement contract. A host may register one scoped provider before the DispatchWorkflow
/// feature, or replace its default descriptor explicitly. The feature preserves that override and rejects zero or
/// multiple providers at shell startup. Resolution never fans out. A null result preserves ordinary no-capability behavior.
/// </remarks>
/// <exception cref="Exceptions.WorkflowControlBoundaryException">Retained identity is inconsistent or a store cannot be read safely; the original failure is retained as InnerException.</exception>
/// <exception cref="OperationCanceledException">Cancellation passes through without wrapping.</exception>
public interface IRuntimeWorkflowControlBoundary
{
    /// <summary>Returns null for an execution without the capability; conflicting retained identity is a failure.</summary>
    ValueTask<RuntimeChildControlTarget?> ResolveAsync(string workflowExecutionId, string rootActivityExecutionId,
        CancellationToken cancellationToken = default);
}

/// <summary>Marks a structural activity that declares its control protocol through ActivityChildControlConsumerAttribute.</summary>
public interface IRuntimeActivityChildControlConsumer;
