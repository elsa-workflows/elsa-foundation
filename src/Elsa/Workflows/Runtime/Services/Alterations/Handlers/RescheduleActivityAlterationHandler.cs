using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Core.Services;

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
        var supersededSubtree = SupersededSubtree(source, activities);
        var supersededSubtreeScopes = SupersededSubtreeScopes(supersededSubtree);
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

        var successorId = RuntimeChainId.Derive(context.Job.JobId, $"alteration-reschedule:{context.Ordinal}:{source.Execution.ActivityExecutionId}");
        var scopeId = RuntimeChainId.Derive(successorId, "scope");
        var now = _timeProvider.GetUtcNow();
        // StartActivity executes intrinsics only from Scheduled. A preserved input snapshot therefore means a direct
        // Running → Start continuation only for CLR activities; intrinsic successors must re-enter through Schedule.
        var successorStatus = sourceNode.IntrinsicKind is null && source.InputSnapshot is not null
            ? ActivityExecutionStatus.Running
            : ActivityExecutionStatus.Scheduled;
        var provenance = ActivitySchedulingProvenance.From(
            workflow.WorkflowExecutionId,
            source.ParentActivityExecutionId,
            source.SchedulingActivityExecutionId,
            source.BranchId,
            source.IterationId,
            source.Provenance.ExecutionPathId,
            scopeId,
            "OperatorRescheduleActivity");
        var successor = source with
        {
            Execution = source.Execution with { ActivityExecutionId = successorId },
            Status = successorStatus,
            SubStatus = null,
            ExecutionSequence = StableSequence(successorId),
            ScheduledAt = now,
            StartedAt = successorStatus == ActivityExecutionStatus.Running ? now : null,
            CompletedAt = null,
            Provenance = provenance,
            ExecutionScopeId = scopeId,
            BookmarkIds = [],
            IncidentIds = [],
            FaultCount = 0,
            AggregateFaultCount = 0,
            Metadata = NewMetadata(source.Metadata, source.Execution.ActivityExecutionId),
            Attempts = [],
            PrivateState = null,
            Completion = null,
            Fault = null,
            TriggerRegistrations = [],
            TriggerDeliveries = [],
            Attempt = null,
            SupersededByActivityExecutionId = null,
            SupersededAt = null
        };
        var superseded = source with
        {
            Status = ActivityExecutionStatus.Superseded,
            CompletedAt = now,
            SupersededByActivityExecutionId = successorId,
            SupersededAt = now
        };
        superseded.EnsureSupersessionCompatible();

        var continuation = NewContinuation(workflow, successor, context, now);
        var supersession = await _supersessionPlanner.PlanAsync(workflow.WorkflowExecutionId, source, activities, now, cancellationToken);
        context.Staging.Stage(new RescheduleActivityStagedChange(
            superseded,
            successor,
            supersession.CancelledDescendants,
            supersession.Cleanup,
            ActivityExecutionInspectionProjection.FromState(superseded, $"checkpoint:alteration:{context.Job.JobId}", now),
            ActivityExecutionInspectionProjection.FromState(successor, $"checkpoint:alteration:{context.Job.JobId}", now),
            continuation));
        return WorkflowAlterationPreflightResult.Accepted();
    }

    private static RuntimePostCommitIntent NewContinuation(
        WorkflowExecutionState workflow,
        ActivityExecutionState successor,
        WorkflowAlterationPreflightContext context,
        DateTimeOffset now)
    {
        var commandKind = successor.Status == ActivityExecutionStatus.Running
            ? WorkflowExecutionCommandKind.StartActivity
            : WorkflowExecutionCommandKind.ScheduleActivity;
        object payload = successor.Status == ActivityExecutionStatus.Running
            ? new RuntimeStartActivityCommandPayload(workflow.PinnedExecutable, successor.Execution.ExecutableNodeId, successor.Execution.ActivityExecutionId, "OperatorRescheduleActivity")
            : new RuntimeScheduleActivityCommandPayload(workflow.PinnedExecutable, successor.Execution.ExecutableNodeId, successor.Execution.ActivityExecutionId, "OperatorRescheduleActivity", successor.SchedulingActivityExecutionId, successor.ParentActivityExecutionId, successor.Provenance);
        var workItem = new RuntimeSchedulerWorkItem(
            RuntimeChainId.Derive(context.Job.JobId, $"alteration-reschedule-work:{context.Ordinal}:{successor.Execution.ActivityExecutionId}"),
            workflow.WorkflowExecutionId,
            RuntimeChainId.Derive(context.Job.JobId, $"alteration-reschedule-command:{context.Ordinal}:{successor.Execution.ActivityExecutionId}"),
            commandKind,
            context.Job.JobId,
            RuntimeChainId.Derive(context.Job.JobId, $"alteration-reschedule-idempotency:{context.Ordinal}:{successor.Execution.ActivityExecutionId}"),
            now,
            now,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtime.alteration.jobId"] = context.Job.JobId,
                ["runtime.alteration.planId"] = context.Job.PlanId,
                ["runtime.alteration.kind"] = context.Envelope.Kind
            },
            envelopeMetadata: new Dictionary<string, string>(),
            executionScopeId: successor.ExecutionScopeId);
        return new RuntimePostCommitIntent(
            $"alteration:{context.Job.JobId}:reschedule:{context.Ordinal}:{successor.Execution.ActivityExecutionId}",
            workflow.WorkflowExecutionId,
            RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            now,
            successor.Execution.ActivityExecutionId,
            workItem.IdempotencyKey,
            JsonSerializer.SerializeToElement(workItem),
            workItem.CommandMetadata,
            materializedSchedulerWorkItem: workItem);
    }

    private static IReadOnlyDictionary<string, string> NewMetadata(IReadOnlyDictionary<string, string> source, string sourceId)
    {
        var metadata = source.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["runtime.supersedesActivityExecutionId"] = sourceId;
        return metadata;
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

    private static long StableSequence(string value) => BitConverter.ToInt64(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)), 0) & long.MaxValue;

    private static IReadOnlyCollection<ActivityExecutionState> SupersededSubtree(
        ActivityExecutionState source,
        IReadOnlyCollection<ActivityExecutionState> activities)
    {
        var children = activities
            .Where(activity => !string.IsNullOrWhiteSpace(activity.ParentActivityExecutionId))
            .GroupBy(activity => activity.ParentActivityExecutionId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group, StringComparer.Ordinal);
        var result = new List<ActivityExecutionState>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<ActivityExecutionState>([source]);
        while (queue.TryDequeue(out var activity))
        {
            if (!visited.Add(activity.Execution.ActivityExecutionId))
                continue;
            result.Add(activity);
            if (children.TryGetValue(activity.Execution.ActivityExecutionId, out var descendants))
                foreach (var descendant in descendants)
                    queue.Enqueue(descendant);
        }

        return result;
    }

    private static IReadOnlySet<string> SupersededSubtreeScopes(IReadOnlyCollection<ActivityExecutionState> activities) =>
        activities
            .SelectMany(activity => new[] { activity.Execution.ActivityExecutionId, activity.ExecutionScopeId })
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope!)
            .ToHashSet(StringComparer.Ordinal);

    private sealed record RescheduleActivityStagedChange(
        ActivityExecutionState Superseded,
        ActivityExecutionState Successor,
        IReadOnlyCollection<ActivityExecutionState> CancelledDescendants,
        ActivityScopeCleanupRequest Cleanup,
        ActivityExecutionInspectionProjection SupersededInspection,
        ActivityExecutionInspectionProjection SuccessorInspection,
        RuntimePostCommitIntent Continuation) : IWorkflowAlterationRuntimeCheckpointStagedChange
    {
        public string ChangeKey => $"runtime.alteration.reschedule:{Superseded.Execution.ActivityExecutionId}";

        public void Apply(WorkflowAlterationRuntimeCheckpointStateBuilder builder)
        {
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            builder.AddActivityExecution(new RuntimeStateChange<ActivityExecutionState>(Superseded.Execution.ActivityExecutionId, RuntimeStateChangeOperation.Upsert, Superseded, metadata));
            builder.AddActivityExecution(new RuntimeStateChange<ActivityExecutionState>(Successor.Execution.ActivityExecutionId, RuntimeStateChangeOperation.Upsert, Successor, metadata));
            foreach (var descendant in CancelledDescendants)
            {
                builder.AddActivityExecution(new RuntimeStateChange<ActivityExecutionState>(descendant.Execution.ActivityExecutionId, RuntimeStateChangeOperation.Upsert, descendant, metadata));
                builder.AddActivityExecutionInspection(new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                    descendant.Execution.ActivityExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    ActivityExecutionInspectionProjection.FromState(descendant, SupersededInspection.LastCheckpointId ?? $"checkpoint:alteration:{Superseded.Execution.ActivityExecutionId}", SupersededAt(), metadata: metadata),
                    metadata));
            }
            builder.AddActivityExecutionInspection(new RuntimeStateChange<ActivityExecutionInspectionProjection>(SupersededInspection.ActivityExecutionId, RuntimeStateChangeOperation.Upsert, SupersededInspection, metadata));
            builder.AddActivityExecutionInspection(new RuntimeStateChange<ActivityExecutionInspectionProjection>(SuccessorInspection.ActivityExecutionId, RuntimeStateChangeOperation.Upsert, SuccessorInspection, metadata));
            builder.AddActivityScopeCleanup(Cleanup);
            builder.AddPostCommitIntent(Continuation);
        }

        private DateTimeOffset SupersededAt() => Superseded.SupersededAt ?? Superseded.CompletedAt
            ?? throw new InvalidOperationException("A superseded activity must carry its terminal timestamp.");
    }
}
