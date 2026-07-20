using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Cancels one composite activity scope without changing the enclosing workflow identity.</summary>
public sealed class WorkflowCancelActivityScopeSchedulerWorkHandler(
    IActivityExecutionStateStore activityExecutionStateStore,
    ActivitySubtreeCancellationPlanner cancellationPlanner,
    RuntimeCheckpointCommitter checkpointCommitter,
    IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
    TimeProvider timeProvider) : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
{
    public const string HandlerName = nameof(WorkflowCancelActivityScopeSchedulerWorkHandler);
    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem) =>
        workItem.CommandKind == WorkflowExecutionCommandKind.CancelActivityScope;

    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        var commit = await BuildCommitAsync(workItem, cancellationToken);
        if (commit is not null)
            await checkpointCommitter.CommitAsync(commit, cancellationToken);
    }

    public async ValueTask HandleAsync(
        RuntimeSchedulerWorkItem workItem,
        IRuntimePipelineContext pipelineContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pipelineContext);
        var commit = await BuildCommitAsync(workItem, cancellationToken);
        if (commit is not null)
            pipelineContext.Workspace.StageCheckpointCommit(commit);
    }

    private async ValueTask<RuntimeCheckpointCommit?> BuildCommitAsync(
        RuntimeSchedulerWorkItem workItem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();
        var command = Deserialize(workItem);
        var allStates = await activityExecutionStateStore.ListAllAsync(workItem.WorkflowExecutionId, cancellationToken);
        var byId = allStates.ToDictionary(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal);
        if (!byId.TryGetValue(command.ActivityExecutionId, out var outer))
            throw new InvalidOperationException($"Scope cancellation references missing activity execution '{command.ActivityExecutionId}'.");
        if (!StringComparer.Ordinal.Equals(outer.ExecutionScopeId, command.ExecutionScopeId))
            throw new InvalidOperationException($"Activity execution '{command.ActivityExecutionId}' does not own execution scope '{command.ExecutionScopeId}'.");
        if (outer.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Cancelled or ActivityExecutionStatus.Recovered)
            return null;

        var occurredAt = timeProvider.GetUtcNow();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = workItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = workItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = "ActivityScopeCancellation",
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ActivityExecutionId] = command.ActivityExecutionId,
            [RuntimeMetadataKeys.ScopeCancellationReason] = command.Reason
        };
        var plan = await cancellationPlanner.PlanAsync(
            workItem.WorkflowExecutionId,
            outer,
            allStates,
            subStatus: "ScopeCancelled",
            metadata,
            occurredAt,
            cancellationToken);
        var checkpointId = $"checkpoint:{workItem.WorkItemId}:activity-scope-cancelled:{command.ExecutionScopeId}";
        var inspections = new List<RuntimeStateChange<ActivityExecutionInspectionProjection>>(plan.CancelledStates.Count);
        foreach (var state in plan.CancelledStates)
        {
            var projection = await inspectionAccumulator.BuildProjectionAsync(
                state, checkpointId, occurredAt, metadata: metadata, cancellationToken: cancellationToken);
            inspections.Add(new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                state.Execution.ActivityExecutionId,
                RuntimeStateChangeOperation.Upsert,
                projection,
                metadata));
        }

        return new RuntimeCheckpointCommit(
            $"commit:{workItem.WorkItemId}:activity-scope-cancelled:{command.ExecutionScopeId}",
            new RuntimeCheckpoint(
                checkpointId,
                RuntimeCheckpointNames.ActivityCancelled,
                workItem.WorkflowExecutionId,
                occurredAt,
                plan.CancelledStates.Select(state => state.Execution.ActivityExecutionId).ToArray(),
                metadata),
            new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: plan.CancelledStates.Select(state => new RuntimeStateChange<ActivityExecutionState>(
                    state.Execution.ActivityExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    state,
                    metadata)).ToArray(),
                bookmarks: [],
                durableValues: [],
                incidents: plan.SuppressedIncidents.Select(incident => new RuntimeStateChange<IncidentState>(
                    incident.IncidentId,
                    RuntimeStateChangeOperation.Upsert,
                    incident,
                    metadata)).ToArray(),
                operational: [],
                activityExecutionInspections: inspections,
                activityScopeCleanups: [plan.Cleanup]),
            PostCommitIntents: [],
            Metadata: metadata);
    }

    private static CancelActivityScopeCommand Deserialize(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("CancelActivityScope scheduler work requires a payload.");
        try
        {
            return payload.Deserialize<CancelActivityScopeCommand>()
                   ?? throw new InvalidOperationException("CancelActivityScope scheduler work payload resolved to null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("CancelActivityScope scheduler work payload is invalid.", exception);
        }
    }
}
