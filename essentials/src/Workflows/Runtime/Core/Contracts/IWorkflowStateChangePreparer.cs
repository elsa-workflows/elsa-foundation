using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Prepares an output assignment through the runtime's canonical encoding and output projection.</summary>
/// <remarks>
/// A scoped single-provider replacement contract. Replace the default registration with one scoped implementation;
/// adding a second provider is rejected at startup. Preparation does not create or accept a separate transaction.
/// </remarks>
public interface IWorkflowStateChangePreparer
{
    /// <summary>Encodes a fresh payload under its effective policy without accepting a checkpoint.</summary>
    ValueTask<RuntimeStateChange<DurableValueState>> PrepareOutputAsync(
        string workflowExecutionId, string invocationId, string name, ValueEnvelope value,
        DateTimeOffset occurredAt, CancellationToken cancellationToken);
}
