using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
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
/// An execution with no accepted state has nothing to fault, and one that is already terminal already has its outcome;
/// both are left alone. When the fault commit itself fails, that failure is thrown, so an execution that could not be
/// faulted is never reported as handled.
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
    private readonly ILogger<CheckpointRuleViolationWorkflowFaulter> _logger;

    public CheckpointRuleViolationWorkflowFaulter(
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        RuntimeCheckpointCommitter checkpointCommitter,
        IRuntimeFaultCapturePolicy faultCapturePolicy,
        TimeProvider timeProvider,
        ILogger<CheckpointRuleViolationWorkflowFaulter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(faultCapturePolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _workflowExecutionStateStore = workflowExecutionStateStore;
        _checkpointCommitter = checkpointCommitter;
        _faultCapturePolicy = faultCapturePolicy;
        _timeProvider = timeProvider;
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
    public async ValueTask FaultIfCheckpointRuleViolatedAsync(
        string workflowExecutionId,
        RuntimeSchedulerDrainResult? drainResult,
        Exception? drainFailure,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var violation = drainFailure is not null && RuntimeCheckpointCommitValidationException.IsCauseOf(drainFailure)
            ? _faultCapturePolicy.Capture(drainFailure).ToSummaryString()
            : drainResult?.Items.FirstOrDefault(item => item.CheckpointRuleViolation)?.Error;
        if (violation is null)
            return;

        var workflow = await _workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken);
        if (workflow is null || workflow.Status.IsTerminal())
            return;

        await _checkpointCommitter.CommitAsync(NewFaultCommit(workflow, violation, _timeProvider.GetUtcNow()), cancellationToken);
        _logger.LogWarning(
            drainFailure,
            "Workflow execution {WorkflowExecutionId} was faulted because a checkpoint rule refused one of its commits; incident {IncidentId} records the refusal: {Violation}",
            workflowExecutionId,
            IncidentId(workflowExecutionId),
            violation);
    }

    private static RuntimeCheckpointCommit NewFaultCommit(WorkflowExecutionState workflow, string violation, DateTimeOffset occurredAt)
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
            message: $"A checkpoint rule refused a commit of workflow execution '{workflowExecutionId}', so it was faulted at its last accepted checkpoint: {violation}",
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
