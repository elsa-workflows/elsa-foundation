using System.Globalization;
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

        SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(workItem, startPayload.PinnedExecutable, executable.Identity);

        var dispatchId = workItem.CommandMetadata.GetValueOrDefault(RuntimeMetadataKeys.WorkflowDispatchId);
        var now = dispatchId is null ? _timeProvider.GetUtcNow() : workItem.EnqueuedAt;
        var rootActivityId = executable.RootActivity.ExecutableNodeId;
        var commandMetadata = CreateWorkflowStartCommandMetadata(workItem.CommandMetadata, now);
        var rootActivityWorkItem = NewRootActivityWorkItem(workItem, startPayload.PinnedExecutable, rootActivityId, dispatchId, now, commandMetadata);
        var postCommitIntents = new[] { NewRootActivityPostCommitIntent(workItem, rootActivityWorkItem, rootActivityId, now) };

        var checkpointWorkItem = NewWorkflowStartedCheckpointWorkItem(workItem, startPayload, postCommitIntents, now, commandMetadata);
        await _schedulerWorkQueue.EnqueueAsync(checkpointWorkItem, cancellationToken);
    }

    private static WorkflowExecutionStartCommandPayload DeserializeStartPayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkHandlerHelpers.DeserializePayload(
            workItem,
            requiresPayloadMessage: "Start scheduler work item requires a start command payload.",
            resolvedToNullMessage: "Start scheduler work item payload resolved to null.",
            invalidPayloadMessage: "Start scheduler work item payload is not a valid start command payload.",
            deserialize: static (_, payload) => payload.Deserialize<WorkflowExecutionStartCommandPayload>(),
            // #412 (Start exception-masking): narrowed to a ParamName whitelist mirroring the sibling handlers.
            // Previously ANY ArgumentException raised during deserialization was misreported as "invalid payload,"
            // masking unrelated bugs. Only the payload's own constructor-validation ParamNames are treated as a
            // payload-validation failure; an unrelated ArgumentException now propagates unwrapped.
            isPayloadValidationException: static exception =>
                exception is JsonException or NotSupportedException ||
                exception is ArgumentException argumentException && IsStartPayloadValidationException(argumentException));

    /// <summary>
    /// Whitelist of the <see cref="WorkflowExecutionStartCommandPayload"/> constructor's own validation
    /// <c>ParamName</c>s. Only these are classified as payload-validation failures (wrapped as an invalid-payload
    /// <see cref="InvalidOperationException"/>); any other <see cref="ArgumentException"/> propagates so an
    /// unrelated bug is not masked. Public so the narrowing boundary can be characterized without IVT
    /// (constitution §2.23.3), matching the sibling handlers' predicate shape.
    /// </summary>
    public static bool IsStartPayloadValidationException(ArgumentException exception) =>
        exception.ParamName is
            "pinnedExecutable" or
            "requestedArtifactId" or
            "parentWorkflowExecutionId" or
            "correlationId" or
            "tenantId" or
            "dispatchNestingDepth";

    private RuntimeSchedulerWorkItem NewRootActivityWorkItem(
        RuntimeSchedulerWorkItem startWorkItem,
        WorkflowExecutableIdentity pinnedExecutable,
        string rootActivityId,
        string? dispatchId,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string> commandMetadata)
    {
        var activityExecutionId = dispatchId is null
            ? _idGenerator.NewActivityExecutionId()
            : $"{dispatchId}:activity:root";
        var attempt = new ActivityExecutionAttemptLineage(1, activityExecutionId, null);
        var provenance = ActivitySchedulingProvenance.From(
            startWorkItem.WorkflowExecutionId,
            parentActivityExecutionId: null,
            schedulingActivityExecutionId: null,
            branchId: null,
            iterationId: null,
            executionPathId: null,
            executionScopeId: null,
            schedulingCause: RuntimeScheduleActivityCommandPayload.WorkflowStartReason,
            attempt: attempt);
        var payload = new RuntimeScheduleActivityCommandPayload(
            pinnedExecutable,
            rootActivityId,
            activityExecutionId,
            RuntimeScheduleActivityCommandPayload.WorkflowStartReason,
            schedulingProvenance: provenance);

        return new RuntimeSchedulerWorkItem(
            workItemId: $"{startWorkItem.WorkItemId}:schedule:{rootActivityId}",
            workflowExecutionId: startWorkItem.WorkflowExecutionId,
            commandId: $"{startWorkItem.CommandId}:schedule:{rootActivityId}",
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            envelopeId: startWorkItem.EnvelopeId,
            idempotencyKey: $"{startWorkItem.IdempotencyKey}:schedule:{rootActivityId}",
            enqueuedAt: now,
            recordedAt: startWorkItem.RecordedAt,
            sequence: startWorkItem.Sequence is { } sequence ? sequence + 2 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: commandMetadata,
            envelopeMetadata: startWorkItem.EnvelopeMetadata,
            executionScopeId: null,
            attempt: attempt);
    }

    private RuntimePostCommitIntent NewRootActivityPostCommitIntent(
        RuntimeSchedulerWorkItem startWorkItem,
        RuntimeSchedulerWorkItem rootActivityWorkItem,
        string rootActivityId,
        DateTimeOffset now)
    {
        return new RuntimePostCommitIntent(
            intentId: $"{startWorkItem.WorkItemId}:postcommit:schedule:root:{rootActivityId}",
            workflowExecutionId: startWorkItem.WorkflowExecutionId,
            kind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork,
            recordedAt: now,
            activityExecutionId: null,
            idempotencyKey: rootActivityWorkItem.IdempotencyKey,
            payload: JsonSerializer.SerializeToElement(rootActivityWorkItem),
            metadata: new Dictionary<string, string>
            {
                ["runtime.sourceSchedulerWorkItemId"] = startWorkItem.WorkItemId,
                ["runtime.schedulerWorkItemId"] = rootActivityWorkItem.WorkItemId,
                ["runtime.rootExecutableNodeId"] = rootActivityId
            });
    }

    private RuntimeSchedulerWorkItem NewWorkflowStartedCheckpointWorkItem(
        RuntimeSchedulerWorkItem startWorkItem,
        WorkflowExecutionStartCommandPayload startPayload,
        IReadOnlyCollection<RuntimePostCommitIntent> postCommitIntents,
        DateTimeOffset now,
        IReadOnlyDictionary<string, string> commandMetadata)
    {
        var payload = new RuntimeCheckpointCommandPayload(
            startPayload.PinnedExecutable,
            RuntimeCheckpointNames.WorkflowStarted,
            activityExecutionIds: [],
            RuntimeCheckpointCommandPayload.WorkflowStartReason,
            postCommitIntents,
            seedVariables: startPayload.Variables,
            seedInputs: startPayload.Inputs,
            seedStimulusInput: startPayload.StimulusInput,
            seedTriggerNodeId: startPayload.TriggerNodeId,
            runKind: startPayload.RunKind,
            pinnedSource: startPayload.PinnedSource,
            parentWorkflowExecutionId: startPayload.ParentWorkflowExecutionId,
            correlationId: startPayload.CorrelationId,
            tenantId: startPayload.TenantId,
            partition: startPayload.Partition,
            authority: startPayload.Authority,
            dispatchNestingDepth: startPayload.DispatchNestingDepth);

        return new RuntimeSchedulerWorkItem(
            workItemId: $"{startWorkItem.WorkItemId}:checkpoint:{RuntimeCheckpointNames.WorkflowStarted}",
            workflowExecutionId: startWorkItem.WorkflowExecutionId,
            commandId: $"{startWorkItem.CommandId}:checkpoint:{RuntimeCheckpointNames.WorkflowStarted}",
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            envelopeId: startWorkItem.EnvelopeId,
            idempotencyKey: $"{startWorkItem.IdempotencyKey}:checkpoint:{RuntimeCheckpointNames.WorkflowStarted}",
            enqueuedAt: now,
            // The checkpoint is causally after the currently dispatched start item. A delayed outbox
            // redelivery may preserve an older semantic start time in `now`; using the source item's
            // durable queue-recorded time prevents the follow-up from sorting ahead of the item whose
            // handler is still awaiting its ack-delete.
            recordedAt: startWorkItem.RecordedAt,
            sequence: startWorkItem.Sequence is { } sequence ? sequence + 1 : null,
            payload: JsonSerializer.SerializeToElement(payload),
            commandMetadata: commandMetadata,
            envelopeMetadata: startWorkItem.EnvelopeMetadata);
    }

    private static IReadOnlyDictionary<string, string> CreateWorkflowStartCommandMetadata(
        IReadOnlyDictionary<string, string> commandMetadata,
        DateTimeOffset startedAt)
    {
        var metadata = commandMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.WorkflowStartedAt] = startedAt.ToString("O", CultureInfo.InvariantCulture);
        return RuntimeModelMetadata.Snapshot(metadata);
    }
}
