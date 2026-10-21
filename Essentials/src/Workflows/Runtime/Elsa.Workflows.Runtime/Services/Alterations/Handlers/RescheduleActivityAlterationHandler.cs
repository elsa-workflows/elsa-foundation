using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations.Handlers;

/// <summary>
/// Trusted <c>RescheduleActivity/1</c> implementation. It creates a distinct successor and records immutable
/// supersession lineage; it never retries, recovers, or resolves the source's incident.
/// </summary>
public sealed class RescheduleActivityAlterationHandler(
    IWorkflowExecutableStore executableStore,
    IIncidentStateStore incidentStateStore,
    ActivityExecutionSupersessionPlanner supersessionPlanner,
    TimeProvider timeProvider) : IWorkflowAlterationPreflightHandler
{
    private readonly IWorkflowExecutableStore _executableStore = executableStore ?? throw new ArgumentNullException(nameof(executableStore));
    private readonly IIncidentStateStore _incidentStateStore = incidentStateStore ?? throw new ArgumentNullException(nameof(incidentStateStore));
    private readonly ActivityExecutionSupersessionPlanner _supersessionPlanner = supersessionPlanner ?? throw new ArgumentNullException(nameof(supersessionPlanner));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    private const string Slug = "reschedule";
    private const string Reason = "OperatorRescheduleActivity";

    public async ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(
        WorkflowAlterationPreflightContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryReadPayload(context.Envelope.Payload, out var sourceId))
            return WorkflowAlterationPreflightResult.Rejected("InvalidRescheduleActivityPayload", "RescheduleActivity requires sourceActivityExecutionId.");

        var workflow = context.ProjectedState.GetRequired<WorkflowExecutionState>();
        if (workflow.Status.IsTerminal())
            return WorkflowAlterationPreflightResult.Rejected("WorkflowTerminal", "A terminal workflow cannot reschedule an activity.");
        if (WorkflowAlterationCapturedConcurrencyValidator.ValidateWorkflow(context.Job, workflow) is { } workflowConflict)
            return WorkflowAlterationPreflightResult.Rejected(workflowConflict.Code, workflowConflict.Message);
        var activityView = context.ProjectedState.GetRequired<WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection>();
        var activities = activityView.Activities;
        var source = activities.SingleOrDefault(state => StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, sourceId));
        if (source is null)
            return WorkflowAlterationPreflightResult.Rejected("SourceActivityNotFound", "The requested source activity does not exist.");
        if (WorkflowAlterationCapturedConcurrencyValidator.ValidateActivity(context.Job, source, "source") is { } sourceConflict)
            return WorkflowAlterationPreflightResult.Rejected(sourceConflict.Code, sourceConflict.Message);
        var supersededSubtree = ActivitySuccessorStaging.SupersededSubtree(source, activities);
        var supersededSubtreeScopes = ActivitySuccessorStaging.SupersededSubtreeScopes(supersededSubtree);
        if (activityView.ActiveSchedulerClaims.Any(claim => claim.Item.ExecutionScopeId is { } scope && supersededSubtreeScopes.Contains(scope)))
            return WorkflowAlterationPreflightResult.Rejected("SourceWorkClaimed", "The source activity has claimed scheduler work and cannot be superseded.");
        if (source.Status is not (ActivityExecutionStatus.Scheduled or ActivityExecutionStatus.Waiting or ActivityExecutionStatus.Suspended or ActivityExecutionStatus.Faulted))
            return WorkflowAlterationPreflightResult.Rejected("SourceActivityNotReschedulable", "Only scheduled, waiting, suspended, or faulted activities can be rescheduled.");
        var supersededSubtreeIds = supersededSubtree
            .Select(activity => activity.Execution.ActivityExecutionId)
            .ToHashSet(StringComparer.Ordinal);
        var blockingIncidents = await _incidentStateStore.ListBlockingAsync(workflow.WorkflowExecutionId, cancellationToken);
        if (blockingIncidents.Any(incident => incident.ActivityExecutionId is { } activityExecutionId && supersededSubtreeIds.Contains(activityExecutionId)))
            return WorkflowAlterationPreflightResult.Rejected("BlockingIncident", "The source activity has incident evidence and must be resolved through recovery before rescheduling.");

        var executable = await _executableStore.FindAsync(workflow.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null || !Equals(executable.Identity, workflow.PinnedExecutable) || !executable.NodesById.TryGetValue(source.Execution.ExecutableNodeId, out var sourceNode))
            return WorkflowAlterationPreflightResult.Rejected("PinnedExecutableUnavailable", "The source activity node is unavailable in the pinned executable.");
        if (ParentThatEvaluatedSourceRefusal(source, activities, executable) is { } parentRefusal)
            return parentRefusal;

        var successorId = ActivitySuccessorStaging.SuccessorId(context, Slug, source);
        var scopeId = ActivitySuccessorStaging.SuccessorScopeId(successorId);
        var now = _timeProvider.GetUtcNow();
        // The successor keeps the scheduling metadata its parent gave the source, so the parent still routes its outcome.
        var provenance = ActivitySchedulingProvenance.From(
            workflow.WorkflowExecutionId,
            source.ParentActivityExecutionId,
            source.SchedulingActivityExecutionId,
            source.BranchId,
            source.IterationId,
            source.Provenance.ExecutionPathId,
            scopeId,
            Reason,
            source.Provenance.Metadata);
        var successor = ActivitySuccessorStaging.NewSuccessor(
            source,
            sourceNode,
            successorId,
            scopeId,
            provenance,
            ActivitySuccessorStaging.SuccessorMetadata(source.Metadata, source.Execution.ActivityExecutionId),
            now);
        var superseded = ActivitySuccessorStaging.Supersede(source, successorId, now);

        var continuation = ActivitySuccessorStaging.NewContinuation(workflow, successor, context, now, Slug, Reason, ActivitySuccessorStaging.SchedulingMetadata(successor));
        var supersession = await _supersessionPlanner.PlanAsync(workflow.WorkflowExecutionId, source, activities, now, cancellationToken);
        context.Staging.Stage(new ActivitySuccessorStaging.SupersessionStagedChange(
            $"runtime.alteration.reschedule:{superseded.Execution.ActivityExecutionId}",
            superseded,
            successor,
            supersession.CancelledDescendants,
            supersession.Cleanup,
            ActivityExecutionInspectionProjection.FromState(superseded, $"checkpoint:alteration:{context.Job.JobId}", now),
            ActivityExecutionInspectionProjection.FromState(successor, $"checkpoint:alteration:{context.Job.JobId}", now),
            continuation,
            Incidents: []));
        return WorkflowAlterationPreflightResult.Accepted();
    }

    // A parent that already evaluated the source's fault may have stopped tracking the child (a BPMN error boundary or
    // late-fault guard drops it), and the runtime cannot tell from outside whether it still expects an outcome. So the
    // successor's outcome may only go to a running parent that opted in (IRuntimeAcceptsRescheduledFaultedChild, #3135);
    // otherwise the reschedule is refused rather than leaving the successor to run with no one to receive its outcome.
    private static WorkflowAlterationPreflightResult? ParentThatEvaluatedSourceRefusal(
        ActivityExecutionState source,
        IReadOnlyCollection<ActivityExecutionState> activities,
        WorkflowExecutable executable)
    {
        // An ancestor that received this fault past a composite that does not handle it (spec 258 R-3) already
        // decided on it, for example a compensation scope that faulted or abandoned the step without cancelling it.
        if (source.Metadata.TryGetValue(RuntimeMetadataKeys.DescendantFaultProcessedWorkItemId, out var descendantWorkItemId) &&
            !string.IsNullOrWhiteSpace(descendantWorkItemId))
            return WorkflowAlterationPreflightResult.Rejected(
                "SourceFaultHandledByAncestor",
                "An ancestor of the source activity already received and decided on its fault.");
        if (!source.Metadata.TryGetValue(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId, out var processedWorkItemId) ||
            string.IsNullOrWhiteSpace(processedWorkItemId))
            return null;

        var parent = source.ParentActivityExecutionId is { } parentId
            ? activities.SingleOrDefault(state => StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, parentId))
            : null;
        if (parent is not { Status: ActivityExecutionStatus.Running })
            return WorkflowAlterationPreflightResult.Rejected(
                "SourceParentNotRunning",
                "The source activity's parent already evaluated its outcome and is no longer running, so nothing would receive the successor's outcome.");
        if (!executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var parentNode) ||
            !StringComparer.OrdinalIgnoreCase.Equals(parentNode.Metadata.GetValueOrDefault(RuntimeMetadataKeys.AcceptsRescheduledFaultedChild), "true"))
            return WorkflowAlterationPreflightResult.Rejected(
                "ParentDoesNotAcceptRescheduledChild",
                "The source activity's parent already evaluated its fault and does not accept a rescheduled child's outcome.");
        return null;
    }

    private static bool TryReadPayload(JsonElement payload, out string sourceId)
    {
        sourceId = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("sourceActivityExecutionId", out var source) || source.ValueKind != JsonValueKind.String ||
            payload.EnumerateObject().Any(property => property.Name != "sourceActivityExecutionId"))
            return false;
        sourceId = source.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(sourceId);
    }
}
