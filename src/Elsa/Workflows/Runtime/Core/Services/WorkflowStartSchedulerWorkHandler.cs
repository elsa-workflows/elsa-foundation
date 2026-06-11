using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowStartSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowStartSchedulerWorkHandler);

    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly IRuntimeExecutionIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;

    public WorkflowStartSchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IRuntimeExecutionIdGenerator idGenerator)
        : this(workflowExecutableStore, schedulerWorkQueue, idGenerator, TimeProvider.System)
    {
    }

    public WorkflowStartSchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableStore);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workflowExecutableStore = workflowExecutableStore;
        _schedulerWorkQueue = schedulerWorkQueue;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
    }

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.Start;
    }

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        var startPayload = DeserializeStartPayload(workItem);
        var executable = await _workflowExecutableStore.FindAsync(startPayload.RequestedArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(startPayload.RequestedArtifactId);

        ValidatePinnedExecutable(workItem, startPayload.PinnedExecutable, executable.Identity);

        if (executable.StartNodeIds.Count == 0)
            throw new InvalidOperationException($"Workflow executable artifact '{executable.Identity.ArtifactId}' does not declare any start nodes.");

        var now = _timeProvider.GetUtcNow();
        var startNodeIds = executable.StartNodeIds.ToArray();
        var postCommitIntents = new List<RuntimePostCommitIntent>(startNodeIds.Length);
        for (var index = 0; index < startNodeIds.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startNodeId = startNodeIds[index];
            var startNodeWorkItem = NewStartNodeWorkItem(workItem, startPayload.PinnedExecutable, startNodeId, index + 1, now);
            postCommitIntents.Add(NewStartNodePostCommitIntent(workItem, startNodeWorkItem, startNodeId, index + 1, now));
        }

        var checkpointWorkItem = NewWorkflowStartedCheckpointWorkItem(workItem, startPayload.PinnedExecutable, postCommitIntents, now);
        await _schedulerWorkQueue.EnqueueAsync(checkpointWorkItem, cancellationToken);
    }

    private static WorkflowExecutionStartCommandPayload DeserializeStartPayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("Start scheduler work item requires a start command payload.");

        try
        {
            return payload.Deserialize<WorkflowExecutionStartCommandPayload>()
                   ?? throw new InvalidOperationException("Start scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidOperationException("Start scheduler work item payload is not a valid start command payload.", exception);
        }
    }

    private static void ValidatePinnedExecutable(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutableIdentity pinnedExecutable,
        WorkflowExecutableIdentity loadedExecutable)
    {
        if (WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(loadedExecutable, pinnedExecutable))
            return;

        throw new InvalidOperationException(
            $"Start scheduler work item '{workItem.WorkItemId}' loaded executable artifact '{WorkflowExecutableIdentityComparer.Format(loadedExecutable)}' " +
            $"but pinned executable artifact '{WorkflowExecutableIdentityComparer.Format(pinnedExecutable)}'.");
    }

    private RuntimeSchedulerWorkItem NewStartNodeWorkItem(
        RuntimeSchedulerWorkItem startWorkItem,
        WorkflowExecutableIdentity pinnedExecutable,
        string startNodeId,
        int index,
        DateTimeOffset now)
    {
        var payload = new RuntimeScheduleActivityCommandPayload(
            pinnedExecutable,
            startNodeId,
            _idGenerator.NewActivityExecutionId(),
            RuntimeScheduleActivityCommandPayload.WorkflowStartReason);

        return new RuntimeSchedulerWorkItem(
            workItemId: $"{startWorkItem.WorkItemId}:schedule:{startNodeId}",
            workflowExecutionId: startWorkItem.WorkflowExecutionId,
            commandId: $"{startWorkItem.CommandId}:schedule:{startNodeId}",
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            envelopeId: startWorkItem.EnvelopeId,
            idempotencyKey: $"{startWorkItem.IdempotencyKey}:schedule:{startNodeId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: startWorkItem.Sequence is { } sequence ? sequence + index + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: startWorkItem.CommandMetadata,
            envelopeMetadata: startWorkItem.EnvelopeMetadata);
    }

    private RuntimePostCommitIntent NewStartNodePostCommitIntent(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeSchedulerWorkItem startNodeWorkItem,
        string startNodeId,
        int index,
        DateTimeOffset now)
    {
        return new RuntimePostCommitIntent(
            intentId: $"{startWorkItem.WorkItemId}:postcommit:schedule:{index}:{startNodeId}",
            workflowExecutionId: startWorkItem.WorkflowExecutionId,
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            recordedAt: now,
            activityExecutionId: null,
            idempotencyKey: startNodeWorkItem.IdempotencyKey,
            payload: JsonSerializer.SerializeToElement(startNodeWorkItem),
            metadata: new Dictionary<string, string>
            {
                ["runtime.sourceSchedulerWorkItemId"] = startWorkItem.WorkItemId,
                ["runtime.schedulerWorkItemId"] = startNodeWorkItem.WorkItemId,
                ["runtime.startExecutableNodeId"] = startNodeId
            });
    }

    private RuntimeSchedulerWorkItem NewWorkflowStartedCheckpointWorkItem(
        RuntimeSchedulerWorkItem startWorkItem,
        WorkflowExecutableIdentity pinnedExecutable,
        IReadOnlyCollection<RuntimePostCommitIntent> postCommitIntents,
        DateTimeOffset now)
    {
        var payload = new RuntimeCheckpointCommandPayload(
            pinnedExecutable,
            RuntimeCheckpointNames.WorkflowStarted,
            activityExecutionIds: [],
            RuntimeCheckpointCommandPayload.WorkflowStartReason,
            postCommitIntents);

        return new RuntimeSchedulerWorkItem(
            workItemId: $"{startWorkItem.WorkItemId}:checkpoint:{RuntimeCheckpointNames.WorkflowStarted}",
            workflowExecutionId: startWorkItem.WorkflowExecutionId,
            commandId: $"{startWorkItem.CommandId}:checkpoint:{RuntimeCheckpointNames.WorkflowStarted}",
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            envelopeId: startWorkItem.EnvelopeId,
            idempotencyKey: $"{startWorkItem.IdempotencyKey}:checkpoint:{RuntimeCheckpointNames.WorkflowStarted}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: startWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: startWorkItem.CommandMetadata,
            envelopeMetadata: startWorkItem.EnvelopeMetadata);
    }
}
