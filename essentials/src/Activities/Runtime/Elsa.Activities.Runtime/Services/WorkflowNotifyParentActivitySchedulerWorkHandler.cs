using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Handles a non-terminal child→parent structural notification (seam C). Reactivates the target
/// parent — the notifying child's committed parent — from its pinned snapshot and dispatches
/// <see cref="IRuntimeActivityChildNotificationHandler.OnChildNotifiedAsync"/>, applying the returned
/// continuation (and any seam-A subtree cancellations / child schedules / further parent notifications it
/// staged) in one atomic checkpoint commit. The notifying child keeps running throughout; a notification whose
/// child has since completed or faulted still delivers (only the parent's non-Running state acks it away), and
/// a parent that does not implement the interface acks the notification silently. Sibling of
/// <see cref="WorkflowParentActivityCompletionSchedulerWorkHandler"/>.
/// </summary>
public sealed class WorkflowNotifyParentActivitySchedulerWorkHandler : RuntimeSchedulerWorkHandlerBase<RuntimeNotifyParentCommandPayload>
{
    // FROZEN wire value: the drainer's poison path persists this HandlerName verbatim and matches
    // it on recovery. Do not rename the type or this constant.
    public const string HandlerName = nameof(WorkflowNotifyParentActivitySchedulerWorkHandler);

    public WorkflowNotifyParentActivitySchedulerWorkHandler(
        IServiceScopeFactory serviceScopeFactory,
        TimeProvider? timeProvider = null)
        : base(serviceScopeFactory, timeProvider)
    {
    }

    public override string Name => HandlerName;

    public override bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        return workItem.CommandKind == WorkflowExecutionCommandKind.NotifyParentActivity;
    }

    protected override ValueTask HandleWithServicesAsync(
        RuntimeSchedulerWorkItem workItem,
        RuntimeNotifyParentCommandPayload payload,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken) =>
        // The notifying child's own status is NOT a delivery gate — a child that completed or faulted after staging
        // still delivers; only a parent that is no longer running acks the notification away.
        StructuralCallbackEvaluation.RunAsync(
            serviceProvider,
            TimeProvider,
            workItem,
            payload,
            new StructuralCallbackEvaluation.Definition
            {
                CommandName = "NotifyParentActivity",
                EvaluationName = "child-notification",
                SnapshotWorkItemMetadataKey = RuntimeMetadataKeys.NotifyParentSchedulerWorkItemId,
                DeferredCheckpointReason = "ChildNotificationEvaluation",
                FaultSubStatus = "ParentNotificationFaulted",
                CancellationDisposalMessage = "Structural notification callback cancellation and activation disposal both failed.",
                OptsIn = static activity => activity is IRuntimeActivityChildNotificationHandler,
                InvokeAsync = (activity, context) => ((IRuntimeActivityChildNotificationHandler)activity).OnChildNotifiedAsync(
                    context,
                    new ActivityChildNotifiedContext(
                        payload.NotifyingChildActivityExecutionId,
                        payload.NotifyingChildExecutableNodeId,
                        payload.Code,
                        payload.NotifyingChildIterationId,
                        payload.Payload,
                        payload.ViaChildActivityExecutionId,
                        payload.ViaChildExecutableNodeId)),
                NewCommitSource = checkpointReason => NewCommitSource(workItem, payload, checkpointReason),
                NewFaultIncidentRecordRequest = (checkpointCommitter, state, exception, subStatus, valueSnapshots) =>
                    NewFaultIncidentRecordRequest(checkpointCommitter, workItem, payload, state, exception, subStatus, valueSnapshots)
            },
            alreadyProcessed: null,
            cancellationToken);

    private static ActivityFaultIncidentRecordRequest NewFaultIncidentRecordRequest(
        RuntimeCheckpointCommitter checkpointCommitter,
        RuntimeSchedulerWorkItem workItem,
        RuntimeNotifyParentCommandPayload payload,
        ActivityExecutionState state,
        Exception exception,
        string subStatus,
        IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> valueSnapshots)
    {
        var activityMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.CheckpointReason] = payload.Reason,
            [RuntimeMetadataKeys.NotifyParentSchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.NotifyingChildActivityExecutionId] = payload.NotifyingChildActivityExecutionId,
            [RuntimeMetadataKeys.ParentNotificationCode] = payload.Code
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
        RuntimeSchedulerWorkItem notifyWorkItem,
        RuntimeNotifyParentCommandPayload notifyPayload,
        string checkpointReason) =>
        StructuralParentEvaluationSupport.NewCommitSource(
            notifyWorkItem,
            notifyPayload,
            checkpointReason,
            derivedCommandMetadata: notifyWorkItem.CommandMetadata,
            evaluationMetadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.NotifyingChildActivityExecutionId] = notifyPayload.NotifyingChildActivityExecutionId,
                [RuntimeMetadataKeys.ParentNotificationCode] = notifyPayload.Code
            });

    private static readonly string[] PayloadValidationParamNames =
    [
        "pinnedExecutable", "executableNodeId", "activityExecutionId", "parentActivityExecutionId", "branchId",
        "notifyingChildActivityExecutionId", "notifyingChildExecutableNodeId", "notifyingChildIterationId", "code", "reason"
    ];

    protected override RuntimeNotifyParentCommandPayload DeserializePayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkItems.DeserializePayload<RuntimeNotifyParentCommandPayload>(workItem, "NotifyParentActivity", "notify parent payload", PayloadValidationParamNames);

}
