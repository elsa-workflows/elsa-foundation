using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Checkpoints;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// The successor and supersession construction shared by the alterations that replace one activity execution with a
/// distinct successor (<c>RescheduleActivity/1</c>, <c>RetryFaultedActivity/1</c>). Each alteration names its own
/// identity slug, continuation reason and successor provenance; everything else is built the same way, so both
/// produce the same supersession lineage, cleanup and continuation shape.
/// </summary>
internal static class ActivitySuccessorStaging
{
    /// <summary>The deterministic successor id: a replayed preflight of the same job derives the same successor.</summary>
    public static string SuccessorId(WorkflowAlterationPreflightContext context, string slug, ActivityExecutionState source) =>
        RuntimeChainId.Derive(context.Job.JobId, $"alteration-{slug}:{context.Ordinal}:{source.Execution.ActivityExecutionId}");

    /// <summary>The fresh execution scope a successor owns.</summary>
    public static string SuccessorScopeId(string successorId) => RuntimeChainId.Derive(successorId, "scope");

    /// <summary>
    /// Builds the successor of <paramref name="source"/>. It keeps the source's inputs, parent, scheduling identity,
    /// branch, iteration and pinned node, and starts directly when its inputs are pinned and it is a CLR activity;
    /// <c>StartActivity</c> executes intrinsics only from <c>Scheduled</c>, so intrinsic successors re-enter through
    /// <c>ScheduleActivity</c>.
    /// </summary>
    public static ActivityExecutionState NewSuccessor(
        ActivityExecutionState source,
        ExecutableNode sourceNode,
        string successorId,
        string scopeId,
        ActivitySchedulingProvenance provenance,
        IReadOnlyDictionary<string, string> metadata,
        DateTimeOffset now)
    {
        var successorStatus = sourceNode.IntrinsicKind is null && source.InputSnapshot is not null
            ? ActivityExecutionStatus.Running
            : ActivityExecutionStatus.Scheduled;
        return source with
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
            Metadata = metadata,
            Attempts = [],
            PrivateState = null,
            // Values and binding policy stay pinned, but the typed snapshot belongs to this new
            // native invocation. Carrying the old owner fails VF-ACT-001 at typed completion.
            InputSnapshot = source.InputSnapshot is { } input ? new ActivityInputSnapshot(successorId,
                input.ContractFingerprint, input.BindingFingerprint, input.Values, input.MaterializedAt) : null,
            Completion = null,
            Fault = null,
            TriggerRegistrations = [],
            TriggerDeliveries = [],
            Attempt = null,
            SupersededByActivityExecutionId = null,
            SupersededAt = null
        };
    }

    /// <summary>Marks <paramref name="source"/> superseded by <paramref name="successorId"/>, validating the lineage.</summary>
    public static ActivityExecutionState Supersede(ActivityExecutionState source, string successorId, DateTimeOffset now)
    {
        var superseded = source with
        {
            Status = ActivityExecutionStatus.Superseded,
            CompletedAt = now,
            SupersededByActivityExecutionId = successorId,
            SupersededAt = now
        };
        superseded.EnsureSupersessionCompatible();
        return superseded;
    }

    /// <summary>
    /// Copies <paramref name="source"/> metadata, except the <see cref="PerInvocationMetadataKeys"/>, and records which
    /// execution the successor supersedes.
    /// </summary>
    // The successor is a fresh invocation of the same occurrence. Carried over, the per-invocation markers would tell
    // the parent-completion handler the successor's outcome was already processed (so a structural parent that already
    // evaluated the source's fault never hears of the successor), point a background coordinator at the source's work
    // record, and describe a fault the successor never had.
    public static Dictionary<string, string> SuccessorMetadata(IReadOnlyDictionary<string, string> source, string sourceId)
    {
        var metadata = source.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var key in PerInvocationMetadataKeys)
            metadata.Remove(key);
        metadata[RuntimeMetadataKeys.SupersedesActivityExecutionId] = sourceId;
        return metadata;
    }

    /// <summary>The activity metadata the runtime keeps per invocation, which a successor never inherits.</summary>
    public static readonly IReadOnlyList<string> PerInvocationMetadataKeys =
    [
        RuntimeMetadataKeys.ActivityAttemptActivationClaim,
        RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId,
        RuntimeMetadataKeys.ActivityActivationCompletedWorkItemId,
        RuntimeMetadataKeys.ActivityAttemptOrdinalHighWatermark,
        RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId,
        RuntimeMetadataKeys.ParentOutcomeWorkItemId,
        RuntimeMetadataKeys.DescendantFaultProcessedWorkItemId,
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
        RuntimeMetadataKeys.BackgroundActivityWorkId
    ];

    /// <summary>The successor's scheduling command metadata, rebuilt from its durable scheduling provenance.</summary>
    // The source's own schedule command is gone, so its successor's command is rebuilt from the durable scheduling
    // provenance, the way an ordinary child schedule carries the parent's request metadata. A structural parent reads
    // its routing data from the child's command metadata, so this is what lets it receive the successor's outcome as
    // an ordinary child outcome. Routing data a parent kept only in its command metadata is not durable and is lost.
    public static Dictionary<string, string> SchedulingMetadata(ActivityExecutionState successor)
    {
        var metadata = successor.Provenance.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (successor.ParentActivityExecutionId is { } parentActivityExecutionId)
            metadata[RuntimeMetadataKeys.ParentActivityExecutionId] = parentActivityExecutionId;
        metadata[RuntimeMetadataKeys.ChildExecutableNodeId] = successor.Execution.ExecutableNodeId;
        return metadata;
    }

    /// <summary>
    /// The post-commit intent that starts (or schedules) the successor once the alteration checkpoint lands. Its command
    /// metadata names the alteration; <paramref name="schedulingMetadata"/>, when given, is laid underneath it.
    /// </summary>
    public static RuntimePostCommitIntent NewContinuation(
        WorkflowExecutionState workflow,
        ActivityExecutionState successor,
        WorkflowAlterationPreflightContext context,
        DateTimeOffset now,
        string slug,
        string reason,
        IReadOnlyDictionary<string, string>? schedulingMetadata = null)
    {
        var commandMetadata = schedulingMetadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : schedulingMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        commandMetadata["runtime.alteration.jobId"] = context.Job.JobId;
        commandMetadata["runtime.alteration.planId"] = context.Job.PlanId;
        commandMetadata["runtime.alteration.kind"] = context.Envelope.Kind;
        var commandKind = successor.Status == ActivityExecutionStatus.Running
            ? WorkflowExecutionCommandKind.StartActivity
            : WorkflowExecutionCommandKind.ScheduleActivity;
        object payload = successor.Status == ActivityExecutionStatus.Running
            ? new RuntimeStartActivityCommandPayload(workflow.PinnedExecutable, successor.Execution.ExecutableNodeId, successor.Execution.ActivityExecutionId, reason)
            : new RuntimeScheduleActivityCommandPayload(workflow.PinnedExecutable, successor.Execution.ExecutableNodeId, successor.Execution.ActivityExecutionId, reason, successor.SchedulingActivityExecutionId, successor.ParentActivityExecutionId, successor.Provenance);
        var workItem = new RuntimeSchedulerWorkItem(
            RuntimeChainId.Derive(context.Job.JobId, $"alteration-{slug}-work:{context.Ordinal}:{successor.Execution.ActivityExecutionId}"),
            workflow.WorkflowExecutionId,
            RuntimeChainId.Derive(context.Job.JobId, $"alteration-{slug}-command:{context.Ordinal}:{successor.Execution.ActivityExecutionId}"),
            commandKind,
            context.Job.JobId,
            RuntimeChainId.Derive(context.Job.JobId, $"alteration-{slug}-idempotency:{context.Ordinal}:{successor.Execution.ActivityExecutionId}"),
            now,
            now,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: commandMetadata,
            envelopeMetadata: new Dictionary<string, string>(),
            executionScopeId: successor.ExecutionScopeId);
        return new RuntimePostCommitIntent(
            $"alteration:{context.Job.JobId}:{slug}:{context.Ordinal}:{successor.Execution.ActivityExecutionId}",
            workflow.WorkflowExecutionId,
            RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            now,
            successor.Execution.ActivityExecutionId,
            workItem.IdempotencyKey,
            JsonSerializer.SerializeToElement(workItem),
            workItem.CommandMetadata,
            materializedSchedulerWorkItem: workItem);
    }

    /// <summary>The source and every descendant below it, the set a supersession retires.</summary>
    public static IReadOnlyCollection<ActivityExecutionState> SupersededSubtree(
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

    /// <summary>The execution ids and execution scopes of <paramref name="activities"/>, to match claimed scheduler work.</summary>
    public static IReadOnlySet<string> SupersededSubtreeScopes(IReadOnlyCollection<ActivityExecutionState> activities) =>
        activities
            .SelectMany(activity => new[] { activity.Execution.ActivityExecutionId, activity.ExecutionScopeId })
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope!)
            .ToHashSet(StringComparer.Ordinal);

    private static long StableSequence(string value) => BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(value)), 0) & long.MaxValue;

    /// <summary>
    /// One supersession's checkpoint effects: the superseded source, its successor, the cancelled live descendants with
    /// their inspections, the subtree cleanup, the continuation, and any incident changes the alteration makes.
    /// </summary>
    internal sealed record SupersessionStagedChange(
        string ChangeKey,
        ActivityExecutionState Superseded,
        ActivityExecutionState Successor,
        IReadOnlyCollection<ActivityExecutionState> CancelledDescendants,
        ActivityScopeCleanupRequest Cleanup,
        ActivityExecutionInspectionProjection SupersededInspection,
        ActivityExecutionInspectionProjection SuccessorInspection,
        RuntimePostCommitIntent Continuation,
        IReadOnlyCollection<IncidentState> Incidents) : IWorkflowAlterationRuntimeCheckpointStagedChange
    {
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
            foreach (var incident in Incidents)
                builder.AddIncident(new RuntimeStateChange<IncidentState>(incident.IncidentId, RuntimeStateChangeOperation.Upsert, incident, metadata));
            builder.AddActivityScopeCleanup(Cleanup);
            builder.AddPostCommitIntent(Continuation);
        }

        private DateTimeOffset SupersededAt() => Superseded.SupersededAt ?? Superseded.CompletedAt
            ?? throw new InvalidOperationException("A superseded activity must carry its terminal timestamp.");
    }
}
