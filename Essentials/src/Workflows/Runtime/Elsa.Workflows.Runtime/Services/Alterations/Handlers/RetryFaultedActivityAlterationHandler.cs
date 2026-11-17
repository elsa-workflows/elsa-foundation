using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Services.WorkHandlers;

namespace Elsa.Workflows.Runtime.Services.Alterations.Handlers;

/// <summary>
/// Trusted <c>RetryFaultedActivity/1</c> implementation: an operator retry of one faulted activity occurrence in a
/// running run. In one alteration checkpoint it resolves the source's blocking incidents with an operator-retry
/// outcome and replaces the source with a successor built like <c>RescheduleActivity/1</c>'s, except that the
/// successor keeps the source's scheduling cause and provenance metadata, so a structural parent still recognises
/// it, and keeps the source's operation keys, so an external participant sees the same logical operation.
/// </summary>
/// <remarks>
/// A source whose parent is no longer running is refused (<c>SourceParentNotRunning</c>), so a retry never starts work
/// under a cancelled or finished structural parent. Only an incident that is waiting for intervention (outcome
/// <c>WaitForIntervention</c>, from a structural parent or from the workflow's incident strategy) can be retried. A
/// blocking incident still without an outcome is waiting for its parent or its strategy; retrying it would race that
/// decision, so it is refused.
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

    private const string RetrySlug = "retry";
    private const string RetryReason = "OperatorRetryFaultedActivity";
    private const string ReExecuteSlug = "reexecute";
    private const string ReExecuteReason = "OperatorReExecuteFaultedActivity";
    private const string AcceptSlug = "accept";
    private const string AcceptReason = "OperatorAcceptRecordedCompletion";

    /// <summary>The outcome-metadata value that names which operator action resolved the incident.</summary>
    internal const string OperatorActionRetry = "Retry";
    internal const string OperatorActionReExecute = "ReExecute";
    internal const string OperatorActionAccept = "AcceptRecordedCompletion";

    private readonly IWorkflowExecutableStore _executableStore = executableStore ?? throw new ArgumentNullException(nameof(executableStore));
    private readonly IIncidentStateStore _incidentStateStore = incidentStateStore ?? throw new ArgumentNullException(nameof(incidentStateStore));
    private readonly IWorkflowAlterationStore _alterationStore = alterationStore ?? throw new ArgumentNullException(nameof(alterationStore));
    private readonly ActivityExecutionSupersessionPlanner _supersessionPlanner = supersessionPlanner ?? throw new ArgumentNullException(nameof(supersessionPlanner));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(
        WorkflowAlterationPreflightContext context,
        CancellationToken cancellationToken = default) =>
        PreflightAsync(context, ActivitySuccessorStaging.SuccessorKind.Retry, cancellationToken);

    /// <summary>
    /// The shared preflight of the two operator actions on a faulted activity waiting for intervention (spec 1221
    /// FR-014): a retry continues the same logical invocation on its committed inputs; a re-execution
    /// (<c>ReExecuteFaultedActivity/1</c>) starts a new invocation on fresh inputs. Both resolve the source's
    /// incidents and supersede it with a successor row.
    /// </summary>
    internal async ValueTask<WorkflowAlterationPreflightResult> PreflightAsync(
        WorkflowAlterationPreflightContext context,
        ActivitySuccessorStaging.SuccessorKind kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var reExecute = kind == ActivitySuccessorStaging.SuccessorKind.ReExecute;
        var accept = kind == ActivitySuccessorStaging.SuccessorKind.Accept;
        var slug = accept ? AcceptSlug : reExecute ? ReExecuteSlug : RetrySlug;
        var reason = accept ? AcceptReason : reExecute ? ReExecuteReason : RetryReason;
        var operatorAction = accept ? OperatorActionAccept : reExecute ? OperatorActionReExecute : OperatorActionRetry;
        if (!TryReadPayload(context.Envelope.Payload, out var sourceId, out var operatorReason))
            return WorkflowAlterationPreflightResult.Rejected(
                "InvalidRetryFaultedActivityPayload",
                $"{(accept ? AcceptRecordedCompletionAlterationHandler.Kind : reExecute ? ReExecuteFaultedActivityAlterationHandler.Kind : Kind)} requires sourceActivityExecutionId and accepts only an optional reason of at most {MaxReasonLength} characters.");

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
        // A parent that is no longer running never consumes the successor's outcome, but the successor's effect would
        // still happen, for example a compensation handler under a scope a Compensate cancelled (spec 260 R9).
        if (source.ParentActivityExecutionId is { } parentActivityExecutionId &&
            activities.SingleOrDefault(state => StringComparer.Ordinal.Equals(state.Execution.ActivityExecutionId, parentActivityExecutionId)) is { } parent &&
            parent.Status != ActivityExecutionStatus.Running)
            return WorkflowAlterationPreflightResult.Rejected("SourceParentNotRunning", "The source activity's parent is not running, so a retry cannot run under it.");

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
        if (accept && RevalidateRecordedCompletion(source, sourceNode, workflow) is { } acceptRefusal)
            return acceptRefusal;

        var plan = await _alterationStore.FindPlanAsync(context.Job.PlanId, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var successorId = ActivitySuccessorStaging.SuccessorId(context, slug, source);
        var scopeId = ActivitySuccessorStaging.SuccessorScopeId(successorId);
        var successor = ActivitySuccessorStaging.NewSuccessor(
            source,
            sourceNode,
            successorId,
            scopeId,
            NewProvenance(workflow, source, scopeId, reason, keepOperationKeys: !reExecute),
            NewMetadata(source, reExecute, accept),
            now,
            kind);
        WorkflowExecutionState? acceptedWorkflow = null;
        if (accept)
            (successor, acceptedWorkflow) = ApplyRecordedCompletion(source, sourceNode, successor, workflow, now);
        var superseded = ActivitySuccessorStaging.Supersede(source, successorId, now);
        var resolvedIncidents = sourceIncidents
            .Select(incident => ResolveRetriedIncident(incident, context, plan?.SubmittedBy.Subject, operatorReason, now, operatorAction))
            .ToArray();

        var continuation = ActivitySuccessorStaging.NewContinuation(workflow, successor, context, now, slug, reason, ActivitySuccessorStaging.SchedulingMetadata(successor));
        var supersession = await _supersessionPlanner.PlanAsync(workflow.WorkflowExecutionId, source, activities, now, cancellationToken);
        if (acceptedWorkflow is not null)
        {
            // The accepted writes commit in the same checkpoint as the successor that records them.
            context.ProjectedState.Set(acceptedWorkflow);
            context.Staging.Stage(new AcceptedRecordedWritesStagedChange(context.Ordinal, superseded.Execution.ActivityExecutionId, acceptedWorkflow));
        }

        context.Staging.Stage(new ActivitySuccessorStaging.SupersessionStagedChange(
            $"runtime.alteration.{slug}:{superseded.Execution.ActivityExecutionId}",
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
    // retried child by them. For a retry the source's operation keys are written as assigned keys, so the successor
    // resolves the same keys even when the source's came from its own first-attempt identity or invocation id. A
    // re-execution is a new invocation: it keeps only the keys a structural parent assigned, and otherwise derives new
    // ones from its own identity.
    private static ActivitySchedulingProvenance NewProvenance(
        WorkflowExecutionState workflow,
        ActivityExecutionState source,
        string scopeId,
        string reason,
        bool keepOperationKeys)
    {
        var metadata = source.Provenance.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (keepOperationKeys)
        {
            metadata[RuntimeMetadataKeys.OperationKey] = ActivityOperationKeyResolver.ResolveOperationKey(source);
            if (ActivityOperationKeyResolver.ResolveTargetOperationKey(source) is { } targetOperationKey)
                metadata[RuntimeMetadataKeys.TargetOperationKey] = targetOperationKey;
        }

        return ActivitySchedulingProvenance.From(
            workflow.WorkflowExecutionId,
            source.ParentActivityExecutionId,
            source.SchedulingActivityExecutionId,
            source.BranchId,
            source.IterationId,
            source.Provenance.ExecutionPathId,
            scopeId,
            source.Provenance.SchedulingCause ?? reason,
            metadata);
    }

    private static Dictionary<string, string> NewMetadata(ActivityExecutionState source, bool reExecute, bool accept)
    {
        // The shared successor metadata already drops the source's per-invocation markers. Every successor carries
        // RetryOfActivityExecutionId: a structural parent recognises its replaced faulted child by it.
        var metadata = ActivitySuccessorStaging.SuccessorMetadata(source.Metadata, source.Execution.ActivityExecutionId);
        metadata[RuntimeMetadataKeys.RetryOfActivityExecutionId] = source.Execution.ActivityExecutionId;
        if (reExecute)
            metadata[AlterationOutcomeMetadataKeys.OperatorAction] = OperatorActionReExecute;
        if (accept)
            metadata[AlterationOutcomeMetadataKeys.OperatorAction] = OperatorActionAccept;
        return metadata;
    }

    // Spec 1221 FR-017: a recorded completion is accepted only against the state it was produced for. The body is not
    // run again and its inputs are not refreshed, so the pinned contract and every target variable must still be what
    // the completion was produced under; anything else is refused rather than applied to a changed scope.
    private static WorkflowAlterationPreflightResult? RevalidateRecordedCompletion(
        ActivityExecutionState source,
        ExecutableNode sourceNode,
        WorkflowExecutionState workflow)
    {
        if (source.RecordedCompletion is not { } recorded)
            return WorkflowAlterationPreflightResult.Rejected(
                "NoRecordedCompletion",
                "The source activity has no recorded completion: only a step whose body succeeded and whose commit a Fault lost update refused can be accepted.");
        if (sourceNode.ActivityContract is not { } contract ||
            !StringComparer.Ordinal.Equals(contract.SchemaFingerprint, recorded.Completion.ContractFingerprint))
            return WorkflowAlterationPreflightResult.Rejected(
                "RecordedCompletionContractChanged",
                "The pinned activity contract is not the one the recorded completion was produced under.");
        if (recorded.VariableWrites.Count == 0)
            return null;
        if (workflow.RootVariableFrame is not { Kind: VariableFrameKind.Root, Status: VariableFrameStatus.Active } frame)
            return WorkflowAlterationPreflightResult.Rejected("RootVariableFrameUnavailable", "The target workflow has no writable root variable frame.");
        foreach (var write in recorded.VariableWrites)
        {
            if (!frame.Values.TryGetValue(write.VariableKey, out var declared) || !SameType(declared.Type, write.Value.Type))
                return WorkflowAlterationPreflightResult.Rejected(
                    "RecordedCompletionScopeChanged",
                    $"Workflow variable '{write.VariableKey}' is no longer declared as the recorded completion wrote it.");
        }

        return null;
    }

    // The successor completes with the recorded transition: the same result and outcome selection, the attempt that
    // produced them, and every recorded write applied as an override. Each write keeps the value it overwrote, so the
    // acceptance is repairable, and the source keeps its refused write and recorded completion as the conflict.
    private static (ActivityExecutionState Successor, WorkflowExecutionState Workflow) ApplyRecordedCompletion(
        ActivityExecutionState source,
        ExecutableNode sourceNode,
        ActivityExecutionState successor,
        WorkflowExecutionState workflow,
        DateTimeOffset now)
    {
        var recorded = source.RecordedCompletion!;
        var successorId = successor.Execution.ActivityExecutionId;
        var attempt = recorded.Attempt is { } recordedAttempt
            ? new ActivityAttempt(recordedAttempt.AttemptId, successorId, recordedAttempt.Ordinal, recordedAttempt.Reason,
                recordedAttempt.StartedAt, recordedAttempt.EndedAt ?? now, recordedAttempt.TriggerDeliveryId, ActivityTransitionKind.Complete)
            : null;
        var writer = new VariableWriter(VariableWriteSource.AcceptedCompletion, sourceNode.ExecutableNodeId, successorId, attempt?.AttemptId);
        var records = new List<VariableWriteRecord>(recorded.VariableWrites.Count);
        var frame = workflow.RootVariableFrame;
        foreach (var write in recorded.VariableWrites)
        {
            var before = frame!;
            var previousValue = before.Values[write.VariableKey];
            var committed = before.Commit(write.VariableKey, write.Value, before.Revision, writeBase: null, Elsa.Primitives.Models.LostUpdateMode.Default, writer);
            var decision = committed.Decision;
            records.Add(new VariableWriteRecord(
                before.ScopeId,
                write.VariableKey,
                writer,
                VariableCommitOutcome.AcceptedOverride,
                baseVersion: null,
                decision.Current.Version,
                decision.Resulting.Version,
                committed.Frame.Revision,
                now,
                decision.ChangesValue ? decision.Current.Writer : null,
                decision.ChangesValue ? previousValue : null));
            frame = committed.Frame;
        }

        var completion = recorded.Completion;
        var metadata = successor.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.CompletionOutcomeNames] = JsonSerializer.Serialize(completion.OutcomeNames);
        var completed = RuntimeContainerScopeService.CloseOwnedFrames(successor with
        {
            Status = ActivityExecutionStatus.Completed,
            SubStatus = null,
            StartedAt = successor.StartedAt ?? now,
            CompletedAt = now,
            Metadata = metadata,
            Completion = new ActivityCompletion(successorId, attempt?.AttemptId ?? completion.AttemptId, completion.Result,
                completion.Selection, now, completion.ContractFingerprint),
            Attempts = attempt is null ? [] : [attempt],
            VariableWrites = records.Count == 0 ? null : records
        });
        return (completed, frame is null ? workflow : workflow with { RootVariableFrame = frame });
    }

    private static bool SameType(Elsa.Primitives.Models.ValueTypeDescriptor left, Elsa.Primitives.Models.ValueTypeDescriptor right) =>
        StringComparer.Ordinal.Equals(left.Alias, right.Alias) && left.CollectionKind == right.CollectionKind;

    private sealed record AcceptedRecordedWritesStagedChange(
        int Ordinal,
        string SourceActivityExecutionId,
        WorkflowExecutionState Workflow) : IWorkflowAlterationRuntimeCheckpointStagedChange
    {
        public string ChangeKey => $"runtime.alteration.accept-recorded-writes:{Ordinal}:{SourceActivityExecutionId}";

        public void Apply(WorkflowAlterationRuntimeCheckpointStateBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.SetWorkflowExecution(Workflow);
        }
    }

    private static IncidentState ResolveRetriedIncident(
        IncidentState incident,
        WorkflowAlterationPreflightContext context,
        string? submittedBy,
        string? operatorReason,
        DateTimeOffset resolvedAt,
        string operatorAction)
    {
        var outcomeMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AlterationJobIdKey] = context.Job.JobId,
            [AlterationPlanIdKey] = context.Job.PlanId,
            [AlterationOutcomeMetadataKeys.OperatorAction] = operatorAction
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

    private const string AlterationJobIdKey = AlterationOutcomeMetadataKeys.JobId;
    private const string AlterationPlanIdKey = AlterationOutcomeMetadataKeys.PlanId;
    private const string AlterationSubmittedByKey = AlterationOutcomeMetadataKeys.SubmittedBy;
    private const string AlterationReasonKey = AlterationOutcomeMetadataKeys.Reason;

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
