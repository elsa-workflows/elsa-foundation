using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowStartSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowStartSchedulerWorkHandler);

    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly TimeProvider _timeProvider;

    public WorkflowStartSchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue)
        : this(workflowExecutableStore, schedulerWorkQueue, TimeProvider.System)
    {
    }

    public WorkflowStartSchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableStore);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workflowExecutableStore = workflowExecutableStore;
        _schedulerWorkQueue = schedulerWorkQueue;
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

        var index = 0;
        foreach (var startNodeId in executable.StartNodeIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            await EnqueueStartNodeAsync(workItem, startPayload.PinnedExecutable, startNodeId, index, cancellationToken);
        }
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

    private async ValueTask EnqueueStartNodeAsync(
        RuntimeSchedulerWorkItem startWorkItem,
        WorkflowExecutableIdentity pinnedExecutable,
        string startNodeId,
        int index,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var payload = new RuntimeScheduleActivityCommandPayload(
            pinnedExecutable,
            startNodeId,
            RuntimeScheduleActivityCommandPayload.WorkflowStartReason);

        var workItem = new RuntimeSchedulerWorkItem(
            workItemId: $"{startWorkItem.WorkItemId}:schedule:{startNodeId}",
            workflowExecutionId: startWorkItem.WorkflowExecutionId,
            commandId: $"{startWorkItem.CommandId}:schedule:{startNodeId}",
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            envelopeId: startWorkItem.EnvelopeId,
            idempotencyKey: $"{startWorkItem.IdempotencyKey}:schedule:{startNodeId}",
            enqueuedAt: now,
            recordedAt: now,
            sequence: startWorkItem.Sequence is { } sequence ? sequence + index : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: startWorkItem.CommandMetadata,
            envelopeMetadata: startWorkItem.EnvelopeMetadata);

        await _schedulerWorkQueue.EnqueueAsync(workItem, cancellationToken);
    }
}
