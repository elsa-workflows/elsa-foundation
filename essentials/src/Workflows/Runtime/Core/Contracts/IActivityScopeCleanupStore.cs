using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Provider-neutral cleanup seam used by scope cancellation. Capture is side-effect free; apply is invoked by
/// the checkpoint store inside the same atomic boundary as descendant terminalization.
/// </summary>
public interface IActivityScopeCleanupStore
{
    /// <summary>Whether exact original bookmark summaries, including a known empty set, are supplied before the selected native cleanup.</summary>
    bool SupportsHistoryBookmarks => false;

    ValueTask<ActivityScopeCleanupRequest> CaptureAsync(
        string workflowExecutionId,
        string executionScopeId,
        IReadOnlySet<string> activityExecutionIds,
        CancellationToken cancellationToken = default);

    ValueTask ApplyAsync(ActivityScopeCleanupRequest request, CancellationToken cancellationToken = default);
}
