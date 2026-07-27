using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Produces whole-workflow cancellation state changes without committing them. This keeps scheduler and alteration
/// cancellation behavior identical while leaving their checkpoint identities and delivery responsibilities separate.
/// </summary>
public sealed class WorkflowCancellationPlanner(
    IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
    TimeProvider timeProvider,
    IActivityScopeCleanupStore? activityScopeCleanupStore = null)
{
    private readonly IRuntimeActivityExecutionInspectionAccumulator _inspectionAccumulator = inspectionAccumulator ?? throw new ArgumentNullException(nameof(inspectionAccumulator));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    private readonly IActivityScopeCleanupStore? _activityScopeCleanupStore = activityScopeCleanupStore;

    public async ValueTask<WorkflowCancellationPlan?> PlanAsync(
        WorkflowExecutionState workflow,
        IReadOnlyCollection<ActivityExecutionState> activities,
        string checkpointId,
        IReadOnlyDictionary<string, string> stateChangeMetadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentException.ThrowIfNullOrWhiteSpace(checkpointId);
        ArgumentNullException.ThrowIfNull(stateChangeMetadata);

        if (workflow.Status.IsTerminal())
            return null;

        var occurredAt = _timeProvider.GetUtcNow();
        var cancelledWorkflow = RuntimeContainerScopeService.CloseRootFrame(workflow with
        {
            Status = WorkflowExecutionStatus.Cancelled,
            UpdatedAt = occurredAt,
            CompletedAt = occurredAt
        });
        var cancelledActivities = activities
            .Where(IsCancellable)
            .Select(state => RuntimeContainerScopeService.CloseOwnedFrames(state with
            {
                Status = ActivityExecutionStatus.Cancelled,
                CompletedAt = occurredAt
            }))
            .OrderBy(state => state.ExecutionSequence)
            .ThenBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .ToArray();
        var activityChanges = cancelledActivities
            .Select(state => new RuntimeStateChange<ActivityExecutionState>(
                state.Execution.ActivityExecutionId,
                RuntimeStateChangeOperation.Upsert,
                state,
                stateChangeMetadata))
            .ToArray();
        var inspectionChanges = new List<RuntimeStateChange<ActivityExecutionInspectionProjection>>(cancelledActivities.Length);
        foreach (var state in cancelledActivities)
        {
            var inspection = await _inspectionAccumulator.BuildProjectionAsync(
                state,
                checkpointId,
                occurredAt,
                metadata: stateChangeMetadata,
                cancellationToken: cancellationToken);
            inspectionChanges.Add(new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                state.Execution.ActivityExecutionId,
                RuntimeStateChangeOperation.Upsert,
                inspection,
                stateChangeMetadata));
        }

        // A cancellation checkpoint may clean only scopes whose live execution it terminalizes.  Including an
        // already-terminal sibling would let a late whole-workflow cancellation reclaim state which belongs to a
        // completed/faulted history record rather than to the live cancellation transition.
        var cleanupScopeIds = cancelledActivities
            .SelectMany(state => new[] { state.Execution.ActivityExecutionId, state.ExecutionScopeId })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Append(workflow.WorkflowExecutionId)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlyCollection<ActivityScopeCleanupRequest> cleanupRequests = _activityScopeCleanupStore is null
            ? Array.Empty<ActivityScopeCleanupRequest>()
            : [await _activityScopeCleanupStore.CaptureAsync(
                workflow.WorkflowExecutionId,
                workflow.WorkflowExecutionId,
                cleanupScopeIds,
                cancellationToken)];

        return new WorkflowCancellationPlan(cancelledWorkflow, activityChanges, inspectionChanges, cleanupRequests);
    }

    private static bool IsCancellable(ActivityExecutionState state) =>
        state.Status is not ActivityExecutionStatus.Completed
            and not ActivityExecutionStatus.Faulted
            and not ActivityExecutionStatus.Cancelled
            and not ActivityExecutionStatus.Recovered;
}

/// <summary>Checkpoint-ready cancellation changes with no persistence behavior.</summary>
public sealed record WorkflowCancellationPlan(
    WorkflowExecutionState WorkflowExecution,
    IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> ActivityExecutions,
    IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> ActivityExecutionInspections,
    IReadOnlyCollection<ActivityScopeCleanupRequest> CleanupRequests);
