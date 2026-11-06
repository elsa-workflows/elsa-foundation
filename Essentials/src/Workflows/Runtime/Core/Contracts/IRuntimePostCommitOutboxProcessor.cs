using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimePostCommitOutboxProcessor
{
    bool SupportsSourceKind(RuntimeDeliverySourceKind sourceKind) => sourceKind == RuntimeDeliverySourceKind.WorkflowCheckpoint;
    ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
        RuntimePostCommitOutboxProcessRequest request,
        CancellationToken cancellationToken = default);
}
