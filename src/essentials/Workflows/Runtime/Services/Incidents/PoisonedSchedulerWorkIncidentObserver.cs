using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Services.Incidents;

/// <summary>
/// Projects parked scheduler poison records into blocking incidents so a dispatch-time fault is observable.
/// Without this, a work item that faults during dispatch (before the activity fault path can record its own
/// incident) is recorded only in <see cref="IWorkflowSchedulerPoisonStore"/>: the workflow stays Running, the
/// activity stays Scheduled, and neither the incidents API nor the Studio timeline surfaces anything.
///
/// <para>Runs as a drain observer <b>before</b> <see cref="BlockingIncidentWorkflowFaultObserver"/>. It records a
/// system-authored <c>WaitForIntervention</c> outcome, so the downstream fault observer preserves the workflow's existing
/// lifecycle state while operators can inspect the durable poison incident.</para>
///
/// <para>Only <see cref="RuntimeSchedulerPoisonDisposition.Poisoned"/> records are surfaced — a
/// <see cref="RuntimeSchedulerPoisonDisposition.RetryScheduled"/> record is still being re-driven by the retry
/// policy and must not fault the workflow. Incident ids are deterministic per work item and an existing incident
/// is never overwritten, so an operator-resolved incident stays resolved and repeated drains are idempotent.</para>
///
/// <para>This is the tail of the <b>handler</b>-fault path only; an activity fault takes an entirely different route and
/// ends in an authored incident strategy. Both are mapped in <c>docs/runtime-fault-behavior.md</c>.</para>
/// </summary>
public sealed class PoisonedSchedulerWorkIncidentObserver : IWorkflowSchedulerDrainObserver
{
    /// <summary>The <see cref="IncidentState.FailureType"/> of incidents projected from poison records.</summary>
    public const string IncidentFailureType = "SchedulerWorkPoisoned";

    private readonly IWorkflowSchedulerPoisonStore _poisonStore;
    private readonly IIncidentStateStore _incidentStateStore;
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PoisonedSchedulerWorkIncidentObserver> _logger;
    private readonly IActivityExecutionStateStore? _activityExecutionStateStore;
    private readonly IRuntimeActivityExecutionInspectionAccumulator? _inspectionAccumulator;

    public PoisonedSchedulerWorkIncidentObserver(
        IWorkflowSchedulerPoisonStore poisonStore,
        IIncidentStateStore incidentStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider,
        ILogger<PoisonedSchedulerWorkIncidentObserver>? logger = null)
        : this(
            poisonStore,
            incidentStateStore,
            checkpointCommitter,
            timeProvider,
            activityExecutionStateStore: null,
            inspectionAccumulator: null,
            logger: logger)
    {
    }

    public PoisonedSchedulerWorkIncidentObserver(
        IWorkflowSchedulerPoisonStore poisonStore,
        IIncidentStateStore incidentStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider,
        IActivityExecutionStateStore? activityExecutionStateStore,
        IRuntimeActivityExecutionInspectionAccumulator? inspectionAccumulator,
        ILogger<PoisonedSchedulerWorkIncidentObserver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(poisonStore);
        ArgumentNullException.ThrowIfNull(incidentStateStore);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _poisonStore = poisonStore;
        _incidentStateStore = incidentStateStore;
        _checkpointCommitter = checkpointCommitter;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<PoisonedSchedulerWorkIncidentObserver>.Instance;
        _activityExecutionStateStore = activityExecutionStateStore;
        _inspectionAccumulator = inspectionAccumulator;
    }

    /// <summary>
    /// The deterministic incident id projected for a poisoned scheduler work item. The work-item id is folded into a
    /// fixed-length fingerprint (#923) so the id stays comfortably within the 128-char <c>by-incident-id</c>
    /// projection column (GW-PHYSICAL-037, #922) regardless of how deep in the workflow the poisoned hop was. The
    /// derivation is deterministic, so the existing-incident dedupe below still recognizes an already-recorded (or
    /// operator-resolved) incident for the same work item. The human-readable work-item id is preserved in the
    /// incident message and metadata, not the id.
    /// </summary>
    public static string IncidentId(string workItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        return $"incident:{RuntimeChainId.Fingerprint(workItemId)}:scheduler-poison";
    }

    public async ValueTask OnDrainedAsync(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerDrainResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(result);

        // A poison record is only ever written by a drain that produced a Faulted item result, so a fault-free
        // drain needs no poison-store lookup. (A process crash between that drain and this notification leaves
        // the record unsurfaced until the workflow's next faulted drain; the record itself stays inspectable.)
        if (!result.StoppedOnFault)
            return;

        var workflowExecutionId = result.WorkflowExecutionId;
        var poisonRecords = await _poisonStore.ListAsync(workflowExecutionId, cancellationToken);

        foreach (var record in poisonRecords.OrderBy(item => item.WorkItemId, StringComparer.Ordinal))
        {
            if (record.Disposition != RuntimeSchedulerPoisonDisposition.Poisoned)
                continue;

            // Recording an incident is best-effort surfacing of an already-durable poison record: it must never
            // sink the drain (and therefore the test-run / dispatch API call, #922). A persistence failure here —
            // e.g. a projection-column overflow — is caught and logged with the original poison record so the
            // underlying fault stays diagnosable instead of being masked by the storage error, and the drain
            // continues to the remaining records and downstream observers.
            try
            {
                var incidentId = IncidentId(record.WorkItemId);
                var existing = await _incidentStateStore.FindAsync(workflowExecutionId, incidentId, cancellationToken);
                if (existing is not null)
                    continue;

                await CommitIncidentAsync(workflowExecutionId, incidentId, record, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to record a blocking incident for poisoned scheduler work item {WorkItemId} of workflow execution {WorkflowExecutionId} " +
                    "(command {CommandKind}, handler {HandlerName}, {FailureCount} failure(s)); the original poison fault was {FaultType}: {FaultMessage} " +
                    "(inner fault: {InnerFault}). The poison record remains durable in the poison store; continuing the drain.",
                    record.WorkItemId,
                    workflowExecutionId,
                    record.CommandKind,
                    record.HandlerName,
                    record.FailureCount,
                    record.Fault.ExceptionType,
                    record.Fault.Message,
                    record.InnerFault?.ToSummaryString() ?? "none");
            }
        }
    }

    private async ValueTask CommitIncidentAsync(
        string workflowExecutionId,
        string incidentId,
        RuntimeSchedulerPoisonRecord record,
        CancellationToken cancellationToken)
    {
        var occurredAt = _timeProvider.GetUtcNow();
        var metadata = NewMetadata(incidentId, record);
        var activityExecutionId = ValueOrNull(metadata, RuntimeMetadataKeys.ActivityExecutionId);
        var executableNodeId = ValueOrNull(metadata, RuntimeMetadataKeys.ExecutableNodeId);
        var incident = new IncidentState(
            incidentId: incidentId,
            workflowExecutionId: workflowExecutionId,
            activityExecutionId: activityExecutionId,
            executableNodeId: executableNodeId,
            severity: IncidentSeverity.Critical,
            status: IncidentStatus.Blocking,
            resolutionOutcome: new IncidentResolutionOutcome(
                IncidentResolutionActionKinds.WaitForIntervention,
                occurredAt,
                strategy: null,
                systemSource: IncidentResolutionSystemSources.PoisonedSchedulerWork),
            failureType: IncidentFailureType,
            message: $"Scheduler work item '{record.WorkItemId}' ({record.CommandKind}) was poisoned during dispatch " +
                     $"by handler '{record.HandlerName}' after {record.FailureCount} failure(s): {record.Fault.ToSummaryString()}" +
                     (record.InnerFault is null ? "" : $" ---> {record.InnerFault.ToSummaryString()}"),
            createdAt: occurredAt,
            resolvedAt: null,
            metadata: metadata);

        // Fold the work-item id into a fixed-length fingerprint so these ledger ids stay bounded too (#923); the
        // pair (workflowExecutionId, work-item fingerprint) keeps them deterministic and unique per poison record.
        var workItemFingerprint = RuntimeChainId.Fingerprint(record.WorkItemId);
        var checkpointId = $"checkpoint:{workflowExecutionId}:scheduler-poison:{workItemFingerprint}";
        var activityStateChanges = new List<RuntimeStateChange<ActivityExecutionState>>();
        var activityInspectionChanges = new List<RuntimeStateChange<ActivityExecutionInspectionProjection>>();
        var checkpointActivityExecutionIds = new List<string>();
        if (activityExecutionId is not null && _activityExecutionStateStore is not null)
        {
            try
            {
                var activityState = await _activityExecutionStateStore.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken);
                if (activityState is not null &&
                    (executableNodeId is null || StringComparer.Ordinal.Equals(activityState.Execution.ExecutableNodeId, executableNodeId)))
                {
                    var associatedState = activityState.IncidentIds.Contains(incidentId, StringComparer.Ordinal)
                        ? activityState
                        : activityState with { IncidentIds = activityState.IncidentIds.Append(incidentId).ToArray() };
                    var inputFailure = BuildInputFailureSnapshot(incident, occurredAt);
                    var projection = _inspectionAccumulator is null
                        ? null
                        : await _inspectionAccumulator.BuildProjectionAsync(
                            associatedState,
                            checkpointId,
                            occurredAt,
                            incidents: [ActivityExecutionIncidentSummary.From(incident)],
                            valueSnapshots: inputFailure is null ? [] : [inputFailure],
                            metadata: metadata,
                            cancellationToken: cancellationToken);

                    checkpointActivityExecutionIds.Add(activityExecutionId);
                    activityStateChanges.Add(new RuntimeStateChange<ActivityExecutionState>(
                        StateId: activityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: associatedState,
                        Metadata: metadata));
                    if (projection is not null)
                        activityInspectionChanges.Add(new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                            StateId: activityExecutionId,
                            Operation: RuntimeStateChangeOperation.Upsert,
                            State: projection,
                            Metadata: metadata));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // An optional activity-side read projection must not prevent the durable poison incident itself from
                // being committed. The incident retains its payload-derived address if that projection is unavailable.
                _logger.LogWarning(
                    exception,
                    "Failed to associate poisoned scheduler work item {WorkItemId} with activity execution {ActivityExecutionId}; recording the incident without an activity projection.",
                    record.WorkItemId,
                    activityExecutionId);
            }
        }
        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{workflowExecutionId}:scheduler-poison:{workItemFingerprint}",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.IncidentRecorded,
                WorkflowExecutionId: workflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: checkpointActivityExecutionIds,
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: activityStateChanges,
                bookmarks: [],
                durableValues: [],
                incidents:
                [
                    new RuntimeStateChange<IncidentState>(
                        StateId: incidentId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: incident,
                        Metadata: metadata)
                ],
                operational: [],
                activityExecutionInspections: activityInspectionChanges),
            PostCommitIntents: [],
            Metadata: metadata);

        await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private static Dictionary<string, string> NewMetadata(string incidentId, RuntimeSchedulerPoisonRecord record)
    {
        var metadata = record.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[RuntimeMetadataKeys.IncidentId] = incidentId;
        metadata[RuntimeMetadataKeys.CheckpointReason] = IncidentFailureType;
        metadata[RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory;
        metadata[RuntimeMetadataKeys.SchedulerWorkItemId] = record.WorkItemId;
        metadata[RuntimeMetadataKeys.CommandKind] = record.CommandKind.ToString();
        metadata[RuntimeMetadataKeys.SchedulerPoisonHandlerName] = record.HandlerName;
        metadata[RuntimeMetadataKeys.SchedulerPoisonFailureCount] = record.FailureCount.ToString();
        metadata[RuntimeMetadataKeys.FaultType] = record.Fault.ExceptionType;
        metadata[RuntimeMetadataKeys.FaultMessage] = record.Fault.Message;
        if (!string.IsNullOrWhiteSpace(record.Fault.StackTrace))
            metadata[RuntimeMetadataKeys.FaultStackTrace] = record.Fault.StackTrace;
        if (record.InnerFault is { } innerFault)
        {
            metadata[RuntimeMetadataKeys.FaultInnerType] = innerFault.ExceptionType;
            metadata[RuntimeMetadataKeys.FaultInnerMessage] = innerFault.Message;
        }
        return metadata;
    }

    private static ActivityExecutionInspectionValueSnapshot? BuildInputFailureSnapshot(
        IncidentState incident,
        DateTimeOffset capturedAt)
    {
        if (!incident.Metadata.TryGetValue(RuntimeMetadataKeys.InputFailureCode, out var failureCode) ||
            failureCode is not (ExpressionInputFailureException.ExpressionEvaluationFailed or ExpressionInputFailureException.InputMaterializationFailed) ||
            !incident.Metadata.TryGetValue(RuntimeMetadataKeys.InputKey, out var inputKey) ||
            string.IsNullOrWhiteSpace(inputKey))
            return null;

        var phase = incident.Metadata.GetValueOrDefault(RuntimeMetadataKeys.InputEvaluationPhase);
        var language = incident.Metadata.GetValueOrDefault(RuntimeMetadataKeys.ExpressionLanguage);
        var snapshotMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.IncidentId] = incident.IncidentId,
            [RuntimeMetadataKeys.InputFailureCode] = failureCode
        };
        if (!string.IsNullOrWhiteSpace(language))
            snapshotMetadata[RuntimeMetadataKeys.ExpressionLanguage] = language;
        if (!string.IsNullOrWhiteSpace(phase))
            snapshotMetadata[RuntimeMetadataKeys.InputEvaluationPhase] = phase;

        var message = incident.Metadata.GetValueOrDefault(RuntimeMetadataKeys.FaultInnerMessage);
        if (string.IsNullOrWhiteSpace(message))
            message = "Input evaluation failed before a value snapshot was committed.";

        return new ActivityExecutionInspectionValueSnapshot(
            Name: inputKey,
            Subject: ActivityExecutionInspectionValueSubject.ActivityInput,
            CaptureMode: RuntimePayloadCaptureMode.MetadataOnly,
            Type: null,
            CapturedAt: capturedAt,
            Payload: null,
            CaptureReason: "Input evaluation failed before a value snapshot was committed.",
            IsSensitive: IsProtectedFailure(incident),
            Metadata: snapshotMetadata,
            InputKey: inputKey,
            EvaluationId: incident.IncidentId,
            Phase: phase,
            Failure: new RuntimeInputEvaluationFailure(failureCode, message, incident.IncidentId));
    }

    private static bool IsProtectedFailure(IncidentState incident) =>
        !incident.Metadata.TryGetValue(RuntimeMetadataKeys.FaultInnerType, out var innerType) ||
        StringComparer.Ordinal.Equals(innerType, typeof(RedactedPortableExpressionException).FullName);

    private static string? ValueOrNull(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
