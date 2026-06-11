using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowCompleteActivitySchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowCompleteActivitySchedulerWorkHandler);

    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly IWorkflowExecutableStore? _workflowExecutableStore;
    private readonly IRuntimeExecutionIdGenerator? _idGenerator;
    private readonly TimeProvider _timeProvider;

    public WorkflowCompleteActivitySchedulerWorkHandler(
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue)
        : this(activityExecutionStateStore, schedulerWorkQueue, workflowExecutableStore: null, idGenerator: null, TimeProvider.System)
    {
    }

    public WorkflowCompleteActivitySchedulerWorkHandler(
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        TimeProvider timeProvider)
        : this(activityExecutionStateStore, schedulerWorkQueue, workflowExecutableStore: null, idGenerator: null, timeProvider)
    {
    }

    public WorkflowCompleteActivitySchedulerWorkHandler(
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IWorkflowExecutableStore workflowExecutableStore,
        IRuntimeExecutionIdGenerator idGenerator)
        : this(activityExecutionStateStore, schedulerWorkQueue, workflowExecutableStore, idGenerator, TimeProvider.System)
    {
    }

    public WorkflowCompleteActivitySchedulerWorkHandler(
        IActivityExecutionStateStore activityExecutionStateStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IWorkflowExecutableStore? workflowExecutableStore,
        IRuntimeExecutionIdGenerator? idGenerator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _activityExecutionStateStore = activityExecutionStateStore;
        _schedulerWorkQueue = schedulerWorkQueue;
        _workflowExecutableStore = workflowExecutableStore;
        _idGenerator = idGenerator;
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
        switch (payload.CompletionKind)
        {
            case SchedulerCompletionKind.ActivityCompleted:
                if (payload.ParentActivityExecutionId is null)
                    return;

                var parentState = await _activityExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, payload.ParentActivityExecutionId, cancellationToken);
                if (parentState is null)
                    throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references missing parent activity execution '{payload.ParentActivityExecutionId}' for workflow execution '{workItem.WorkflowExecutionId}'.");

                await EnqueueParentCompletionEvaluationAsync(workItem, payload, parentState, cancellationToken);
                return;

            case SchedulerCompletionKind.ParentCompletionEvaluation:
                await EnqueueContinuationSchedulingAsync(workItem, payload, cancellationToken);
                return;

            case SchedulerCompletionKind.ContinuationScheduling:
                await EnqueueCheckpointAsync(workItem, payload, cancellationToken);
                return;

            default:
                throw new InvalidOperationException($"CompleteActivity scheduler work item '{workItem.WorkItemId}' references unsupported completion kind '{payload.CompletionKind}'.");
        }
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

    private async ValueTask EnqueueCheckpointAsync(
        RuntimeSchedulerWorkItem continuationSchedulingWorkItem,
        RuntimeCompleteActivityCommandPayload continuationSchedulingPayload,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var activityExecutionId = continuationSchedulingPayload.ActivityExecutionId;
        var checkpointName = RuntimeCheckpointNames.ActivityCompleted;
        var postCommitIntents = await CreateDownstreamSchedulerIntentsAsync(continuationSchedulingWorkItem, continuationSchedulingPayload, now, cancellationToken);
        var payload = new RuntimeCheckpointCommandPayload(
            continuationSchedulingPayload.PinnedExecutable,
            checkpointName,
            [activityExecutionId],
            RuntimeCheckpointCommandPayload.ActivityCompletionPropagationReason,
            postCommitIntents);

        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: $"{continuationSchedulingWorkItem.WorkItemId}:checkpoint:{checkpointName}:{activityExecutionId}",
            workflowExecutionId: continuationSchedulingWorkItem.WorkflowExecutionId,
            commandId: $"{continuationSchedulingWorkItem.CommandId}:checkpoint:{checkpointName}:{activityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            envelopeId: continuationSchedulingWorkItem.EnvelopeId,
            idempotencyKey: $"{continuationSchedulingWorkItem.IdempotencyKey}:checkpoint:{checkpointName}:{activityExecutionId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: continuationSchedulingWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: continuationSchedulingWorkItem.CommandMetadata,
            envelopeMetadata: continuationSchedulingWorkItem.EnvelopeMetadata);

        await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }

    private async ValueTask<IReadOnlyCollection<RuntimePostCommitIntent>> CreateDownstreamSchedulerIntentsAsync(
        RuntimeSchedulerWorkItem continuationSchedulingWorkItem,
        RuntimeCompleteActivityCommandPayload continuationSchedulingPayload,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (_workflowExecutableStore is null || _idGenerator is null || continuationSchedulingPayload.OutcomeNames.Count == 0)
            return [];

        var executable = await _workflowExecutableStore.FindAsync(continuationSchedulingPayload.PinnedExecutable.ArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(continuationSchedulingPayload.PinnedExecutable.ArtifactId);

        ValidatePinnedExecutable(continuationSchedulingWorkItem, continuationSchedulingPayload.PinnedExecutable, executable.Identity);

        if (!executable.NodesById.ContainsKey(continuationSchedulingPayload.ExecutableNodeId))
            throw new InvalidOperationException($"CompleteActivity scheduler work item '{continuationSchedulingWorkItem.WorkItemId}' references executable node '{continuationSchedulingPayload.ExecutableNodeId}', which is missing from executable artifact '{WorkflowExecutableIdentityComparer.Format(executable.Identity)}'.");

        var outcomeNames = continuationSchedulingPayload.OutcomeNames.ToHashSet(StringComparer.Ordinal);
        var matchingEdges = executable.Edges
            .Where(edge => StringComparer.Ordinal.Equals(edge.SourceNodeId, continuationSchedulingPayload.ExecutableNodeId) && outcomeNames.Contains(edge.SourcePort))
            .ToArray();

        if (matchingEdges.Length == 0)
            return [];

        var postCommitIntents = new List<RuntimePostCommitIntent>(matchingEdges.Length);
        for (var index = 0; index < matchingEdges.Length; index++)
        {
            var edge = matchingEdges[index];
            var downstreamWorkItem = NewDownstreamScheduleWorkItem(
                continuationSchedulingWorkItem,
                continuationSchedulingPayload,
                edge,
                index + 1,
                now);

            postCommitIntents.Add(new RuntimePostCommitIntent(
                intentId: $"{continuationSchedulingWorkItem.WorkItemId}:postcommit:schedule:{index + 1}:{edge.SourcePort}:{edge.TargetNodeId}",
                workflowExecutionId: continuationSchedulingWorkItem.WorkflowExecutionId,
                kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
                recordedAt: now,
                activityExecutionId: continuationSchedulingPayload.ActivityExecutionId,
                idempotencyKey: downstreamWorkItem.IdempotencyKey,
                payload: JsonSerializer.SerializeToElement(downstreamWorkItem),
                metadata: new Dictionary<string, string>
                {
                    ["runtime.sourceSchedulerWorkItemId"] = continuationSchedulingWorkItem.WorkItemId,
                    ["runtime.schedulerWorkItemId"] = downstreamWorkItem.WorkItemId,
                    ["runtime.sourceExecutableNodeId"] = edge.SourceNodeId,
                    ["runtime.sourcePort"] = edge.SourcePort,
                    ["runtime.targetExecutableNodeId"] = edge.TargetNodeId
                }));
        }

        return postCommitIntents;
    }

    private RuntimeSchedulerWorkItem NewDownstreamScheduleWorkItem(
        RuntimeSchedulerWorkItem continuationSchedulingWorkItem,
        RuntimeCompleteActivityCommandPayload continuationSchedulingPayload,
        ExecutableEdge edge,
        int index,
        DateTimeOffset now)
    {
        var activityExecutionId = _idGenerator!.NewActivityExecutionId();
        var payload = new RuntimeScheduleActivityCommandPayload(
            continuationSchedulingPayload.PinnedExecutable,
            edge.TargetNodeId,
            activityExecutionId,
            RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
            continuationSchedulingPayload.ActivityExecutionId);

        return new RuntimeSchedulerWorkItem(
            workItemId: $"{continuationSchedulingWorkItem.WorkItemId}:schedule:{index}:{edge.SourcePort}:{edge.TargetNodeId}",
            workflowExecutionId: continuationSchedulingWorkItem.WorkflowExecutionId,
            commandId: $"{continuationSchedulingWorkItem.CommandId}:schedule:{index}:{edge.SourcePort}:{edge.TargetNodeId}",
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            envelopeId: continuationSchedulingWorkItem.EnvelopeId,
            idempotencyKey: $"{continuationSchedulingWorkItem.IdempotencyKey}:schedule:{index}:{edge.SourcePort}:{edge.TargetNodeId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: continuationSchedulingWorkItem.Sequence is { } sequence ? sequence + 1 + index : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: continuationSchedulingWorkItem.CommandMetadata,
            envelopeMetadata: continuationSchedulingWorkItem.EnvelopeMetadata);
    }

    private static void ValidatePinnedExecutable(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutableIdentity loadedExecutable)
    {
        if (WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(loadedExecutable, pinnedExecutable))
            return;

        throw new InvalidOperationException(
            $"CompleteActivity scheduler work item '{workItem.WorkItemId}' loaded executable artifact '{WorkflowExecutableIdentityComparer.Format(loadedExecutable)}' " +
            $"but pinned executable artifact '{WorkflowExecutableIdentityComparer.Format(pinnedExecutable)}'.");
    }

    private async ValueTask EnqueueContinuationSchedulingAsync(
        RuntimeSchedulerWorkItem parentEvaluationWorkItem,
        RuntimeCompleteActivityCommandPayload parentEvaluationPayload,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var activityExecutionId = parentEvaluationPayload.ActivityExecutionId;
        var payload = new RuntimeCompleteActivityCommandPayload(
            parentEvaluationPayload.PinnedExecutable,
            parentEvaluationPayload.ExecutableNodeId,
            activityExecutionId,
            parentEvaluationPayload.ParentActivityExecutionId,
            parentEvaluationPayload.BranchId,
            parentEvaluationPayload.OutcomeNames,
            RuntimeCompleteActivityCommandPayload.ContinuationSchedulingReason,
            SchedulerCompletionKind.ContinuationScheduling);

        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: $"{parentEvaluationWorkItem.WorkItemId}:continuation:{activityExecutionId}",
            workflowExecutionId: parentEvaluationWorkItem.WorkflowExecutionId,
            commandId: $"{parentEvaluationWorkItem.CommandId}:continuation:{activityExecutionId}",
            commandKind: WorkflowExecutionCommandKind.CompleteActivity,
            envelopeId: parentEvaluationWorkItem.EnvelopeId,
            idempotencyKey: $"{parentEvaluationWorkItem.IdempotencyKey}:continuation:{activityExecutionId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: parentEvaluationWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: parentEvaluationWorkItem.CommandMetadata,
            envelopeMetadata: parentEvaluationWorkItem.EnvelopeMetadata);

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
