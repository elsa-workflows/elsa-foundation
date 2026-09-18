using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Services.Incidents;

/// <summary>
/// Faults a workflow execution after a checkpoint rule refused one of its commits during a drain. Every redelivery commits
/// the same refused checkpoint, so without this the execution stays non-terminal at its last accepted checkpoint, and a
/// parent waiting on it as a child never resumes.
/// </summary>
/// <remarks>
/// <para>
/// A refusal reaches the drain in one of two shapes: a scheduler work handler's commit is refused, which the drainer
/// poisons and marks <see cref="RuntimeSchedulerWorkItemResult.CheckpointRuleViolation"/>; or a drain observer's commit is
/// refused, such as the incident strategy's fault, which escapes the drain carrying a
/// <see cref="RuntimeCheckpointCommitValidationException"/>. Nothing else faults an execution here: a concurrency conflict,
/// a lost lease, or an infrastructure failure is not a rule violation, and a retry can still succeed.
/// </para>
/// <para>
/// The fault commit is built from the execution's last accepted state, never from the refused commit, so it cannot offer
/// the refused content again. It carries the execution moved to <see cref="WorkflowExecutionStatus.Faulted"/> and one
/// blocking incident recording the refusal, and nothing else. It passes through the ordinary enrichers, which add a waited
/// child's terminal dispatch and its parent-resume intent exactly as they do for a child's business fault, so the parent
/// resumes through the same child-faulted path.
/// </para>
/// <para>
/// An execution that is already terminal already has its outcome and is left alone. An execution with no accepted state
/// has nothing to fault out of the store, but a refused FIRST commit is exactly that case, and walking away from it is
/// what left a waiting parent waiting forever once the refusal had no other way home (#1799): a start forwarded through
/// distributed placement is acknowledged before the child runs, so the owning node's refusal never reaches the parent's
/// dispatch. A dispatched child is therefore faulted into existence from its start command, which still carries the
/// execution's whole identity. That is deliberately the same terminal shape a child with accepted state gets, so the
/// ordinary enrichers project the dispatch and the parent-resume intent exactly as they do for a child's business fault,
/// and nothing new has to know about refusals. Only a start that declares a parent is synthesized this way; a root start
/// reports its refusal to its caller and gains nothing from a durable execution that never ran.
/// </para>
/// <para>
/// When the fault commit itself fails, that failure is thrown, so an execution that could not be faulted is never
/// reported as handled. A rule that refuses the child's content refuses this commit too, which leaves the start failing
/// as before.
/// </para>
/// </remarks>
public sealed class CheckpointRuleViolationWorkflowFaulter
{
    /// <summary>The <see cref="IncidentState.FailureType"/> of the incident a checkpoint rule violation records.</summary>
    public const string IncidentFailureType = "CheckpointRuleViolation";

    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly IRuntimeFaultCapturePolicy _faultCapturePolicy;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowDispatchStore? _workflowDispatchStore;
    private readonly ILogger<CheckpointRuleViolationWorkflowFaulter> _logger;

    public CheckpointRuleViolationWorkflowFaulter(
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeFaultCapturePolicy faultCapturePolicy,
        TimeProvider timeProvider,
        ILogger<CheckpointRuleViolationWorkflowFaulter>? logger = null,
        IWorkflowDispatchStore? workflowDispatchStore = null)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(faultCapturePolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workflowExecutionStateStore = workflowExecutionStateStore;
        _checkpointCommitter = checkpointCommitter;
        _faultCapturePolicy = faultCapturePolicy;
        _timeProvider = timeProvider;
        _workflowDispatchStore = workflowDispatchStore;
        _logger = logger ?? NullLogger<CheckpointRuleViolationWorkflowFaulter>.Instance;
    }

    /// <summary>The deterministic id of the incident recorded for a checkpoint rule violation in an execution.</summary>
    public static string IncidentId(string workflowExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        return $"incident:{RuntimeChainId.Fingerprint(workflowExecutionId)}:checkpoint-rule-violation";
    }

    /// <summary>
    /// Faults <paramref name="workflowExecutionId"/> when its drain, which returned <paramref name="drainResult"/> or threw
    /// <paramref name="drainFailure"/>, had a commit refused by a checkpoint rule. Must be called while the drain still
    /// holds the execution's lease, so the fault commit is fenced like every other commit of the drain.
    /// </summary>
    /// <param name="envelope">
    /// The command the drain ran, so a start whose first commit was refused can be faulted into existence from it. Without
    /// it such a refusal is left alone.
    /// </param>
    public async ValueTask FaultIfCheckpointRuleViolatedAsync(
        string workflowExecutionId,
        RuntimeSchedulerDrainResult? drainResult,
        Exception? drainFailure,
        WorkflowExecutionCommandEnvelope? envelope = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var violation = drainFailure is not null && RuntimeCheckpointCommitValidationException.IsCauseOf(drainFailure)
            ? _faultCapturePolicy.Capture(drainFailure).ToSummaryString()
            : drainResult?.Items.FirstOrDefault(item => item.CheckpointRuleViolation)?.Error;
        if (violation is null)
            return;

        var occurredAt = _timeProvider.GetUtcNow();
        var stored = await _workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken);
        if (stored is { } workflow && workflow.Status.IsTerminal())
            return;

        var faultable = stored ?? SynthesizeDispatchedStart(envelope, workflowExecutionId, occurredAt);
        if (faultable is null)
            return;

        await _checkpointCommitter.CommitAsync(
            NewFaultCommit(faultable, violation, occurredAt, hasAcceptedState: stored is not null),
            cancellationToken);
        _logger.LogWarning(
            drainFailure,
            "Workflow execution {WorkflowExecutionId} was faulted because a checkpoint rule refused one of its commits; incident {IncidentId} records the refusal: {Violation}",
            workflowExecutionId,
            IncidentId(workflowExecutionId),
            violation);
    }

    /// <summary>
    /// The execution a refused first commit would have created, read back from the start command that carried it. Only a
    /// start that declares a parent qualifies: it is the case whose refusal has nowhere else to go, and the payload carries
    /// every field a dispatch is matched on, so the synthesized child cannot drift from the dispatch that is waiting on it.
    /// </summary>
    private WorkflowExecutionState? SynthesizeDispatchedStart(
        WorkflowExecutionCommandEnvelope? envelope,
        string workflowExecutionId,
        DateTimeOffset occurredAt)
    {
        // Faulting a child into existence is worth doing only because the ordinary enrichers then project its dispatch
        // and resume its parent, and that projection reads the dispatch store through its additive query capability. A
        // store without it projects nothing, so a synthesized child would be a terminal execution nothing can act on,
        // and the durable child-evidence rule would read it as a delivered start and discard the start failure that is
        // the parent's only other way home. Leave that configuration on the path it already had.
        return _workflowDispatchStore is IWorkflowDispatchQueryStore
            ? ReadDispatchedStart(envelope, workflowExecutionId, occurredAt)
            : null;
    }

    private WorkflowExecutionState? ReadDispatchedStart(
        WorkflowExecutionCommandEnvelope? envelope,
        string workflowExecutionId,
        DateTimeOffset occurredAt)
    {
        if (envelope?.Command is not { Kind: WorkflowExecutionCommandKind.Start, Payload: { } payloadElement } ||
            !StringComparer.Ordinal.Equals(envelope.WorkflowExecutionId, workflowExecutionId))
            return null;

        // A payload this runtime cannot read cannot stand in for the execution, and letting its exception out would report
        // a deserialization failure in place of the refusal the drain called this to handle. The refusal then travels the
        // way it did before, through the start's own delivery result. Narrowed to the payload's own validation the way
        // WorkflowStartSchedulerWorkHandler narrows it, so an unrelated ArgumentException still surfaces as itself rather
        // than being misread as a malformed payload, and logged either way because turning the recovery off for a child
        // is not something to do quietly.
        WorkflowExecutionStartCommandPayload? payload;
        try
        {
            payload = payloadElement.Deserialize<WorkflowExecutionStartCommandPayload>();
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is ArgumentException argument &&
            WorkflowStartSchedulerWorkHandler.IsStartPayloadValidationException(argument))
        {
            _logger.LogWarning(
                exception,
                "Workflow execution {WorkflowExecutionId} could not be faulted from its start command because the command payload could not be read; its refusal stays with the start's delivery result",
                workflowExecutionId);
            return null;
        }

        if (payload?.ParentWorkflowExecutionId is null)
            return null;

        return new WorkflowExecutionState(
            workflowExecutionId,
            payload.PinnedExecutable,
            WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: occurredAt,
            StartedAt: occurredAt,
            UpdatedAt: occurredAt,
            CompletedAt: null,
            payload.CorrelationId,
            payload.ParentWorkflowExecutionId,
            payload.TenantId,
            new Dictionary<string, string>())
        {
            RunKind = payload.RunKind,
            PinnedSource = payload.PinnedSource,
            Partition = payload.Partition,
            Authority = payload.Authority,
            DispatchNestingDepth = payload.DispatchNestingDepth,
            TestScope = payload.TestScope
        };
    }

    private static RuntimeCheckpointCommit NewFaultCommit(
        WorkflowExecutionState workflow,
        string violation,
        DateTimeOffset occurredAt,
        bool hasAcceptedState)
    {
        var workflowExecutionId = workflow.WorkflowExecutionId;
        var incidentId = IncidentId(workflowExecutionId);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.IncidentId] = incidentId,
            [RuntimeMetadataKeys.CheckpointReason] = IncidentFailureType,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory
        };
        var incident = new IncidentState(
            incidentId: incidentId,
            workflowExecutionId: workflowExecutionId,
            activityExecutionId: null,
            executableNodeId: null,
            severity: IncidentSeverity.Critical,
            status: IncidentStatus.Blocking,
            resolutionOutcome: new IncidentResolutionOutcome(
                IncidentResolutionActionKinds.FaultWorkflow,
                occurredAt,
                strategy: null,
                systemSource: IncidentResolutionSystemSources.CheckpointRuleViolation),
            failureType: IncidentFailureType,
            message: hasAcceptedState
                ? $"A checkpoint rule refused a commit of workflow execution '{workflowExecutionId}', so it was faulted at its last accepted checkpoint: {violation}"
                : $"A checkpoint rule refused the first commit of workflow execution '{workflowExecutionId}', so it was faulted without ever reaching a checkpoint: {violation}",
            createdAt: occurredAt,
            resolvedAt: null,
            metadata: metadata);
        var faulted = RuntimeContainerScopeService.CloseRootFrame(workflow with
        {
            Status = WorkflowExecutionStatus.Faulted,
            UpdatedAt = occurredAt,
            CompletedAt = occurredAt
        });

        return new RuntimeCheckpointCommit(
            CommitId: $"commit:{workflowExecutionId}:checkpoint-rule-violation",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: $"checkpoint:{workflowExecutionId}:checkpoint-rule-violation",
                Name: RuntimeCheckpointNames.WorkflowFaulted,
                WorkflowExecutionId: workflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [],
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: new RuntimeStateChange<WorkflowExecutionState>(workflowExecutionId, RuntimeStateChangeOperation.Upsert, faulted, metadata),
                scheduler: null,
                activityExecutions: [],
                bookmarks: [],
                durableValues: [],
                incidents: [new RuntimeStateChange<IncidentState>(incidentId, RuntimeStateChangeOperation.Upsert, incident, metadata)],
                operational: [],
                activityExecutionInspections: []),
            PostCommitIntents: [],
            Metadata: metadata);
    }
}
