using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowStartActivitySchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowStartActivitySchedulerWorkHandler);

    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly TimeProvider _timeProvider;

    public WorkflowStartActivitySchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue)
        : this(workflowExecutableStore, activityExecutionStateStore, schedulerWorkQueue, TimeProvider.System)
    {
    }

    public WorkflowStartActivitySchedulerWorkHandler(
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

        return workItem.CommandKind == WorkflowExecutionCommandKind.StartActivity;
    }

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var startPayload = DeserializeStartPayload(workItem);
        var executable = await _workflowExecutableStore.FindAsync(startPayload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(startPayload.PinnedExecutable.ArtifactId);

        ValidatePinnedExecutable(workItem, startPayload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.ContainsKey(startPayload.ExecutableNodeId))
            throw new InvalidOperationException($"StartActivity scheduler work item '{workItem.WorkItemId}' references executable node '{startPayload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        var state = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, startPayload.ActivityExecutionId, cancellationToken);
        if (state is null)
            throw new InvalidOperationException($"StartActivity scheduler work item '{workItem.WorkItemId}' references missing activity execution '{startPayload.ActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        if (!StringComparer.Ordinal.Equals(state.Execution.ExecutableNodeId, startPayload.ExecutableNodeId))
            throw new InvalidOperationException($"StartActivity scheduler work item '{workItem.WorkItemId}' references executable node '{startPayload.ExecutableNodeId}', but activity execution '{startPayload.ActivityExecutionId}' belongs to executable node '{state.Execution.ExecutableNodeId}'.");

        if (state.Status == ActivityExecutionStatus.Running)
        {
            await EnqueueInvokeActivityAsync(workItem, startPayload, cancellationToken);
            return;
        }

        if (state.Status != ActivityExecutionStatus.Scheduled)
            return;

        await _activityExecutionStateStore.SaveAsync(StartActivity(workItem, startPayload, state), cancellationToken);
        await EnqueueInvokeActivityAsync(workItem, startPayload, cancellationToken);
    }

    private static RuntimeStartActivityCommandPayload DeserializeStartPayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("StartActivity scheduler work item requires a start activity payload.");

        try
        {
            return payload.Deserialize<RuntimeStartActivityCommandPayload>()
                   ?? throw new InvalidOperationException("StartActivity scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsStartPayloadValidationException(argumentException))
        {
            throw new InvalidOperationException("StartActivity scheduler work item payload is not a valid start activity payload.", exception);
        }
    }

    private static bool IsStartPayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "executableNodeId" or
            "activityExecutionId" or
            "reason";

    private static void ValidatePinnedExecutable(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutableIdentity loadedExecutable)
    {
        if (WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(loadedExecutable, pinnedExecutable))
            return;

        throw new InvalidOperationException(
            $"StartActivity scheduler work item '{workItem.WorkItemId}' loaded executable artifact '{WorkflowExecutableIdentityComparer.Format(loadedExecutable)}' " +
            $"but pinned executable artifact '{WorkflowExecutableIdentityComparer.Format(pinnedExecutable)}'.");
    }

    private ActivityExecutionState StartActivity(
        RuntimeSchedulerWorkItem workItem,
        RuntimeStartActivityCommandPayload startPayload,
        ActivityExecutionState state)
    {
        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["runtime.startReason"] = startPayload.Reason;
        metadata["runtime.startSchedulerWorkItemId"] = workItem.WorkItemId;

        return state with
        {
            Status = ActivityExecutionStatus.Running,
            StartedAt = _timeProvider.GetUtcNow(),
            Metadata = metadata
        };
    }

    private async ValueTask EnqueueInvokeActivityAsync(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeStartActivityCommandPayload startPayload,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var payload = new RuntimeInvokeActivityCommandPayload(
            startPayload.PinnedExecutable,
            startPayload.ExecutableNodeId,
            startPayload.ActivityExecutionId,
            RuntimeInvokeActivityCommandPayload.StartedActivityReason);

        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: $"{startWorkItem.WorkItemId}:invoke:{startPayload.ActivityExecutionId}",
            workflowExecutionId: startWorkItem.WorkflowExecutionId,
            commandId: $"{startWorkItem.CommandId}:invoke:{startPayload.ActivityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: startWorkItem.EnvelopeId,
            idempotencyKey: $"{startWorkItem.IdempotencyKey}:invoke:{startPayload.ActivityExecutionId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: startWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: startWorkItem.CommandMetadata,
            envelopeMetadata: startWorkItem.EnvelopeMetadata);

        await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }
}
