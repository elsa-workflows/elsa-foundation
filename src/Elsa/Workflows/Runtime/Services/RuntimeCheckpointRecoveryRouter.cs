using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeCheckpointRecoveryRouter(IRuntimeSchedulerWorkItemResolver resolver)
{
    /// <summary>
    /// Routes a declared recovery authority to its durable scheduler-work provider. A null authority resolves as
    /// source-free <see cref="RuntimeCheckpointRecoveryStatus.Absent"/> without consulting the provider.
    /// </summary>
    /// <exception cref="ArgumentException">A required identity on a supported scheduler-work authority is blank.</exception>
    /// <exception cref="InvalidOperationException">The resolver returned <c>Absent</c> for a declared authority, violating the closed recovery protocol.</exception>
    /// <exception cref="RuntimeSchedulerWorkRecoveryResolutionException">The durable provider failed while resolving the authority.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public async ValueTask<RuntimeCheckpointRecoveryResolution> ResolveAsync(
        RuntimeCheckpointRecoveryAuthority? authority,
        CancellationToken cancellationToken = default)
    {
        if (authority is null)
            return new(RuntimeCheckpointRecoveryStatus.Absent, Route: RuntimeCheckpointRecoveryRoute.SourceFree);

        if (authority.Version == 1 && StringComparer.Ordinal.Equals(authority.Kind, "runtime.scheduler-work"))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(authority.WorkflowExecutionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(authority.WorkItemId);
        }

        RuntimeCheckpointRecoveryResolution resolution;
        try
        {
            resolution = await resolver.ResolveAsync(authority, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and
                                          not RuntimeSchedulerWorkRecoveryResolutionException)
        {
            throw new RuntimeSchedulerWorkRecoveryResolutionException(
                authority.WorkflowExecutionId,
                authority.WorkItemId,
                exception);
        }

        if (resolution.Status == RuntimeCheckpointRecoveryStatus.Absent)
            throw new InvalidOperationException("A declared recovery authority cannot resolve as source-free.");
        return resolution with { Route = RuntimeCheckpointRecoveryRoute.SourceBound };
    }
}
