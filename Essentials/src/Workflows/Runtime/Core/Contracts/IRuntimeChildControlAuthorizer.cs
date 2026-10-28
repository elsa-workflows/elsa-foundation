using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Validates a child-control delivery against its exact retained boundary and immutable committed source outbox item.</summary>
/// <remarks>
/// Single scoped replacement contract owned by DispatchWorkflow. Register one provider before the feature or replace
/// its descriptor; startup rejects missing or multiple providers. No fanout. Validation neither delivers nor acknowledges
/// a batch, and never substitutes the latest root projection for a retained source item.
/// </remarks>
/// <exception cref="Exceptions.WorkflowControlBoundaryException">Source authority or retained identity is missing, conflicting or unreadable; the original cause is retained.</exception>
/// <exception cref="OperationCanceledException">Cancellation passes through unchanged.</exception>
public interface IRuntimeChildControlAuthorizer
{
    ValueTask ValidateAsync(RuntimeChildControlTransport transport, CancellationToken cancellationToken = default);
}
