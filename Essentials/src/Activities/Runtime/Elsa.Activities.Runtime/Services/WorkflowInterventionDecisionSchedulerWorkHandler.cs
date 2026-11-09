using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Delivers an operator's intervention decision (spec 260 R-5) to the structural activity that requested the
/// intervention. The <c>DecideIntervention/1</c> alteration checkpoint resolved the incident and enqueued this work item;
/// this handler reactivates the requester from its pinned snapshot and dispatches
/// <see cref="IRuntimeActivityInterventionDecisionHandler.OnInterventionDecidedAsync"/>, applying the returned
/// continuation, with any child schedules, subtree cancellations and notifications it staged, in one atomic checkpoint
/// commit that also records the work item as processed on the requester. A requester that is no longer running, or that
/// already processed this work item, acks it without a callback. Sibling of
/// <see cref="WorkflowNotifyParentActivitySchedulerWorkHandler"/>, with which it shares
/// <see cref="StructuralCallbackEvaluation"/>.
/// </summary>
public sealed class WorkflowInterventionDecisionSchedulerWorkHandler : RuntimeSchedulerWorkHandlerBase<RuntimeInterventionDecisionCommandPayload>
{
    // FROZEN wire value: the drainer's poison path persists this HandlerName verbatim and matches
    // it on recovery. Do not rename the type or this constant.
    public const string HandlerName = nameof(WorkflowInterventionDecisionSchedulerWorkHandler);

    public WorkflowInterventionDecisionSchedulerWorkHandler(
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider? timeProvider = null)
        : base(serviceScopeFactory, timeProvider)
    {
    }

    public override string Name => HandlerName;

    public override bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return workItem.CommandKind == WorkflowExecutionCommandKind.DeliverInterventionDecision;
    }

    protected override async ValueTask HandleWithServicesAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeInterventionDecisionCommandPayload payload,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var viaChild = await FindViaChildAsync(
            serviceProvider.GetRequiredService<IActivityExecutionStateStore>(), workItem, payload, cancellationToken);
        var decided = new ActivityInterventionDecidedContext(
            payload.IncidentId,
            payload.Decision,
            payload.FaultedActivityExecutionId,
            payload.FaultedExecutableNodeId,
            viaChild.Execution.ActivityExecutionId,
            viaChild.Execution.ExecutableNodeId);

        await StructuralCallbackEvaluation.RunAsync(
            serviceProvider,
            TimeProvider,
            workItem,
            payload,
            new StructuralCallbackEvaluation.Definition
            {
                CommandName = "DeliverInterventionDecision",
                EvaluationName = "intervention-decision",
                SnapshotWorkItemMetadataKey = RuntimeMetadataKeys.InterventionDecisionSchedulerWorkItemId,
                DeferredCheckpointReason = "InterventionDecisionEvaluation",
                FaultSubStatus = "InterventionDecisionFaulted",
                CancellationDisposalMessage = "Structural intervention-decision callback cancellation and activation disposal both failed.",
                // A requester that reached a completed scope's state may still hand over its entries, as on any completion.
                AllowsAncestorNotifications = true,
                OptsIn = static activity => activity is IRuntimeActivityInterventionDecisionHandler,
                InvokeAsync = (activity, context) => ((IRuntimeActivityInterventionDecisionHandler)activity).OnInterventionDecidedAsync(context, decided),
                NewCommitSource = checkpointReason => NewCommitSource(workItem, payload, checkpointReason),
                NewFaultIncidentRecordRequest = (checkpointCommitter, state, exception, subStatus, valueSnapshots) =>
                    NewFaultIncidentRecordRequest(checkpointCommitter, workItem, payload, state, exception, subStatus, valueSnapshots),
                MarkProcessed = state => MarkProcessed(state, workItem.WorkItemId)
            },
            alreadyProcessed: state => WasProcessed(state, workItem.WorkItemId),
            cancellationToken);
    }

    /// <summary>Whether the requester already committed this decision delivery.</summary>
    internal static bool WasProcessed(ActivityExecutionState requester, string workItemId) =>
        requester.Metadata.TryGetValue(RuntimeMetadataKeys.InterventionDecisionProcessedWorkItemId, out var processed) &&
        StringComparer.Ordinal.Equals(processed, workItemId);

    private static ActivityExecutionState MarkProcessed(ActivityExecutionState requester, string workItemId)
    {
        var metadata = requester.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.InterventionDecisionProcessedWorkItemId] = workItemId;
        return requester with { Metadata = RuntimeModelMetadata.Snapshot(metadata) };
    }

    // The requester's direct child on the path from the faulted activity: the faulted activity itself for a direct
    // child's fault, or the composite a descendant's fault came through (spec 258 R-3).
    private static async ValueTask<ActivityExecutionState> FindViaChildAsync(
        IActivityExecutionStateStore store,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInterventionDecisionCommandPayload payload,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = await store.FindAsync(workItem.WorkflowExecutionId, payload.FaultedActivityExecutionId, cancellationToken)
                      ?? throw new InvalidOperationException($"DeliverInterventionDecision scheduler work item '{workItem.WorkItemId}' references missing faulted activity execution '{payload.FaultedActivityExecutionId}'.");
        while (visited.Add(current.Execution.ActivityExecutionId))
        {
            if (StringComparer.Ordinal.Equals(current.ParentActivityExecutionId, payload.ActivityExecutionId))
                return current;
            if (current.ParentActivityExecutionId is not { } parentId)
                break;
            current = await store.FindAsync(workItem.WorkflowExecutionId, parentId, cancellationToken)
                      ?? throw new InvalidOperationException($"DeliverInterventionDecision scheduler work item '{workItem.WorkItemId}' references missing activity execution '{parentId}'.");
        }

        throw new InvalidOperationException(
            $"DeliverInterventionDecision scheduler work item '{workItem.WorkItemId}' names faulted activity execution '{payload.FaultedActivityExecutionId}', which is not below requester '{payload.ActivityExecutionId}'.");
    }

    private static ActivityFaultIncidentRecordRequest NewFaultIncidentRecordRequest(
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeInterventionDecisionCommandPayload payload,
        ActivityExecutionState state,
        Exception exception,
        string subStatus,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots)
    {
        var activityMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.CheckpointReason] = payload.Reason,
            [RuntimeMetadataKeys.InterventionDecisionSchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.InterventionDecision] = payload.Decision,
            [RuntimeMetadataKeys.CausalIncidentId] = payload.IncidentId
        };

        return new ActivityFaultIncidentRecordRequest(
            CheckpointCommitter: checkpointCommitter,
            WorkItem: workItem,
            ActivityExecutionId: payload.ActivityExecutionId,
            ExecutableNodeId: payload.ExecutableNodeId,
            State: state,
            Exception: exception,
            SubStatus: subStatus,
            ActivityMetadata: activityMetadata,
            IncidentMetadata: new Dictionary<string, string>(activityMetadata, StringComparer.Ordinal),
            ValueSnapshots: valueSnapshots);
    }

    private static StructuralParentEvaluationSupport.ParentEvaluationCommitSource NewCommitSource(
        RuntimeSchedulerWorkItem decisionWorkItem,
        RuntimeInterventionDecisionCommandPayload decisionPayload,
        string checkpointReason) =>
        StructuralParentEvaluationSupport.NewCommitSource(
            decisionWorkItem,
            decisionPayload,
            checkpointReason,
            derivedCommandMetadata: decisionWorkItem.CommandMetadata,
            evaluationMetadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.InterventionDecision] = decisionPayload.Decision,
                [RuntimeMetadataKeys.CausalIncidentId] = decisionPayload.IncidentId
            });

    private static readonly string[] PayloadValidationParamNames =
    [
        "pinnedExecutable", "executableNodeId", "activityExecutionId", "parentActivityExecutionId", "branchId",
        "incidentId", "decision", "faultedActivityExecutionId", "faultedExecutableNodeId", "reason"
    ];

    protected override RuntimeInterventionDecisionCommandPayload DeserializePayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkItems.DeserializePayload<RuntimeInterventionDecisionCommandPayload>(workItem, "DeliverInterventionDecision", "intervention decision payload", PayloadValidationParamNames);
}
