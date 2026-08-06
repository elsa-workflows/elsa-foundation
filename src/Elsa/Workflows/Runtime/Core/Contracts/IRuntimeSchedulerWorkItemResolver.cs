using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeSchedulerWorkItemResolver
{
    const int MaximumPageSize = 250;

    /// <summary>Resolves one declared scheduler-work authority against the durable provider.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="authority"/> is null.</exception>
    /// <exception cref="ArgumentException">A required supported-authority identity is blank.</exception>
    /// <exception cref="Elsa.Workflows.Runtime.Core.Exceptions.RuntimeSchedulerWorkRecoveryResolutionException">
    /// The durable provider failed while resolving the scheduler work item.
    /// </exception>
    ValueTask<RuntimeCheckpointRecoveryResolution> ResolveAsync(
        RuntimeCheckpointRecoveryAuthority authority,
        CancellationToken cancellationToken = default);
}
