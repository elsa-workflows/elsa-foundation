using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Cancels one composite activity scope without changing the enclosing workflow identity.</summary>
public sealed class WorkflowCancelActivityScopeSchedulerWorkHandler(
    IActivityExecutionStateStore activityExecutionStateStore,
    IActivityScopeCleanupStore cleanupStore,
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

        var scopedStates = TraverseScope(outer, allStates, cancellationToken);
        var scopeIds = scopedStates.Select(state => state.Execution.ActivityExecutionId).ToHashSet(StringComparer.Ordinal);
        var cleanup = await cleanupStore.CaptureAsync(workItem.WorkflowExecutionId, command.ExecutionScopeId, scopeIds, cancellationToken);
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
        var cancelled = scopedStates
            .Where(IsCancellable)
            .Select(state => state with
            {
                Status = ActivityExecutionStatus.Cancelled,
                SubStatus = "ScopeCancelled",
                CompletedAt = occurredAt,
                Metadata = Merge(state.Metadata, metadata)
            })
            .OrderBy(state => state.ExecutionSequence)
            .ThenBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal)
            .ToArray();
        var checkpointId = $"checkpoint:{workItem.WorkItemId}:activity-scope-cancelled:{command.ExecutionScopeId}";
        var inspections = new List<RuntimeStateChange<ActivityExecutionInspectionProjection>>(cancelled.Length);
        foreach (var state in cancelled)
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
                cancelled.Select(state => state.Execution.ActivityExecutionId).ToArray(),
                metadata),
            new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions: cancelled.Select(state => new RuntimeStateChange<ActivityExecutionState>(
                    state.Execution.ActivityExecutionId,
                    RuntimeStateChangeOperation.Upsert,
                    state,
                    metadata)).ToArray(),
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections: inspections,
                activityScopeCleanups: [cleanup]),
            PostCommitIntents: [],
            Metadata: metadata);
    }

    private static IReadOnlyList<ActivityExecutionState> TraverseScope(
        ActivityExecutionState outer,
        IReadOnlyCollection<ActivityExecutionState> allStates,
        CancellationToken cancellationToken)
    {
        var children = allStates.Where(state => state.ParentActivityExecutionId is not null)
            .GroupBy(state => state.ParentActivityExecutionId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(state => state.ExecutionSequence)
                .ThenBy(state => state.Execution.ActivityExecutionId, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var result = new List<ActivityExecutionState>();
        var queue = new Queue<ActivityExecutionState>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue(outer);
        while (queue.TryDequeue(out var state))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(state.Execution.ActivityExecutionId))
                continue;
            result.Add(state);
            if (!children.TryGetValue(state.Execution.ActivityExecutionId, out var directChildren))
                continue;
            foreach (var child in directChildren)
                queue.Enqueue(child);
        }

        return result;
    }

    private static bool IsCancellable(ActivityExecutionState state) =>
        state.Status is ActivityExecutionStatus.Scheduled or ActivityExecutionStatus.Running or ActivityExecutionStatus.Waiting or ActivityExecutionStatus.Suspended;

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> existing,
        IReadOnlyDictionary<string, string> additions)
    {
        var result = existing.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var item in additions)
            result[item.Key] = item.Value;
        return result;
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
