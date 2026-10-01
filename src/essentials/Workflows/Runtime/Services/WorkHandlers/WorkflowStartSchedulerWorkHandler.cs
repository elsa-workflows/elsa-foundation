using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Services.WorkHandlers;

public sealed class WorkflowStartSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowStartSchedulerWorkHandler);

    private readonly IWorkflowExecutableStore _workflowExecutableStore;
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly IRuntimeExecutionIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowExecutableReader? _executableReader;
    private readonly IIncidentStrategyCatalog? _incidentStrategyCatalog;
    private readonly RuntimeCheckpointCommitter? _checkpointCommitter;
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly ILogger<WorkflowStartSchedulerWorkHandler> _logger;

    public WorkflowStartSchedulerWorkHandler(
        IWorkflowExecutableStore workflowExecutableStore,
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        IRuntimeExecutionIdGenerator idGenerator,
        TimeProvider timeProvider,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IWorkflowExecutableReader? executableReader = null,
        IIncidentStrategyCatalog? incidentStrategyCatalog = null,
        RuntimeCheckpointCommitter? checkpointCommitter = null,
        ILogger<WorkflowStartSchedulerWorkHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutableStore);
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(idGenerator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);

        _workflowExecutableStore = workflowExecutableStore;
        _schedulerWorkQueue = schedulerWorkQueue;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
        _executableReader = executableReader;
        _incidentStrategyCatalog = incidentStrategyCatalog;
        _checkpointCommitter = checkpointCommitter;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _logger = logger ?? NullLogger<WorkflowStartSchedulerWorkHandler>.Instance;
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
        if (await HasStartedAsync(workItem, startPayload, cancellationToken))
            return;

        var executable = await PinnedExecutableRead.FindAsync(_executableReader, _workflowExecutableStore, startPayload.RequestedArtifactId, cancellationToken);
        if (executable is null)
            throw new WorkflowExecutableNotFoundException(startPayload.RequestedArtifactId);

        SchedulerWorkHandlerHelpers.ValidatePinnedExecutable(workItem, startPayload.PinnedExecutable, executable.Identity);

        var dispatchId = workItem.CommandMetadata.GetValueOrDefault(RuntimeMetadataKeys.WorkflowDispatchId);
        var now = dispatchId is null ? _timeProvider.GetUtcNow() : workItem.EnqueuedAt;
        if (_incidentStrategyCatalog is not null &&
            !_incidentStrategyCatalog.TryGet(executable.IncidentStrategy, out _))
        {
            if (_checkpointCommitter is null)
                throw new InvalidOperationException("The runtime cannot record a missing incident strategy without a checkpoint committer.");

            await CommitMissingIncidentStrategyAsync(workItem, executable, workItem.EnqueuedAt, cancellationToken);
            return;
        }

        var rootActivityId = executable.RootActivity.ExecutableNodeId;
        var commandMetadata = CreateWorkflowStartCommandMetadata(workItem.CommandMetadata, now);
        var rootActivityWorkItem = NewRootActivityWorkItem(workItem, startPayload.PinnedExecutable, rootActivityId, dispatchId, now, commandMetadata);
        var postCommitIntents = new[] { NewRootActivityPostCommitIntent(workItem, rootActivityWorkItem, rootActivityId, now) };

        var checkpointWorkItem = NewWorkflowStartedCheckpointWorkItem(workItem, startPayload, postCommitIntents, now, commandMetadata);
        await _schedulerWorkQueue.EnqueueAsync(checkpointWorkItem, cancellationToken);
    }

    /// <summary>
    /// Whether this execution already has state, which makes this Start a duplicate that converges as a no-op (#2195).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every start names its execution before it is dispatched, and a keyed or DispatchWorkflow start names it
    /// deterministically, so a second delivery of the same start reaches this mailbox as another Start for the same id.
    /// The dispatcher answers it as a duplicate when the execution already exists, but that check and its enqueue are not
    /// atomic: a duplicate that passed it while the first start was still in flight arrives here after the first start's
    /// WorkflowStarted checkpoint committed. Running it would rebuild that checkpoint with a fresh root activity and
    /// collide with the committed one, faulting a workflow that is running correctly.
    /// </para>
    /// <para>
    /// Nothing legitimate starts an execution twice. The WorkflowStarted checkpoint is the only writer that brings a
    /// started execution's state into being, and it runs after this handler, so a start being redelivered after a crash
    /// finds no state and runs as before. The faulter that brings a refused child start's state into being does so
    /// because that start can never run; a later delivery of it converges here too.
    /// </para>
    /// </remarks>
    private async ValueTask<bool> HasStartedAsync(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutionStartCommandPayload startPayload,
        CancellationToken cancellationToken)
    {
        if (await _workflowExecutionStateStore.FindAsync(workItem.WorkflowExecutionId, cancellationToken) is not { } existing)
            return false;

        if (StringComparer.Ordinal.Equals(existing.PinnedExecutable.ArtifactId, startPayload.PinnedExecutable.ArtifactId))
        {
            _logger.LogInformation(
                new EventId(68115, "WorkflowStartConvergedOnExistingExecution"),
                "Workflow start {WorkItemId} found workflow execution {WorkflowExecutionId} already started; the duplicate start converged without running again",
                workItem.WorkItemId,
                workItem.WorkflowExecutionId);
        }
        else
        {
            // Not a redelivery: another workflow already owns this execution id. Running would overwrite it, and failing
            // the work item would record its poison against the execution that owns the id. Leave it alone, loudly.
            _logger.LogWarning(
                new EventId(68116, "WorkflowStartRefusedForForeignExecution"),
                "Workflow start {WorkItemId} for artifact {ArtifactId} was refused: workflow execution {WorkflowExecutionId} already exists for artifact {ExistingArtifactId}",
                workItem.WorkItemId,
                startPayload.PinnedExecutable.ArtifactId,
                workItem.WorkflowExecutionId,
                existing.PinnedExecutable.ArtifactId);
        }

        return true;
    }

    private async ValueTask CommitMissingIncidentStrategyAsync(
        RuntimeSchedulerWorkItem workItem,
        WorkflowExecutable executable,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var strategy = executable.IncidentStrategy;
        var fingerprint = RuntimeChainId.Fingerprint($"{strategy.Alias}\n{strategy.Version}");
        var incidentId = $"incident:{workItem.WorkflowExecutionId}:missing-strategy:{fingerprint}";
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.CheckpointReason] = IncidentResolutionSystemSources.MissingStrategyImplementation,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory
        };
        var outcome = new IncidentResolutionOutcome(
            IncidentResolutionActionKinds.WaitForIntervention,
            occurredAt,
            strategy,
            IncidentResolutionSystemSources.MissingStrategyImplementation,
            new Dictionary<string, string>());
        var incident = new IncidentState(
            incidentId,
            workItem.WorkflowExecutionId,
            null,
            executable.RootActivity.ExecutableNodeId,
            IncidentSeverity.Critical,
            IncidentStatus.Blocking,
            outcome,
            "MissingIncidentStrategyImplementation",
            $"Pinned incident strategy '{strategy}' is not available in this runtime deployment.",
            occurredAt,
            null,
            new Dictionary<string, string>());
        var commit = new RuntimeCheckpointCommit(
            $"commit:{workItem.WorkflowExecutionId}:missing-strategy:{fingerprint}",
            new RuntimeCheckpoint(
                $"checkpoint:{workItem.WorkflowExecutionId}:missing-strategy:{fingerprint}",
                RuntimeCheckpointNames.IncidentResolutionBatchApplied,
                workItem.WorkflowExecutionId,
                occurredAt,
                [],
                metadata),
            new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents:
                [
                    new RuntimeStateChange<IncidentState>(
                        incidentId,
                        RuntimeStateChangeOperation.Upsert,
                        incident,
                        metadata)
                ],
                operational: [],
                activityExecutionInspections: []),
            [],
            metadata);
        await _checkpointCommitter!.CommitAsync(commit, cancellationToken);
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
            workItemId: RuntimeChainId.Derive(startWorkItem.WorkItemId, $"schedule:{rootActivityId}"),
            workflowExecutionId: startWorkItem.WorkflowExecutionId,
            commandId: RuntimeChainId.Derive(startWorkItem.CommandId, $"schedule:{rootActivityId}"),
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            envelopeId: startWorkItem.EnvelopeId,
            idempotencyKey: RuntimeChainId.Derive(startWorkItem.IdempotencyKey, $"schedule:{rootActivityId}"),
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
            seedTriggerMetadata: startPayload.TriggerMetadata,
            runKind: startPayload.RunKind,
            pinnedSource: startPayload.PinnedSource,
            parentWorkflowExecutionId: startPayload.ParentWorkflowExecutionId,
            correlationId: startPayload.CorrelationId,
            tenantId: startPayload.TenantId,
            partition: startPayload.Partition,
            authority: startPayload.Authority,
            dispatchNestingDepth: startPayload.DispatchNestingDepth,
            testScope: startPayload.TestScope);

        return new RuntimeSchedulerWorkItem(
            workItemId: RuntimeChainId.Derive(startWorkItem.WorkItemId, $"checkpoint:{RuntimeCheckpointNames.WorkflowStarted}"),
            workflowExecutionId: startWorkItem.WorkflowExecutionId,
            commandId: RuntimeChainId.Derive(startWorkItem.CommandId, $"checkpoint:{RuntimeCheckpointNames.WorkflowStarted}"),
            commandKind: WorkflowExecutionCommandKind.Checkpoint,
            envelopeId: startWorkItem.EnvelopeId,
            idempotencyKey: RuntimeChainId.Derive(startWorkItem.IdempotencyKey, $"checkpoint:{RuntimeCheckpointNames.WorkflowStarted}"),
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
