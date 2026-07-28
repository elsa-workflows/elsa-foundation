using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Consumes recorded incidents to drive the workflow-level fault policy (RT-1 gap a, RT-5). After a drain, if the
/// workflow has one or more blocking incidents and has not already reached a terminal status, this observer commits
/// a <see cref="RuntimeCheckpointNames.WorkflowFaulted"/> checkpoint transitioning the workflow execution status to
/// <see cref="WorkflowExecutionStatus.Faulted"/> — making a faulted workflow observable instead of stuck in
/// <see cref="WorkflowExecutionStatus.Running"/> forever.
///
/// <para><b>Faulted semantics.</b> <see cref="WorkflowExecutionStatus.Faulted"/> is a terminal status: once set, the
/// drainer's terminal-status gate stops further scheduler work. Recovering a faulted workflow (resolving the blocking
/// incident and resuming) is a separate operator/intervention surface, not part of this transition.</para>
/// </summary>
public sealed class BlockingIncidentWorkflowFaultObserver : IWorkflowSchedulerDrainObserver
{
    private const string FaultReason = "BlockingIncident";
    private readonly IIncidentStateStore _incidentStateStore;
    private readonly IWorkflowExecutionStateStore _workflowExecutionStateStore;
    private readonly IActivityExecutionStateStore _activityExecutionStateStore;
    private readonly IRuntimeActivityExecutionInspectionAccumulator _inspectionAccumulator;
    private readonly RuntimeCheckpointCommitter _checkpointCommitter;
    private readonly TimeProvider _timeProvider;

    public BlockingIncidentWorkflowFaultObserver(
        IIncidentStateStore incidentStateStore,
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        IActivityExecutionStateStore activityExecutionStateStore,
        IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
        RuntimeCheckpointCommitter checkpointCommitter,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(incidentStateStore);
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentNullException.ThrowIfNull(activityExecutionStateStore);
        ArgumentNullException.ThrowIfNull(inspectionAccumulator);
        ArgumentNullException.ThrowIfNull(checkpointCommitter);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _incidentStateStore = incidentStateStore;
        _workflowExecutionStateStore = workflowExecutionStateStore;
        _activityExecutionStateStore = activityExecutionStateStore;
        _inspectionAccumulator = inspectionAccumulator;
        _checkpointCommitter = checkpointCommitter;
        _timeProvider = timeProvider;
    }

    public async ValueTask OnDrainedAsync(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerDrainResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(result);

        var workflowExecutionId = envelope.WorkflowExecutionId;

        var workflowState = await _workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken);
        if (workflowState is null || workflowState.Status.IsTerminal())
            return;

        var blockingIncidents = (await _incidentStateStore.ListBlockingAsync(workflowExecutionId, cancellationToken))
            .Where(incident => incident.ResolutionOutcome is null)
            .ToArray();
        if (blockingIncidents.Length == 0)
            return;
        // A missing stable consumer/schema is a deployment compatibility incident. Keep the run recoverable
        // while it waits for deployment correction; ordinary activity faults still terminalize the workflow.
        if (blockingIncidents.All(x => StringComparer.Ordinal.Equals(
                x.FailureType,
                ActivityActivationFailureHandler.IncidentFailureType)))
            return;

        var occurredAt = _timeProvider.GetUtcNow();
        var faultedAncestorStates = await FindFaultedAncestorStatesAsync(workflowExecutionId, blockingIncidents, occurredAt, cancellationToken);
        var faultedState = RuntimeContainerScopeService.CloseRootFrame(workflowState with
        {
            Status = WorkflowExecutionStatus.Faulted,
            UpdatedAt = occurredAt,
            CompletedAt = occurredAt
        });

        var checkpointId = $"checkpoint:{workflowExecutionId}:workflow-faulted";
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.CheckpointReason] = FaultReason
        };
        var activityStateChanges = faultedAncestorStates
            .Select(state => new RuntimeStateChange<ActivityExecutionState>(
                StateId: state.Execution.ActivityExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: state,
                Metadata: metadata))
            .ToArray();
        var activityInspectionChanges = new List<RuntimeStateChange<ActivityExecutionInspectionProjection>>();
        foreach (var state in faultedAncestorStates)
        {
            var inspection = await _inspectionAccumulator.BuildProjectionAsync(
                state,
                checkpointId,
                occurredAt,
                metadata: metadata,
                cancellationToken: cancellationToken);
            activityInspectionChanges.Add(new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                StateId: state.Execution.ActivityExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: inspection,
                Metadata: metadata));
        }

        var commit = new RuntimeCheckpointCommit(
            CommitId: $"commit:{workflowExecutionId}:workflow-faulted",
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.WorkflowFaulted,
                WorkflowExecutionId: workflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: faultedAncestorStates.Select(state => state.Execution.ActivityExecutionId).ToArray(),
                Metadata: metadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: new RuntimeStateChange<WorkflowExecutionState>(
                    StateId: workflowExecutionId,
                    Operation: RuntimeStateChangeOperation.Upsert,
                    State: faultedState,
                    Metadata: metadata),
                scheduler: null,
                activityExecutions: activityStateChanges,
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections: activityInspectionChanges),
            PostCommitIntents: [],
            Metadata: metadata);

        await _checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    private async ValueTask<IReadOnlyCollection<ActivityExecutionState>> FindFaultedAncestorStatesAsync(
        string workflowExecutionId,
        IReadOnlyCollection<IncidentState> blockingIncidents,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        var activeAncestors = new Dictionary<string, ActivityExecutionState>(StringComparer.Ordinal);
        var incidentsByAncestor = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var incident in blockingIncidents.OrderBy(item => item.IncidentId, StringComparer.Ordinal))
        {
            if (string.IsNullOrEmpty(incident.ActivityExecutionId))
                continue;

            var incidentActivity = await _activityExecutionStateStore.FindAsync(workflowExecutionId, incident.ActivityExecutionId, cancellationToken);
            var ancestorId = incidentActivity?.ParentActivityExecutionId;
            var visited = new HashSet<string>(StringComparer.Ordinal);

            while (!string.IsNullOrEmpty(ancestorId) && visited.Add(ancestorId))
            {
                var ancestor = await _activityExecutionStateStore.FindAsync(workflowExecutionId, ancestorId, cancellationToken);
                if (ancestor is null)
                    break;

                if (!IsTerminal(ancestor.Status))
                {
                    var activityExecutionId = ancestor.Execution.ActivityExecutionId;
                    activeAncestors[activityExecutionId] = ancestor;
                    if (!incidentsByAncestor.TryGetValue(activityExecutionId, out var incidentIds))
                    {
                        incidentIds = new(StringComparer.Ordinal);
                        incidentsByAncestor.Add(activityExecutionId, incidentIds);
                    }

                    incidentIds.Add(incident.IncidentId);
                }

                ancestorId = ancestor.ParentActivityExecutionId;
            }
        }

        return activeAncestors.Values
            .Select(state => RuntimeContainerScopeService.CloseOwnedFrames(state with
            {
                Status = ActivityExecutionStatus.Faulted,
                SubStatus = FaultReason,
                CompletedAt = completedAt,
                AggregateFaultCount = state.AggregateFaultCount + incidentsByAncestor[state.Execution.ActivityExecutionId].Count
            }))
            .OrderBy(state => state.ExecutionSequence)
            .ThenBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsTerminal(ActivityExecutionStatus status) =>
        status is ActivityExecutionStatus.Completed
            or ActivityExecutionStatus.Faulted
            or ActivityExecutionStatus.Cancelled
            or ActivityExecutionStatus.Recovered;
}
