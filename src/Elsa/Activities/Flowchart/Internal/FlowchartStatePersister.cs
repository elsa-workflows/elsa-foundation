using System.Text.Json;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// Loads and stages the <see cref="FlowchartExecutionState"/> blob and owns the #382 prune-on-save
/// behavior. Serializes with <see cref="JsonSerializerDefaults.Web"/> and no string-enum converter, so all
/// state enums persist as ordinals — the frozen §E6 wire surface pinned by the golden fixtures. The persisted
/// blob is carried as one typed, versioned structural private-state document. The runtime folds that state
/// into the same checkpoint as the structural continuation and
/// its child schedule intents; this service never commits state independently.
/// </summary>
public sealed class FlowchartStatePersister
{
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
        if (activityExecutionState.PrivateState is not { } privateState)
            return null;

        if (privateState.StateVersion != FlowchartExecutionEngine.StateSchemaVersion ||
            !StringComparer.Ordinal.Equals(privateState.Value.Type.Alias, FlowchartExecutionEngine.StateTypeAlias) ||
            privateState.Value.InlineValue is not { } payload)
        {
            throw new FlowchartExecutionException("Flowchart private state does not match the required type and schema version.");
        }

        try
        {
            return payload.Deserialize<FlowchartExecutionState>(SerializerOptions)
                   ?? throw new FlowchartExecutionException("Flowchart private state resolved to null.");
        }
        catch (FlowchartExecutionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new FlowchartExecutionException("Flowchart private state is invalid.", exception);
        }
    }

    /// <summary>
    /// Trims records that can never influence a future engine decision before the state is persisted
    /// (#382): without this, a loop-heavy flowchart re-serializes an ever-growing blob on every child
    /// completion — O(n²) in CPU and storage across n iterations.
    /// <list type="bullet">
    /// <item><b>Arrivals:</b> <see cref="FlowchartArrivalStatus.Consumed"/> arrivals are dropped
    /// unconditionally — every arrival reader (<see cref="FlowchartJoinCoordinator.PendingArrivals"/>) skips
    /// them. <see cref="FlowchartArrivalStatus.Dead"/> arrivals are dropped once their execution scope is no
    /// longer live (ADR 0064): a dead arrival is retained only while some Active/Waiting path or active child
    /// still sits in its scope, which is what keeps a loop from accumulating one stranded dead arrival per
    /// iteration. Dropping one early is safe for the same reason its iteration key is: the join it was owed to
    /// can no longer fire in that scope, and a later iteration mints a fresh key it could not have matched
    /// anyway.</item>
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
    /// <item><b>Scopes:</b> only <see cref="ExecutionScopeKind.LoopIteration"/> scopes are prunable — they
    /// are the sole scope kind that scales with loop iterations; <c>Root</c>/<c>Branch</c>/<c>Join</c>/<c>Race</c>
    /// counts are bounded by graph structure. A loop-iteration scope is retained iff it is still referenced
    /// by a surviving path (post path-prune) or an <see cref="FlowchartExecutionState.ActiveChildren"/> entry,
    /// or is the root scope. The safety invariant: the engine only ever dereferences a scope by the
    /// <see cref="ExecutionPath.ExecutionScopeId"/> of a <b>live</b> (Active/Waiting) path
    /// (<see cref="FlowchartExecutionEngine"/>, <see cref="FlowchartPolicyApplier"/>), and live paths are
    /// never pruned — so every scope the engine can still reach is in the retained set, and only scopes of
    /// fully-completed past iterations are dropped. Iteration numbering is unaffected because it derives from
    /// the explicit monotonic <see cref="FlowchartExecutionState.LoopIterationCounters"/>, never from the
    /// live scope count, so a pruned scope can never let a later iteration reuse its key.</item>
    /// </list>
    /// Pruning shapes only what is written — the persisted schema is unchanged and any unpruned state
    /// deserializes losslessly (see the golden-fixture tests). The in-memory state within a callback is never
    /// pruned; <see cref="FlowchartExecutionState.Sequence"/> is not bumped because no logical mutation
    /// occurs, and <see cref="FlowchartExecutionState.LoopIterationCounters"/> is carried through untouched so
    /// numbering keeps climbing after scopes are dropped.
    /// </summary>
    private static FlowchartExecutionState PruneForPersistence(FlowchartExecutionState state)
    {
        var liveScopeIds = new HashSet<string>(StringComparer.Ordinal) { state.RootExecutionScopeId };
        foreach (var path in state.ExecutionPaths.Where(path => path.Status is ExecutionPathStatus.Active or ExecutionPathStatus.Waiting))
            liveScopeIds.Add(path.ExecutionScopeId);
        foreach (var child in state.ActiveChildren)
            liveScopeIds.Add(child.ExecutionScopeId);

        var arrivals = state.Arrivals
            .Where(arrival => arrival.Status switch
            {
                FlowchartArrivalStatus.Arrived => true,
                FlowchartArrivalStatus.Dead => liveScopeIds.Contains(arrival.ExecutionScopeId),
                _ => false
            })
            .ToArray();

        var retainedPathIds = new HashSet<string>(StringComparer.Ordinal) { RootPathId };
        foreach (var child in state.ActiveChildren)
            retainedPathIds.Add(child.ExecutionPathId);
        foreach (var arrival in arrivals)
            retainedPathIds.Add(arrival.ExecutionPathId);

        var paths = state.ExecutionPaths
            .Where(path => path.Status != ExecutionPathStatus.Completed
                           || retainedPathIds.Contains(path.ExecutionPathId))
            .ToArray();

        var retainedScopeIds = new HashSet<string>(StringComparer.Ordinal) { state.RootExecutionScopeId };
        foreach (var path in paths)
            retainedScopeIds.Add(path.ExecutionScopeId);
        foreach (var child in state.ActiveChildren)
            retainedScopeIds.Add(child.ExecutionScopeId);
        RetainAncestorsUpToNearestLoopIteration(state, retainedScopeIds);

        var scopes = state.Scopes
            .Where(scope => scope.Kind != ExecutionScopeKind.LoopIteration
                            || retainedScopeIds.Contains(scope.ExecutionScopeId))
            .ToArray();

        var diagnostics = state.Diagnostics.Count <= DiagnosticsCap
            ? state.Diagnostics
            : state.Diagnostics.Skip(state.Diagnostics.Count - DiagnosticsCap).ToArray();

        return state with { Arrivals = arrivals, ExecutionPaths = paths, Scopes = scopes, Diagnostics = diagnostics };
    }

    /// <summary>
    /// Extends the retained scope set with each retained scope's ancestors, stopping at (and including) the
    /// first <see cref="ExecutionScopeKind.LoopIteration"/> one.
    /// <para>
    /// ADR 0064 WU-4 derives a path's iteration key by walking up to its nearest enclosing loop iteration, so
    /// that walk must not run off a pruned parent. Without this, a live path inside a race scope nested in a
    /// loop iteration would keep its own scope but lose the loop scope above it, and the derived key would
    /// silently read <c>null</c>. Stopping at the first loop iteration is what keeps this bounded: iteration
    /// <i>n+1</i>'s scope is parented to iteration <i>n</i>'s, so retaining the whole chain would pin every
    /// iteration ever run and undo the #382 growth fix.
    /// </para>
    /// </summary>
    private static void RetainAncestorsUpToNearestLoopIteration(FlowchartExecutionState state, HashSet<string> retainedScopeIds)
    {
        var scopesById = state.Scopes.ToDictionary(scope => scope.ExecutionScopeId, StringComparer.Ordinal);

        foreach (var scopeId in retainedScopeIds.ToArray())
        {
            var current = scopeId;
            while (scopesById.TryGetValue(current, out var scope) &&
                   scope.Kind != ExecutionScopeKind.LoopIteration &&
                   scope.ParentExecutionScopeId is { } parentScopeId)
            {
                if (!retainedScopeIds.Add(parentScopeId))
                    break;

                current = parentScopeId;
            }
        }
    }

    public RuntimeStructuralContinuation StageState(
        RuntimeStructuralContinuation continuation,
        FlowchartExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        ArgumentNullException.ThrowIfNull(state);

        var value = ValueEnvelope.Inline(
            new Elsa.Primitives.Models.ValueTypeDescriptor(
                FlowchartExecutionEngine.StateTypeAlias,
                schemaVersion: FlowchartExecutionEngine.StateSchemaVersion),
            JsonSerializer.SerializeToElement(PruneForPersistence(state), SerializerOptions),
            ValueProtectionPolicy.InstanceInline);

        return continuation.WithState(value, FlowchartExecutionEngine.StateSchemaVersion);
    }
}
