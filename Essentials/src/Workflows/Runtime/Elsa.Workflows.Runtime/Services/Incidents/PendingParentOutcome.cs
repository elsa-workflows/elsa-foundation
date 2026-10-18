using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Incidents;

/// <summary>Distinguishes local scheduler quiescence from an outstanding durable parent evaluation.</summary>
internal static class PendingParentOutcome
{
    public static async ValueTask<bool> ExistsAsync(
        IReadOnlyCollection<IncidentState> incidents,
        IActivityExecutionStateStore activityStore,
        CancellationToken cancellationToken)
    {
        foreach (var incident in incidents)
        {
            if (!incident.IsBlocking || incident.ResolutionOutcome is not null ||
                incident.ActivityExecutionId is null ||
                !incident.Metadata.TryGetValue(RuntimeMetadataKeys.ParentOutcomeWorkItemId, out var workItemId) ||
                string.IsNullOrWhiteSpace(workItemId))
                continue;

            var child = await activityStore.FindAsync(incident.WorkflowExecutionId, incident.ActivityExecutionId, cancellationToken);
            if (child is not { Status: ActivityExecutionStatus.Faulted, ParentActivityExecutionId: { } parentId } ||
                !child.IncidentIds.Contains(incident.IncidentId, StringComparer.Ordinal) ||
                StringComparer.Ordinal.Equals(child.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId), workItemId))
                continue;

            var parent = await activityStore.FindAsync(incident.WorkflowExecutionId, parentId, cancellationToken);
            if (parent?.Status == ActivityExecutionStatus.Running)
                return true;
        }

        return false;
    }
}
