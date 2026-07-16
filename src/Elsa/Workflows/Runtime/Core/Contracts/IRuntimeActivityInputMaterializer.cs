using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeActivityInputMaterializer
{
    ValueTask<ActivityInputSnapshot> MaterializeSnapshotAsync(
        ExecutableNode node,
        string invocationId,
        RuntimeInputBindingResolutionContext resolutionContext,
        DateTimeOffset materializedAt,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<RuntimeMaterializedActivityInput>> MaterializeInputsAsync(
        ExecutableNode node,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<RuntimeMaterializedActivityInput>> MaterializeInputsAsync(
        ExecutableNode node,
        RuntimeInputBindingResolutionContext resolutionContext,
        CancellationToken cancellationToken = default);
}
