using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;

namespace Elsa.Workflows.Runtime.Services.Alterations.Handlers;

/// <summary>
/// Trusted <c>RetryFaultedActivity/1</c> implementation: an operator retry of one faulted activity occurrence in a
/// running run. In one alteration checkpoint it resolves the source's blocking incidents with an operator-retry
/// outcome and replaces the source with a successor built like <c>RescheduleActivity/1</c>'s, except that the
/// successor keeps the source's scheduling cause and provenance metadata, so a structural parent still recognises
/// it, and keeps the source's operation keys, so an external participant sees the same logical operation.
/// </summary>
/// <remarks>
/// Only an incident that is waiting for intervention (outcome <c>WaitForIntervention</c>, from a structural parent or
/// from the workflow's incident strategy) can be retried. A blocking incident still without an outcome is waiting for
/// its parent or its strategy; retrying it would race that decision, so it is refused.
/// </remarks>
public sealed class RetryFaultedActivityAlterationHandler(
    IWorkflowExecutableStore executableStore,
    IIncidentStateStore incidentStateStore,
    IWorkflowAlterationStore alterationStore,
    ActivityExecutionSupersessionPlanner supersessionPlanner,
    TimeProvider timeProvider) : IWorkflowAlterationPreflightHandler
{
    /// <summary>The stable alteration kind.</summary>
    public const string Kind = "RetryFaultedActivity";

    /// <summary>The schema version of the payload this handler accepts.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The longest operator reason the payload accepts.</summary>
    public const int MaxReasonLength = 500;

    private const string Slug = "retry";
    private const string Reason = "OperatorRetryFaultedActivity";

    private readonly IWorkflowExecutableStore _executableStore = executableStore ?? throw new ArgumentNullException(nameof(executableStore));
    private readonly IIncidentStateStore _incidentStateStore = incidentStateStore ?? throw new ArgumentNullException(nameof(incidentStateStore));
    private readonly IWorkflowAlterationStore _alterationStore = alterationStore ?? throw new ArgumentNullException(nameof(alterationStore));
    private readonly ActivityExecutionSupersessionPlanner _supersessionPlanner = supersessionPlanner ?? throw new ArgumentNullException(nameof(supersessionPlanner));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(
        WorkflowAlterationPreflightContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryReadPayload(context.Envelope.Payload, out var sourceId, out var operatorReason))
            return WorkflowAlterationPreflightResult.Rejected(
                "InvalidRetryFaultedActivityPayload",
                $"RetryFaultedActivity requires sourceActivityExecutionId and accepts only an optional reason of at most {MaxReasonLength} characters.");

        var workflow = context.ProjectedState.GetRequired<WorkflowExecutionState>();
        if (workflow.Status.IsTerminal())
            return WorkflowAlterationPreflightResult.Rejected("WorkflowTerminal", "A terminal workflow cannot retry an activity.");
        if (WorkflowAlterationCapturedConcurrencyValidator.ValidateWorkflow(context.Job, workflow) is { } workflowConflict)
            return WorkflowAlterationPreflightResult.Rejected(workflowConflict.Code, workflowConflict.Message);
        var activityView = context.ProjectedState.GetRequired<WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection>();
        var activities = activityView.Activities;
        var source = activities.SingleOrDefault(state => StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, sourceId));
        if (source is null)
            return WorkflowAlterationPreflightResult.Rejected("SourceActivityNotFound", "The requested source activity does not exist.");
        if (WorkflowAlterationCapturedConcurrencyValidator.ValidateActivity(context.Job, source, "source") is { } sourceConflict)
            return WorkflowAlterationPreflightResult.Rejected(sourceConflict.Code, sourceConflict.Message);
        if (source.Status != ActivityExecutionStatus.Faulted)
            return WorkflowAlterationPreflightResult.Rejected("SourceActivityNotFaulted", "Only a faulted activity can be retried.");

        var supersededSubtree = ActivitySuccessorStaging.SupersededSubtree(source, activities);
        var supersededSubtreeIds = supersededSubtree
            .Select(activity => activity.Execution.ActivityExecutionId)
            .ToHashSet(StringComparer.Ordinal);
        var subtreeIncidents = (await _incidentStateStore.ListBlockingAsync(workflow.WorkflowExecutionId, cancellationToken))
            .Where(incident => incident.ActivityExecutionId is { } activityExecutionId && supersededSubtreeIds.Contains(activityExecutionId))
            .OrderBy(incident => incident.IncidentId, StringComparer.Ordinal)
            .ToArray();
        var sourceIncidents = subtreeIncidents
            .Where(incident => StringComparer.Ordinal.Equals(incident.ActivityExecutionId, source.Execution.ActivityExecutionId))
            .ToArray();
        if (sourceIncidents.Length == 0)
            return WorkflowAlterationPreflightResult.Rejected("NoBlockingIncident", "The source activity has no blocking incident to retry.");
        if (sourceIncidents.Length != subtreeIncidents.Length)
            return WorkflowAlterationPreflightResult.Rejected("BlockingIncidentOutsideSource", "A blocking incident belongs to a descendant of the source activity, not to the source.");
        if (sourceIncidents.Any(incident => !StringComparer.Ordinal.Equals(incident.ResolutionOutcome?.ActionKind, IncidentResolutionActionKinds.WaitForIntervention)))
            return WorkflowAlterationPreflightResult.Rejected("IncidentNotAwaitingIntervention", "The source activity's blocking incident is not waiting for intervention yet.");

        var supersededSubtreeScopes = ActivitySuccessorStaging.SupersededSubtreeScopes(supersededSubtree);
        if (activityView.ActiveSchedulerClaims.Any(claim => claim.Item.ExecutionScopeId is { } scope && supersededSubtreeScopes.Contains(scope)))
            return WorkflowAlterationPreflightResult.Rejected("SourceWorkClaimed", "The source activity has claimed scheduler work and cannot be superseded.");

        var executable = await _executableStore.FindAsync(workflow.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null || !Equals(executable.Identity, workflow.PinnedExecutable) || !executable.NodesById.TryGetValue(source.Execution.ExecutableNodeId, out var sourceNode))
            return WorkflowAlterationPreflightResult.Rejected("PinnedExecutableUnavailable", "The source activity node is unavailable in the pinned executable.");

        var plan = await _alterationStore.FindPlanAsync(context.Job.PlanId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var successorId = ActivitySuccessorStaging.SuccessorId(context, Slug, source);
        var scopeId = ActivitySuccessorStaging.SuccessorScopeId(successorId);
        var successor = ActivitySuccessorStaging.NewSuccessor(
            source,
            sourceNode,
            successorId,
            scopeId,
            NewProvenance(workflow, source, scopeId),
            NewMetadata(source),
            now);
        var superseded = ActivitySuccessorStaging.Supersede(source, successorId, now);
        var resolvedIncidents = sourceIncidents
            .Select(incident => ResolveRetriedIncident(incident, context, plan?.SubmittedBy.Subject, operatorReason, now))
            .ToArray();

        var continuation = ActivitySuccessorStaging.NewContinuation(workflow, successor, context, now, Slug, Reason);
        var supersession = await _supersessionPlanner.PlanAsync(workflow.WorkflowExecutionId, source, activities, now, cancellationToken);
        context.Staging.Stage(new ActivitySuccessorStaging.SupersessionStagedChange(
            $"runtime.alteration.retry:{superseded.Execution.ActivityExecutionId}",
            superseded,
            successor,
            supersession.CancelledDescendants,
            supersession.Cleanup,
            ActivityExecutionInspectionProjection.FromState(superseded, $"checkpoint:alteration:{context.Job.JobId}", now),
            ActivityExecutionInspectionProjection.FromState(successor, $"checkpoint:alteration:{context.Job.JobId}", now),
            continuation,
            resolvedIncidents));
        return WorkflowAlterationPreflightResult.Accepted();
    }

    // The successor keeps the source's scheduling cause and provenance metadata: a structural parent recognises its
    // retried child by them. The source's operation keys are written as assigned keys, so the successor resolves the
    // same keys even when the source's came from its own first-attempt identity or invocation id.
    private static ActivitySchedulingProvenance NewProvenance(
        WorkflowExecutionState workflow,
        ActivityExecutionState source,
        string scopeId)
    {
        var metadata = source.Provenance.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.OperationKey] = ActivityOperationKeyResolver.ResolveOperationKey(source);
        if (ActivityOperationKeyResolver.ResolveTargetOperationKey(source) is { } targetOperationKey)
            metadata[RuntimeMetadataKeys.TargetOperationKey] = targetOperationKey;
        return ActivitySchedulingProvenance.From(
            workflow.WorkflowExecutionId,
            source.ParentActivityExecutionId,
            source.SchedulingActivityExecutionId,
            source.BranchId,
            source.IterationId,
            source.Provenance.ExecutionPathId,
            scopeId,
            source.Provenance.SchedulingCause ?? Reason,
            metadata);
    }

    // The successor is a fresh invocation of the same occurrence. It keeps the source's metadata except the markers
    // the runtime keeps per invocation: carried over, they would tell the parent-completion handler the successor's
    // outcome was already processed, point a background coordinator at the source's work record, and describe a
    // fault the successor never had.
    private static Dictionary<string, string> NewMetadata(ActivityExecutionState source)
    {
        var metadata = ActivitySuccessorStaging.SuccessorMetadata(source.Metadata, source.Execution.ActivityExecutionId);
        foreach (var key in PerInvocationMetadataKeys)
            metadata.Remove(key);
        metadata[RuntimeMetadataKeys.RetryOfActivityExecutionId] = source.Execution.ActivityExecutionId;
        return metadata;
    }

    private static readonly string[] PerInvocationMetadataKeys =
    [
        RuntimeMetadataKeys.ActivityAttemptActivationClaim,
        RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId,
        RuntimeMetadataKeys.ActivityActivationCompletedWorkItemId,
        RuntimeMetadataKeys.ActivityAttemptOrdinalHighWatermark,
        RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId,
        RuntimeMetadataKeys.ParentOutcomeWorkItemId,
        RuntimeMetadataKeys.IncidentId,
        RuntimeMetadataKeys.FaultSubStatus,
        RuntimeMetadataKeys.FaultType,
        RuntimeMetadataKeys.FaultMessage,
        RuntimeMetadataKeys.FaultStackTrace,
        RuntimeMetadataKeys.FaultInnerType,
        RuntimeMetadataKeys.FaultInnerMessage,
        RuntimeMetadataKeys.CausalIncidentId,
        RuntimeMetadataKeys.CausalActivityExecutionId,
        RuntimeMetadataKeys.CausalExecutableNodeId,
        RuntimeMetadataKeys.CausationKind,
        // The background coordinator's admitted-work marker (BackgroundActivityEligibility.WorkIdMetadataKey).
        "runtime.backgroundActivityWorkId"
    ];

    private static IncidentState ResolveRetriedIncident(
        IncidentState incident,
        WorkflowAlterationPreflightContext context,
        string? submittedBy,
        string? operatorReason,
        DateTimeOffset resolvedAt)
    {
        var outcomeMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AlterationJobIdKey] = context.Job.JobId,
            [AlterationPlanIdKey] = context.Job.PlanId
        };
        if (!string.IsNullOrWhiteSpace(submittedBy))
            outcomeMetadata[AlterationSubmittedByKey] = submittedBy;
        if (operatorReason is not null)
            outcomeMetadata[AlterationReasonKey] = operatorReason;

        // The wait-for-intervention outcome this resolution replaces stays visible on the incident.
        var incidentMetadata = incident.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        incidentMetadata[RuntimeMetadataKeys.PriorResolutionActionKind] = incident.ResolutionOutcome!.ActionKind;
        if (incident.ResolutionOutcome.SystemSource is { } priorSystemSource)
            incidentMetadata[RuntimeMetadataKeys.PriorResolutionSystemSource] = priorSystemSource;

        return new IncidentState(
            incident.IncidentId,
            incident.WorkflowExecutionId,
            incident.ActivityExecutionId,
            incident.ExecutableNodeId,
            incident.Severity,
            IncidentStatus.Resolved,
            new IncidentResolutionOutcome(
                IncidentResolutionActionKinds.OperatorRetry,
                resolvedAt,
                strategy: null,
                systemSource: IncidentResolutionSystemSources.OperatorRetryAlteration,
                metadata: outcomeMetadata),
            incident.FailureType,
            incident.Message,
            incident.CreatedAt,
            resolvedAt,
            incidentMetadata);
    }

    private const string AlterationJobIdKey = "runtime.alteration.jobId";
    private const string AlterationPlanIdKey = "runtime.alteration.planId";
    private const string AlterationSubmittedByKey = "runtime.alteration.submittedBy";
    private const string AlterationReasonKey = "runtime.alteration.reason";

    private static bool TryReadPayload(JsonElement payload, out string sourceId, out string? reason)
    {
        sourceId = string.Empty;
        reason = null;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("sourceActivityExecutionId", out var source) || source.ValueKind != JsonValueKind.String ||
            payload.EnumerateObject().Any(property => property.Name is not ("sourceActivityExecutionId" or "reason")))
            return false;

        if (payload.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind != JsonValueKind.Null)
        {
            if (reasonElement.ValueKind != JsonValueKind.String)
                return false;
            var text = reasonElement.GetString()!.Trim();
            // The reason becomes incident outcome metadata, which carries no control characters.
            if (text.Length > MaxReasonLength || text.Any(char.IsControl))
                return false;
            reason = text.Length == 0 ? null : text;
        }

        sourceId = source.GetString()?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(sourceId);
    }
}
