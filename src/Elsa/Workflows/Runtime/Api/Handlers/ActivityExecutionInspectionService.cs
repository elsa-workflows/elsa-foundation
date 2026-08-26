using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Api.Handlers;

public sealed class ActivityExecutionInspectionService(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionInspectionStore inspectionStore,
    IActivityInspectionContextAsync authorization,
    IActivityExecutionHierarchyStore? hierarchyStore = null) : IActivityExecutionInspectionService
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
        return ActivityExecutionInspectionView.From(
            projection,
            boundary,
            await authorization.CanInspectSensitiveValuesAsync(workflowExecution, cancellationToken),
            attempt,
            await authorization.CanResolveSensitiveValuePayloadsAsync(workflowExecution, cancellationToken));
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
