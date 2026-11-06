using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Distributed.Contracts;

/// <summary>One provider-owned qualification of the actual transport, physical context, partition and live format gate.</summary>
/// <remarks>Missing or duplicate owners refuse selected test routing. A declaration of generic durability is insufficient.</remarks>
public interface IExecutionCommandTransportTestBoundary
{
    bool CanTransportTestEntry(WorkflowTestEntry entry, IExecutionCommandTransport physicalTransport);
}
