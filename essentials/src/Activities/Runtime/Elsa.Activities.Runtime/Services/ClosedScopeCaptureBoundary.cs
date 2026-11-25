using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Finds the closed activity execution scope a completing node's graph-variable captures write into:
/// the nearest closed boundary above the node. Looked up only for a node that has such a capture.
/// </summary>
internal static class ClosedScopeCaptureBoundary
{
    public static ValueTask<ActivityExecutionState?> FindAsync(
        IServiceProvider serviceProvider,
        string workflowExecutionId,
        string activityExecutionId,
        ExecutableNode node,
        CancellationToken cancellationToken) =>
        new RuntimeContainerScopeService(
                serviceProvider.GetRequiredService<IActivityExecutionStateStore>(),
                serviceProvider.GetRequiredService<IWorkflowExecutionStateStore>())
            .FindClosedScopeBoundaryAsync(workflowExecutionId, activityExecutionId, node, cancellationToken);
}

/// <summary>Creates a step's <see cref="ClosedScopeRowCommit"/>; null when the host stores no durable values.</summary>
internal static class ClosedScopeRowCommits
{
    public static ClosedScopeRowCommit? Create(IServiceProvider serviceProvider) =>
        serviceProvider.GetService<IDurableValueStateStore>() is { } store
            ? new ClosedScopeRowCommit(store, serviceProvider.GetRequiredService<IRuntimeDurableValueStorageDriverRegistry>())
            : null;
}
