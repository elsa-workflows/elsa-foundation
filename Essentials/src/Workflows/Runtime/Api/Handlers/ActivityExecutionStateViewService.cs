using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>
/// Serves the activity state view (spec 260 R-6): checks the run and the caller's structure grant (which also checks
/// the tenant), loads the activity's private state, and hands it to the projector registered for its type alias. A
/// null result means the run or activity is unknown or not visible to the caller; the endpoint answers 404 without
/// saying which.
/// </summary>
public sealed class ActivityExecutionStateViewService(
    IWorkflowExecutionStateStore workflowExecutionStateStore,
    IActivityExecutionStateStore activityExecutionStateStore,
    IWorkflowExecutableStore workflowExecutableStore,
    IIncidentStateStore incidentStateStore,
    IActivityInspectionContextAsync authorization,
    ActivityStateViewProjectorRegistry projectors) : IActivityExecutionStateViewService
{
    public async Task<ActivityStateView?> GetAsync(GetActivityExecutionStateView request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ActivityExecutionId);

        var workflowExecution = await workflowExecutionStateStore.FindAsync(request.WorkflowExecutionId, cancellationToken);
        if (workflowExecution is null || !await authorization.CanInspectStructureAsync(workflowExecution, cancellationToken))
            return null;

        var activity = await activityExecutionStateStore.FindAsync(request.WorkflowExecutionId, request.ActivityExecutionId, cancellationToken);
        if (activity is null)
            return null;

        var canInspectValues = await authorization.CanInspectSensitiveValuesAsync(workflowExecution, cancellationToken);
        var privateState = activity.PrivateState;
        var projector = privateState is not null ? projectors.Find(privateState.Value.Type.Alias)
            : projectors.FindWithoutState(activity.Execution.ActivityType);
        if (projector is null)
            return Unavailable(activity, privateState is null ? ActivityStateView.StateCleared : ActivityStateView.NoProjector, canInspectValues);

        var executable = await workflowExecutableStore.FindAsync(workflowExecution.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null || !executable.NodesById.TryGetValue(activity.Execution.ExecutableNodeId, out var node))
            return null;

        var incidents = await incidentStateStore.ListAsync(request.WorkflowExecutionId, cancellationToken);
        var context = new ActivityStateViewContext(workflowExecution, activity, node, incidents, canInspectValues);
        var view = privateState is null ? await projector.ProjectWithoutStateAsync(context, cancellationToken)
            : await projector.ProjectAsync(context, cancellationToken);
        if (view is null)
            return Unavailable(activity, ActivityStateView.StateCleared, canInspectValues);
        return new ActivityStateView(
            activity.Execution.WorkflowExecutionId,
            activity.Execution.ActivityExecutionId,
            activity.Execution.ExecutableNodeId,
            activity.Status.ToString(),
            Available: true,
            UnavailableReason: null,
            projector.ViewKind,
            projector.ViewVersion,
            canInspectValues,
            view);
    }

    private static ActivityStateView Unavailable(ActivityExecutionState activity, string reason, bool canInspectValues) =>
        new(
            activity.Execution.WorkflowExecutionId,
            activity.Execution.ActivityExecutionId,
            activity.Execution.ExecutableNodeId,
            activity.Status.ToString(),
            Available: false,
            reason,
            ViewKind: null,
            ViewVersion: null,
            canInspectValues,
            View: null);
}

/// <summary>The activity state view operation the runtime endpoint dispatches to.</summary>
public interface IActivityExecutionStateViewService
{
    Task<ActivityStateView?> GetAsync(GetActivityExecutionStateView request, CancellationToken cancellationToken);
}
