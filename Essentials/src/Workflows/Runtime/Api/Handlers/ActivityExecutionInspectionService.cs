using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class ActivityExecutionInspectionService(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionInspectionStore inspectionStore,
    IActivityInspectionContextAsync authorization,
    IActivityExecutionHierarchyStore? hierarchyStore = null,
    IActivityExecutionStateStore? activityExecutionStateStore = null) : IActivityExecutionInspectionService
{
    public async Task<ActivityExecutionInspectionView?> GetAsync(GetActivityExecution request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivityExecutionId);

        var workflowExecution = await workflowExecutionStateStore.FindAsync(request.WorkflowExecutionId, cancellationToken);
        if (workflowExecution is null || !await authorization.CanInspectStructureAsync(workflowExecution, cancellationToken))
            return null;

        var projection = await inspectionStore.FindAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken);
        if (projection is null)
            return null;
        var boundary = hierarchyStore is null
            ? null
            : await hierarchyStore.FindBoundaryAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken);
        var attempt = hierarchyStore is null
            ? null
            : await hierarchyStore.FindAttemptNavigationAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken);
        // Spec 1221: the write history, a refused completion and superseded input evaluations live on the execution's
        // durable state, not on the inspection projection; they are read here, as metadata and value states only.
        var state = activityExecutionStateStore is null
            ? null
            : await activityExecutionStateStore.FindAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken);
        var canInspectSensitiveValues = await authorization.CanInspectSensitiveValuesAsync(workflowExecution, cancellationToken);
        return ActivityExecutionInspectionView.From(
            projection,
            boundary,
            canInspectSensitiveValues,
            attempt,
            await authorization.CanResolveSensitiveValuePayloadsAsync(workflowExecution, cancellationToken))
            .WithVariableLifecycle(state, canInspectSensitiveValues);
    }
}

/// <summary>
/// The single-activity-execution inspection operation the runtime endpoints dispatch to. A null view means the
/// workflow execution or activity execution is missing, or the caller may not inspect it.
/// </summary>
public interface IActivityExecutionInspectionService
{
    Task<ActivityExecutionInspectionView?> GetAsync(GetActivityExecution request, CancellationToken cancellationToken);
}
