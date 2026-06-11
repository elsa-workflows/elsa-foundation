using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowCompleteActivitySchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowCompleteActivitySchedulerWorkHandler);

    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly TimeProvider _timeProvider;

    public WorkflowCompleteActivitySchedulerWorkHandler(
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue)
        : this(activityExecutionStateStore, schedulerWorkQueue, TimeProvider.System)
    {
    }

    public WorkflowCompleteActivitySchedulerWorkHandler(
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _activityExecutionStateStore = activityExecutionStateStore;
        _schedulerWorkQueue = schedulerWorkQueue;
        _timeProvider = timeProvider;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.CompleteActivity;
    }

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var payload = DeserializeCompletePayload(workItem);
        if (payload.CompletionKind != SchedulerCompletionKind.ActivityCompleted || payload.ParentActivityExecutionId is null)
            return;

        var parentState = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, payload.ParentActivityExecutionId, cancellationToken);
        if (parentState is null)
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references missing parent activity execution '{payload.ParentActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

        await EnqueueParentCompletionEvaluationAsync(workItem, payload, parentState, cancellationToken);
    }

    private async ValueTask EnqueueParentCompletionEvaluationAsync(
        RuntimeSchedulerWorkItem activityCompletedWorkItem,
        RuntimeCompleteActivityCommandPayload activityCompletedPayload,
        ActivityExecutionState parentState,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var parentActivityExecutionId = parentState.Execution.ActivityExecutionId;
        var completedChildActivityExecutionId = activityCompletedPayload.ActivityExecutionId;
        var payload = new RuntimeCompleteActivityCommandPayload(
            activityCompletedPayload.PinnedExecutable,
            parentState.Execution.ExecutableNodeId,
            parentActivityExecutionId,
            parentState.ParentActivityExecutionId,
            parentState.BranchId,
            outcomeNames: [],
            RuntimeCompleteActivityCommandPayload.ParentCompletionEvaluationReason,
            SchedulerCompletionKind.ParentCompletionEvaluation,
            completedChildActivityExecutionId);

        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: $"{activityCompletedWorkItem.WorkItemId}:parent:{parentActivityExecutionId}:child:{completedChildActivityExecutionId}",
            workflowExecutionId: activityCompletedWorkItem.WorkflowExecutionId,
            commandId: $"{activityCompletedWorkItem.CommandId}:parent:{parentActivityExecutionId}:child:{completedChildActivityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: activityCompletedWorkItem.EnvelopeId,
            idempotencyKey: $"{activityCompletedWorkItem.IdempotencyKey}:parent:{parentActivityExecutionId}:child:{completedChildActivityExecutionId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: activityCompletedWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: activityCompletedWorkItem.CommandMetadata,
            envelopeMetadata: activityCompletedWorkItem.EnvelopeMetadata);

        await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private static RuntimeCompleteActivityCommandPayload DeserializeCompletePayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("CompleteActivity scheduler work item requires a complete activity payload.");

        try
        {
            return payload.Deserialize<RuntimeCompleteActivityCommandPayload>()
                   ?? throw new InvalidOperationException("CompleteActivity scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argumentException && IsCompletePayloadValidationException(argumentException))
        {
            throw new InvalidOperationException("CompleteActivity scheduler work item payload is not a valid complete activity payload.", exception);
        }
    }

    private static bool IsCompletePayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "executableNodeId" or
            "activityExecutionId" or
            "parentActivityExecutionId" or
            "branchId" or
            "outcomeNames" or
            "reason" or
            "completionKind" or
            "completedChildActivityExecutionId";
}
