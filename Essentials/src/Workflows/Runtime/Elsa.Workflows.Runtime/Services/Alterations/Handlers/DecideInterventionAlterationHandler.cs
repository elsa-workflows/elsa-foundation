using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.Alterations.Handlers;

/// <summary>
/// Trusted <c>DecideIntervention/1</c> implementation (spec 260 R-5): an operator answers an intervention wait with one
/// of the decisions the requesting structural parent offered. In one alteration checkpoint it resolves the waiting
/// incident with an <c>OperatorDecision</c> outcome and enqueues a durable delivery of the decision to the requester,
/// which applies it in its own structural evaluation. The runtime carries the decision code; it never interprets it.
/// </summary>
/// <remarks>
/// The incident is the lock: only a blocking incident whose outcome is still <c>WaitForIntervention</c> from
/// <c>StructuralIntervention</c> can be decided, so the first operator action wins and a later one is refused as stale
/// (<c>InterventionNotPending</c>). The faulted activity is not touched.
/// </remarks>
public sealed class DecideInterventionAlterationHandler(
    IWorkflowExecutableStore executableStore,
    IIncidentStateStore incidentStateStore,
    IWorkflowAlterationStore alterationStore,
    TimeProvider timeProvider) : IWorkflowAlterationPreflightHandler
{
    /// <summary>The stable alteration kind.</summary>
    public const string Kind = "DecideIntervention";

    /// <summary>The schema version of the payload this handler accepts.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The longest operator reason the payload accepts.</summary>
    public const int MaxReasonLength = 500;

    private const string Slug = "decide";

    private readonly IWorkflowExecutableStore _executableStore = executableStore ?? throw new ArgumentNullException(nameof(executableStore));
    private readonly IIncidentStateStore _incidentStateStore = incidentStateStore ?? throw new ArgumentNullException(nameof(incidentStateStore));
    private readonly IWorkflowAlterationStore _alterationStore = alterationStore ?? throw new ArgumentNullException(nameof(alterationStore));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public async ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(
        WorkflowAlterationPreflightContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!TryReadPayload(context.Envelope.Payload, out var incidentId, out var decision, out var operatorReason))
            return WorkflowAlterationPreflightResult.Rejected(
                "InvalidDecideInterventionPayload",
                $"DecideIntervention requires incidentId, decision and a reason of 1 to {MaxReasonLength} characters without control characters, and accepts nothing else.");

        var workflow = context.ProjectedState.GetRequired<WorkflowExecutionState>();
        if (workflow.Status.IsTerminal())
            return WorkflowAlterationPreflightResult.Rejected("WorkflowTerminal", "A terminal workflow cannot take an intervention decision.");
        if (WorkflowAlterationCapturedConcurrencyValidator.ValidateWorkflow(context.Job, workflow) is { } workflowConflict)
            return WorkflowAlterationPreflightResult.Rejected(workflowConflict.Code, workflowConflict.Message);

        var incident = await _incidentStateStore.FindAsync(workflow.WorkflowExecutionId, incidentId, cancellationToken);
        if (incident is null)
            return WorkflowAlterationPreflightResult.Rejected("IncidentNotFound", "The requested incident does not exist in the workflow.");

        var activityView = context.ProjectedState.GetRequired<WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection>();
        var activities = activityView.Activities;
        var faulted = incident.ActivityExecutionId is { } faultedId
            ? activities.SingleOrDefault(state => StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, faultedId))
            : null;
        if (!IsWaitingForIntervention(incident) || faulted is null)
            return WorkflowAlterationPreflightResult.Rejected(
                "InterventionNotPending",
                "The incident is not waiting for an operator decision: it was already retried, decided or resolved, or its strategy owns it.");

        var outcome = incident.ResolutionOutcome!;
        if (!ReadOfferedDecisions(outcome).Contains(decision, StringComparer.Ordinal))
            return WorkflowAlterationPreflightResult.Rejected("DecisionNotOffered", "The decision is not one the waiting activity offered on this incident.");

        var requesterId = outcome.Metadata.GetValueOrDefault(RuntimeMetadataKeys.InterventionRequestedBy)
                          ?? incident.Metadata.GetValueOrDefault(RuntimeMetadataKeys.InterventionRequestedBy);
        var requester = requesterId is null
            ? null
            : activities.SingleOrDefault(state => StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, requesterId));
        if (requester is null)
            return WorkflowAlterationPreflightResult.Rejected("RequesterNotFound", "The activity that requested the intervention does not exist in the workflow.");
        if (requester.Status != ActivityExecutionStatus.Running)
            return WorkflowAlterationPreflightResult.Rejected("RequesterNotRunning", "The activity that requested the intervention is no longer running.");

        var requesterScopes = new HashSet<string>(StringComparer.Ordinal) { requester.Execution.ActivityExecutionId };
        if (requester.ExecutionScopeId is { } requesterScope)
            requesterScopes.Add(requesterScope);
        if (activityView.ActiveSchedulerClaims.Any(claim => claim.Item.ExecutionScopeId is { } scope && requesterScopes.Contains(scope)))
            return WorkflowAlterationPreflightResult.Rejected("RequesterWorkClaimed", "The activity that requested the intervention has claimed scheduler work; try again.");

        var executable = await _executableStore.FindAsync(workflow.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null || !Equals(executable.Identity, workflow.PinnedExecutable) || !executable.NodesById.ContainsKey(requester.Execution.ExecutableNodeId))
            return WorkflowAlterationPreflightResult.Rejected("PinnedExecutableUnavailable", "The requesting activity's node is unavailable in the pinned executable.");

        var plan = await _alterationStore.FindPlanAsync(context.Job.PlanId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var resolved = ResolveDecidedIncident(incident, context, plan?.SubmittedBy.Subject, operatorReason, decision, requester.Execution.ActivityExecutionId, now);
        var delivery = NewDelivery(workflow, requester, faulted, incident.IncidentId, decision, context, now);
        context.Staging.Stage(new DecisionStagedChange($"runtime.alteration.decide:{incident.IncidentId}", resolved, delivery));
        return WorkflowAlterationPreflightResult.Accepted();
    }

    private static bool IsWaitingForIntervention(IncidentState incident) =>
        incident.Status == IncidentStatus.Blocking &&
        incident.ResolutionOutcome is { } outcome &&
        StringComparer.Ordinal.Equals(outcome.ActionKind, IncidentResolutionActionKinds.WaitForIntervention) &&
        StringComparer.Ordinal.Equals(outcome.SystemSource, IncidentResolutionSystemSources.StructuralIntervention);

    // A wait that offered nothing (one from before spec 260, or a requester that offers no decisions) offers nothing here.
    private static IReadOnlyCollection<string> ReadOfferedDecisions(IncidentResolutionOutcome outcome)
    {
        if (!outcome.Metadata.TryGetValue(RuntimeMetadataKeys.InterventionDecisions, out var raw) || string.IsNullOrWhiteSpace(raw))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(raw) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IncidentState ResolveDecidedIncident(
        IncidentState incident,
        WorkflowAlterationPreflightContext context,
        string? submittedBy,
        string operatorReason,
        string decision,
        string requesterId,
        DateTimeOffset resolvedAt)
    {
        var outcomeMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AlterationOutcomeMetadataKeys.JobId] = context.Job.JobId,
            [AlterationOutcomeMetadataKeys.PlanId] = context.Job.PlanId,
            [AlterationOutcomeMetadataKeys.Reason] = operatorReason,
            [RuntimeMetadataKeys.InterventionDecision] = decision,
            [RuntimeMetadataKeys.InterventionRequestedBy] = requesterId
        };
        if (!string.IsNullOrWhiteSpace(submittedBy))
            outcomeMetadata[AlterationOutcomeMetadataKeys.SubmittedBy] = submittedBy;

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
                IncidentResolutionActionKinds.OperatorDecision,
                resolvedAt,
                strategy: null,
                systemSource: IncidentResolutionSystemSources.OperatorDecisionAlteration,
                metadata: outcomeMetadata),
            incident.FailureType,
            incident.Message,
            incident.CreatedAt,
            resolvedAt,
            incidentMetadata);
    }

    // The delivery's identities derive from the job and the incident, so a replayed alteration checkpoint enqueues the
    // same work item (spec 260 R3).
    private static RuntimePostCommitIntent NewDelivery(
        WorkflowExecutionState workflow,
        ActivityExecutionState requester,
        ActivityExecutionState faulted,
        string incidentId,
        string decision,
        WorkflowAlterationPreflightContext context,
        DateTimeOffset now)
    {
        var payload = new RuntimeInterventionDecisionCommandPayload(
            workflow.PinnedExecutable,
            requester.Execution.ExecutableNodeId,
            requester.Execution.ActivityExecutionId,
            requester.ParentActivityExecutionId,
            requester.BranchId,
            incidentId,
            decision,
            faulted.Execution.ActivityExecutionId,
            faulted.Execution.ExecutableNodeId,
            RuntimeInterventionDecisionCommandPayload.DeliverInterventionDecisionReason);
        var commandMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.ParentActivityExecutionId] = requester.Execution.ActivityExecutionId,
            ["runtime.alteration.jobId"] = context.Job.JobId,
            ["runtime.alteration.planId"] = context.Job.PlanId,
            ["runtime.alteration.kind"] = context.Envelope.Kind
        };
        var suffix = $"{context.Ordinal}:{incidentId}";
        var workItem = new RuntimeSchedulerWorkItem(
            RuntimeChainId.Derive(context.Job.JobId, $"alteration-{Slug}-work:{suffix}"),
            workflow.WorkflowExecutionId,
            RuntimeChainId.Derive(context.Job.JobId, $"alteration-{Slug}-command:{suffix}"),
            WorkflowExecutionCommandKind.DeliverInterventionDecision,
            context.Job.JobId,
            RuntimeChainId.Derive(context.Job.JobId, $"alteration-{Slug}-idempotency:{suffix}"),
            now,
            now,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: commandMetadata,
            envelopeMetadata: new Dictionary<string, string>(),
            executionScopeId: requester.ExecutionScopeId ?? requester.Provenance.ExecutionScopeId,
            attempt: requester.Attempt ?? requester.Provenance.Attempt);
        return new RuntimePostCommitIntent(
            $"alteration:{context.Job.JobId}:{Slug}:{suffix}",
            workflow.WorkflowExecutionId,
            RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            now,
            requester.Execution.ActivityExecutionId,
            workItem.IdempotencyKey,
            JsonSerializer.SerializeToElement(workItem),
            workItem.CommandMetadata,
            materializedSchedulerWorkItem: workItem);
    }

    private static bool TryReadPayload(JsonElement payload, out string incidentId, out string decision, out string reason)
    {
        incidentId = string.Empty;
        decision = string.Empty;
        reason = string.Empty;
        if (payload.ValueKind != JsonValueKind.Object ||
            payload.EnumerateObject().Any(property => property.Name is not ("incidentId" or "decision" or "reason")) ||
            !TryReadString(payload, "incidentId", out incidentId) ||
            !TryReadString(payload, "decision", out decision) ||
            !TryReadString(payload, "reason", out var rawReason))
            return false;

        var text = rawReason.Trim();
        // The reason becomes incident outcome metadata, which carries no control characters.
        if (text.Length is 0 or > MaxReasonLength || text.Any(char.IsControl))
            return false;

        reason = text;
        incidentId = incidentId.Trim();
        decision = decision.Trim();
        return incidentId.Length > 0 && decision.Length > 0;
    }

    private static bool TryReadString(JsonElement payload, string propertyName, out string value)
    {
        value = string.Empty;
        if (!payload.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString() ?? string.Empty;
        return true;
    }

    /// <summary>The decision's checkpoint effects: the resolved incident and the durable delivery to the requester.</summary>
    internal sealed record DecisionStagedChange(
        string ChangeKey,
        IncidentState Incident,
        RuntimePostCommitIntent Delivery) : IWorkflowAlterationRuntimeCheckpointStagedChange
    {
        public void Apply(WorkflowAlterationRuntimeCheckpointStateBuilder builder)
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            builder.AddIncident(new RuntimeStateChange<IncidentState>(Incident.IncidentId, RuntimeStateChangeOperation.Upsert, Incident, metadata));
            builder.AddPostCommitIntent(Delivery);
        }
    }
}
