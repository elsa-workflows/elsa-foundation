using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Finds the closed activity execution scope a completing node's graph-variable captures write into (spec 265 D-2):
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
