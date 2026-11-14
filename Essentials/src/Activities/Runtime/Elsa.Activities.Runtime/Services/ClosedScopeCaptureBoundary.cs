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
    public static async ValueTask<IReadOnlyDictionary<string, RuntimeTestCaptureTarget>> FindTestTargetsAsync(
        IServiceProvider services, string workflowExecutionId, string activityExecutionId,
        ExecutableNode node, CancellationToken cancellationToken)
    {
        var targets = new Dictionary<string, RuntimeTestCaptureTarget>(StringComparer.Ordinal);
        if (await FindAsync(services, workflowExecutionId, activityExecutionId, node, cancellationToken) is not null)
            return targets;
        var access = services.GetService<IRuntimeTestContextFrameAccess>();
        if (access is null) return targets;
        foreach (var scopeId in node.OutputCaptures.Values.Select(x => x.Metadata.GetValueOrDefault(
                     Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.CaptureTargetScopeId))
                     .Where(x => x is not null).Distinct(StringComparer.Ordinal))
            if (await access.FindCaptureTargetAsync(workflowExecutionId, activityExecutionId, scopeId!, cancellationToken) is { } target)
                targets.Add(scopeId!, target);
        return targets;
    }

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
