using System.Text.Json;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// Loads and persists the <see cref="FlowchartExecutionState"/> blob and owns the #382 prune-on-save
/// behavior. Serializes with <see cref="JsonSerializerDefaults.Web"/> and no string-enum converter, so all
/// state enums persist as ordinals — the frozen §E6 wire surface pinned by the golden fixtures. The persisted
/// blob is written under <see cref="FlowchartExecutionEngine.StateMetadataKey"/> as part of a mandatory
/// activity-inspection checkpoint. Mechanical extraction of the former engine helpers; the prune predicate,
/// serialization options, checkpoint envelope, and metadata are byte-for-byte unchanged.
/// </summary>
public sealed class FlowchartStatePersister(
    RuntimeCheckpointCommitter checkpointCommitter,
    IRuntimeActivityExecutionInspectionAccumulator inspectionAccumulator,
    TimeProvider? timeProvider = null)
{
    private const string FlowchartStatePersistenceReason = "FlowchartStatePersistence";
    private const string RootPathId = "path:root";

    /// <summary>Maximum diagnostics retained in the persisted state blob (#382); oldest are dropped first.</summary>
    private const int DiagnosticsCap = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static FlowchartExecutionState CreateInitialState(string? startNodeId)
    {
        const string rootScopeId = "scope:root";
        const string rootPathId = "path:root";
        var rootScope = new ExecutionScope(rootScopeId, ExecutionScopeKind.Root, ownerNodeId: startNodeId);
        var rootPath = new ExecutionPath(rootPathId, rootScopeId, currentNodeId: startNodeId);
        return new FlowchartExecutionState(rootScopeId, [rootScope], [rootPath]);
    }

    public static FlowchartExecutionState? LoadState(ActivityExecutionState activityExecutionState)
    {
        if (!activityExecutionState.Metadata.TryGetValue(FlowchartExecutionEngine.StateMetadataKey, out var serialized) || string.IsNullOrWhiteSpace(serialized))
            return null;

        try
        {
            return JsonSerializer.Deserialize<FlowchartExecutionState>(serialized, SerializerOptions)
                   ?? throw new FlowchartExecutionException("Flowchart execution state metadata resolved to null.");
        }
        catch (FlowchartExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new FlowchartExecutionException("Flowchart execution state metadata is invalid.", exception);
        }
    }

    public void SaveState(IRuntimeActivityExecutionContext context, FlowchartExecutionState state)
    {
        SaveStateAsync(context, state)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Trims records that can never influence a future engine decision before the state is persisted
    /// (#382): without this, a loop-heavy flowchart re-serializes an ever-growing blob on every child
    /// completion — O(n²) in CPU and storage across n iterations.
    /// <list type="bullet">
    /// <item><b>Arrivals:</b> <see cref="FlowchartArrivalStatus.Consumed"/> arrivals are dropped
    /// unconditionally — the only arrival reader (<see cref="FlowchartJoinCoordinator.MatchingArrivals"/>) filters on
    /// <see cref="FlowchartArrivalStatus.Arrived"/>.</item>
    /// <item><b>Paths:</b> only <see cref="ExecutionPathStatus.Completed"/> paths are dropped, and then
    /// only unless they are (a) the root path, (b) referenced by an
    /// <see cref="FlowchartExecutionState.ActiveChildren"/> entry, or (c) referenced by a surviving
    /// arrival (belt-and-braces; such paths are Waiting and therefore retained anyway).
    /// <see cref="ExecutionPathStatus.Canceled"/>/<see cref="ExecutionPathStatus.Faulted"/> paths are
    /// <b>never</b> pruned: <see cref="FlowchartScopeResolver.CompleteRaceScope"/> strips losing branches from
    /// <c>ActiveChildren</c> while their activities may still be in flight, and the loser's late
    /// completion is absorbed via the by-id path lookup ("ignored completion for canceled path") — prune
    /// the record and that lookup faults the flowchart. Canceled/Faulted counts are bounded by graph
    /// structure (races, breaks, incidents), not by loop iterations, so the O(n²) growth fix is
    /// unaffected. <see cref="ExecutionPath.ParentExecutionPathId"/> references may dangle after
    /// pruning; the engine never reads them.</item>
    /// <item><b>Diagnostics:</b> capped to the most recent <see cref="DiagnosticsCap"/>; they are
    /// audit-only and never read by the engine.</item>
    /// <item><b>Scopes are never pruned:</b> loop iteration numbering derives from the cumulative
    /// loop-iteration scope count per owner node (<see cref="FlowchartScopeResolver.ResolveTargetScope"/>); pruning would collapse
    /// the count and let a later iteration reuse an earlier iteration key.</item>
    /// </list>
    /// Pruning shapes only what is written — the schema is unchanged and readers accept unpruned pre-#382
    /// blobs (see the golden-fixture tests). The in-memory state within a callback is never pruned;
    /// <see cref="FlowchartExecutionState.Sequence"/> is not bumped because no logical mutation occurs.
    /// </summary>
    private static FlowchartExecutionState PruneForPersistence(FlowchartExecutionState state)
    {
        var arrivals = state.Arrivals.Where(arrival => arrival.Status == FlowchartArrivalStatus.Arrived).ToArray();

        var retainedPathIds = new HashSet<string>(StringComparer.Ordinal) { RootPathId };
        foreach (var child in state.ActiveChildren)
            retainedPathIds.Add(child.ExecutionPathId);
        foreach (var arrival in arrivals)
            retainedPathIds.Add(arrival.ExecutionPathId);

        var paths = state.ExecutionPaths
            .Where(path => path.Status != ExecutionPathStatus.Completed
                           || retainedPathIds.Contains(path.ExecutionPathId))
            .ToArray();

        var diagnostics = state.Diagnostics.Count <= DiagnosticsCap
            ? state.Diagnostics
            : state.Diagnostics.Skip(state.Diagnostics.Count - DiagnosticsCap).ToArray();

        return state with { Arrivals = arrivals, ExecutionPaths = paths, Diagnostics = diagnostics };
    }

    public async ValueTask SaveStateAsync(IRuntimeActivityExecutionContext context, FlowchartExecutionState state)
    {
        var metadata = context.ActivityExecutionState.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[FlowchartExecutionEngine.StateMetadataKey] = JsonSerializer.Serialize(PruneForPersistence(state), SerializerOptions);
        var updatedState = context.ActivityExecutionState with { Metadata = metadata };
        var occurredAt = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var checkpointId = $"checkpoint:{context.SchedulerWorkItem.WorkItemId}:flowchart-state";
        var commitId = $"commit:{context.SchedulerWorkItem.WorkItemId}:flowchart-state";
        var checkpointMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = context.SchedulerWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CommandId] = context.SchedulerWorkItem.CommandId,
            [RuntimeMetadataKeys.CheckpointReason] = FlowchartStatePersistenceReason,
            [RuntimeMetadataKeys.CheckpointRequirement] = RuntimeMetadataKeys.CheckpointRequirementMandatory,
            [RuntimeMetadataKeys.ExecutableArtifactId] = context.PinnedExecutable.ArtifactId,
            [RuntimeMetadataKeys.ExecutableArtifactVersion] = context.PinnedExecutable.ArtifactVersion,
            [RuntimeMetadataKeys.ExecutableArtifactHash] = context.PinnedExecutable.ArtifactHash
        };
        var stateChangeMetadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.SchedulerWorkItemId] = context.SchedulerWorkItem.WorkItemId,
            [RuntimeMetadataKeys.CheckpointReason] = FlowchartStatePersistenceReason
        };
        var inspection = await inspectionAccumulator.BuildProjectionAsync(
            updatedState,
            checkpointId,
            occurredAt,
            metadata: stateChangeMetadata,
            cancellationToken: context.CancellationToken);
        var commit = new RuntimeCheckpointCommit(
            CommitId: commitId,
            Checkpoint: new RuntimeCheckpoint(
                CheckpointId: checkpointId,
                Name: RuntimeCheckpointNames.ActivityInspectionCaptured,
                WorkflowExecutionId: context.WorkflowExecutionId,
                OccurredAt: occurredAt,
                ActivityExecutionIds: [updatedState.Execution.ActivityExecutionId],
                Metadata: checkpointMetadata),
            StateChanges: new RuntimeCheckpointStateChangeSet(
                workflowExecution: null,
                scheduler: null,
                activityExecutions:
                [
                    new RuntimeStateChange<ActivityExecutionState>(
                        StateId: updatedState.Execution.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: updatedState,
                        Metadata: stateChangeMetadata)
                ],
                bookmarks: [],
                durableValues: [],
                incidents: [],
                operational: [],
                activityExecutionInspections:
                [
                    new RuntimeStateChange<ActivityExecutionInspectionProjection>(
                        StateId: updatedState.Execution.ActivityExecutionId,
                        Operation: RuntimeStateChangeOperation.Upsert,
                        State: inspection,
                        Metadata: stateChangeMetadata)
                ]),
            PostCommitIntents: [],
            Metadata: new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.SchedulerWorkItemId] = context.SchedulerWorkItem.WorkItemId,
                [RuntimeMetadataKeys.CommandKind] = context.SchedulerWorkItem.CommandKind.ToString()
            });

        await checkpointCommitter.CommitAsync(commit, context.CancellationToken);
    }
}
