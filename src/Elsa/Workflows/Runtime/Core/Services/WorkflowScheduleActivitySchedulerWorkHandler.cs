using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowScheduleActivitySchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowScheduleActivitySchedulerWorkHandler);

    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly TimeProvider _timeProvider;

    public WorkflowScheduleActivitySchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue)
        : this(workflowExecutableStore, activityExecutionStateStore, schedulerWorkQueue, TimeProvider.System)
    {
    }

    public WorkflowScheduleActivitySchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workflowExecutableStore = workflowExecutableStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _schedulerWorkQueue = schedulerWorkQueue;
        _timeProvider = timeProvider;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity;
    }

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var schedulePayload = DeserializeSchedulePayload(workItem);
        var executable = await _workflowExecutableStore.FindAsync(schedulePayload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(schedulePayload.PinnedExecutable.ArtifactId);

        ValidatePinnedExecutable(workItem, schedulePayload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.TryGetValue(schedulePayload.ExecutableNodeId, out var executableNode))
            throw new InvalidOperationException($"ScheduleActivity scheduler work item '{workItem.WorkItemId}' references executable node '{schedulePayload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        var existing = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, schedulePayload.ActivityExecutionId, cancellationToken);
        if (existing is not null)
        {
            if (!StringComparer.Ordinal.Equals(existing.Execution.ExecutableNodeId, schedulePayload.ExecutableNodeId))
                throw new InvalidOperationException($"ScheduleActivity scheduler work item '{workItem.WorkItemId}' references executable node '{schedulePayload.ExecutableNodeId}', but activity execution '{schedulePayload.ActivityExecutionId}' belongs to executable node '{existing.Execution.ExecutableNodeId}'.");

            if (existing.Status == ActivityExecutionStatus.Scheduled)
                await EnqueueStartActivityAsync(workItem, schedulePayload, cancellationToken);

            return;
        }

        var state = NewActivityExecutionState(workItem, schedulePayload, executableNode);
        await _activityExecutionStateStore.SaveAsync(state, cancellationToken);
        await EnqueueStartActivityAsync(workItem, schedulePayload, cancellationToken);
    }

    private static RuntimeScheduleActivityCommandPayload DeserializeSchedulePayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("ScheduleActivity scheduler work item requires a schedule activity payload.");

        try
        {
            return payload.Deserialize<RuntimeScheduleActivityCommandPayload>()
                   ?? throw new InvalidOperationException("ScheduleActivity scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsSchedulePayloadValidationException(argumentException))
        {
            throw new InvalidOperationException("ScheduleActivity scheduler work item payload is not a valid schedule activity payload.", exception);
        }
    }

    private static bool IsSchedulePayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "executableNodeId" or
            "activityExecutionId" or
            "reason" or
            "schedulingActivityExecutionId";

    private static void ValidatePinnedExecutable(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutableIdentity loadedExecutable)
    {
        if (WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(loadedExecutable, pinnedExecutable))
            return;

        throw new InvalidOperationException(
            $"ScheduleActivity scheduler work item '{workItem.WorkItemId}' loaded executable artifact '{WorkflowExecutableIdentityComparer.Format(loadedExecutable)}' " +
            $"but pinned executable artifact '{WorkflowExecutableIdentityComparer.Format(pinnedExecutable)}'.");
    }

    private ActivityExecutionState NewActivityExecutionState(
        RuntimeSchedulerWorkItem workItem,
        RuntimeScheduleActivityCommandPayload schedulePayload,
        ExecutableNode executableNode)
    {
        var scheduledAt = _timeProvider.GetUtcNow();
        var execution = new ActivityExecution(
            ActivityExecutionId: schedulePayload.ActivityExecutionId,
            WorkflowExecutionId: workItem.WorkflowExecutionId,
            ExecutableNodeId: executableNode.ExecutableNodeId,
            AuthoredActivityId: executableNode.AuthoredActivityId,
            ActivityType: executableNode.ActivityType,
            ActivityTypeVersion: executableNode.ActivityTypeVersion);

        return new ActivityExecutionState(
            Execution: execution,
            Status: ActivityExecutionStatus.Scheduled,
            SubStatus: null,
            ScheduledAt: scheduledAt,
            StartedAt: null,
            CompletedAt: null,
            SchedulingActivityExecutionId: schedulePayload.SchedulingActivityExecutionId,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: null,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>
            {
                ["runtime.scheduleReason"] = schedulePayload.Reason,
                ["runtime.schedulerWorkItemId"] = workItem.WorkItemId,
                ["runtime.pinnedArtifactId"] = schedulePayload.PinnedExecutable.ArtifactId
            });
    }

    private async ValueTask EnqueueStartActivityAsync(
        RuntimeSchedulerWorkItem scheduleWorkItem,
        RuntimeScheduleActivityCommandPayload schedulePayload,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var payload = new RuntimeStartActivityCommandPayload(
            schedulePayload.PinnedExecutable,
            schedulePayload.ExecutableNodeId,
            schedulePayload.ActivityExecutionId,
            RuntimeStartActivityCommandPayload.ScheduledActivityReason);

        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: $"{scheduleWorkItem.WorkItemId}:start:{schedulePayload.ActivityExecutionId}",
            workflowExecutionId: scheduleWorkItem.WorkflowExecutionId,
            commandId: $"{scheduleWorkItem.CommandId}:start:{schedulePayload.ActivityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.StartActivity,
            envelopeId: scheduleWorkItem.EnvelopeId,
            idempotencyKey: $"{scheduleWorkItem.IdempotencyKey}:start:{schedulePayload.ActivityExecutionId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: scheduleWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: scheduleWorkItem.CommandMetadata,
            envelopeMetadata: scheduleWorkItem.EnvelopeMetadata);

        await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }
}
