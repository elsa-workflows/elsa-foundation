using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Models;
using static Elsa.Activities.Flowchart.Internal.FlowchartStateMutator;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// Execution-scope resolution and scope-scoped path cancellation for the Flowchart engine: race-continuation
/// lookup, loop-iteration scope creation on backward edges, first-wins race completion, and Break-driven path
/// cancellation. Loop-iteration numbering derives from the explicit monotonic per-owner counter in
/// <see cref="FlowchartExecutionState.LoopIterationCounters"/> (<see cref="ResolveTargetScope"/>), decoupled
/// from the live scope count — this is what lets stale loop-iteration scopes be pruned on persistence
/// without a later iteration reusing an earlier key (#382 / W32).
/// </summary>
internal static class FlowchartScopeResolver
{
    /// <summary>
    /// The loop-iteration key that belongs to <paramref name="executionScopeId"/>: the
    /// <see cref="ExecutionScope.LoopIterationKey"/> of the nearest enclosing
    /// <see cref="ExecutionScopeKind.LoopIteration"/> scope, walking up the scope tree, or <c>null</c> outside
    /// a loop.
    /// <para>
    /// ADR 0064 WU-4: the key used to be copied from the emitting path onto every new one, which made the scope
    /// tree and the key two independent records of the same fact — and two records of one fact can disagree.
    /// Deriving it means a path's iteration identity is whatever its scope says it is, by construction. This
    /// changes where the key comes from, not what it is: numbering still comes from the monotonic per-owner
    /// <see cref="FlowchartExecutionState.LoopIterationCounters"/> (#382 / W32), so pruning a completed
    /// iteration's scope still cannot let a later iteration reuse an earlier key.
    /// </para>
    /// </summary>
    public static string? ResolveIterationKey(FlowchartExecutionState state, string executionScopeId)
    {
        var scopeId = executionScopeId;
        // The chain is a tree, so it terminates; the bound is a cheap guard against a corrupt parent cycle
        // rather than an expected case.
        for (var depth = 0; depth <= state.Scopes.Count; depth++)
        {
            var scope = state.Scopes.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.ExecutionScopeId, scopeId));
            if (scope is null)
                return null;

            if (scope.Kind == ExecutionScopeKind.LoopIteration)
                return scope.LoopIterationKey;

            if (scope.ParentExecutionScopeId is not { } parentScopeId)
                return null;

            scopeId = parentScopeId;
        }

        return null;
    }

    public static ExecutionScope ResolveContinuationScope(FlowchartExecutionState state, ExecutionScope scope)
    {
        if (scope.Kind != ExecutionScopeKind.Race || string.IsNullOrWhiteSpace(scope.ParentExecutionScopeId))
            return scope;

        return state.Scopes.FirstOrDefault(item => StringComparer.Ordinal.Equals(item.ExecutionScopeId, scope.ParentExecutionScopeId))
               ?? throw new FlowchartExecutionException($"Flowchart race parent scope '{scope.ParentExecutionScopeId}' was not found.");
    }

    public static FlowchartExecutionState CancelPendingPathsForBreak(FlowchartExecutionState state, ExecutionPath breakingPath, string breakNodeId)
    {
        state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Completed, breakNodeId, null, breakingPath.ExecutionPathId, breakingPath.ExecutionScopeId, $"Flowchart ended early because node '{breakNodeId}' completed with a Break outcome.");

        foreach (var pendingPath in state.ExecutionPaths
                     .Where(path =>
                         !StringComparer.Ordinal.Equals(path.ExecutionPathId, breakingPath.ExecutionPathId) &&
                         (path.Status == ExecutionPathStatus.Active || path.Status == ExecutionPathStatus.Waiting))
                     .ToArray())
        {
            state = UpdatePath(state, pendingPath with { Status = ExecutionPathStatus.Canceled });
            state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Canceled, pendingPath.CurrentNodeId, pendingPath.IncomingConnectionId, pendingPath.ExecutionPathId, pendingPath.ExecutionScopeId, $"Flowchart Break canceled pending path '{pendingPath.ExecutionPathId}'.");
        }

        return state with { ActiveChildren = [], Sequence = state.Sequence + 1 };
    }

    public static FlowchartExecutionState CompleteRaceScope(FlowchartExecutionState state, ExecutionScope raceScope, ExecutionPath winningPath)
    {
        foreach (var losingPath in state.ExecutionPaths
                     .Where(path =>
                         !StringComparer.Ordinal.Equals(path.ExecutionPathId, winningPath.ExecutionPathId) &&
                         StringComparer.Ordinal.Equals(path.ExecutionScopeId, raceScope.ExecutionScopeId) &&
                         (path.Status == ExecutionPathStatus.Active || path.Status == ExecutionPathStatus.Waiting))
                     .ToArray())
        {
            state = UpdatePath(state, losingPath with { Status = ExecutionPathStatus.Canceled });
            state = FlowchartDiagnosticAccumulator.Add(state, FlowchartDiagnosticKind.Canceled, losingPath.CurrentNodeId, losingPath.IncomingConnectionId, losingPath.ExecutionPathId, losingPath.ExecutionScopeId, $"Flowchart first-wins race canceled losing path '{losingPath.ExecutionPathId}'.");
        }

        state = state with
        {
            ActiveChildren = state.ActiveChildren
                .Where(child => !StringComparer.Ordinal.Equals(child.ExecutionScopeId, raceScope.ExecutionScopeId) || StringComparer.Ordinal.Equals(child.ExecutionPathId, winningPath.ExecutionPathId))
                .ToArray(),
            Sequence = state.Sequence + 1
        };
        return UpdateScope(state, raceScope with { Status = ExecutionScopeStatus.Completed });
    }

    public static ExecutionScope ResolveTargetScope(FlowchartReachabilityAnalyzer reachabilityAnalyzer, FlowchartExecutionState state, FlowchartGraph graph, ExecutionScope currentScope, FlowchartConnection connection)
    {
        if (!reachabilityAnalyzer.IsBackwardEdge(graph, connection.Source.NodeId, connection.Target.NodeId))
            return currentScope;

        // Iteration numbering derives from the explicit monotonic per-owner counter, not the live scope
        // count — this is what lets stale loop-iteration scopes be pruned without a later iteration ever
        // reusing an earlier key. FlowchartStateMutator.AddScope advances the counter atomically when this
        // scope is appended, reading the same pre-append value, so the minted key and the bump stay in sync.
        var ownerNodeId = connection.Target.NodeId;
        var iterationNumber = (state.LoopIterationCounters.TryGetValue(ownerNodeId, out var lastIterationNumber) ? lastIterationNumber : 0) + 1;
        var iterationKey = $"{ownerNodeId}:{iterationNumber}";
        var scope = new ExecutionScope(
            executionScopeId: NewId(state, "scope"),
            kind: ExecutionScopeKind.LoopIteration,
            parentExecutionScopeId: currentScope.ExecutionScopeId,
            createdByNodeId: connection.Source.NodeId,
            startConnectionId: graph.GetConnectionId(connection),
            ownerNodeId: ownerNodeId,
            loopIterationKey: iterationKey);

        return scope;
    }
}
