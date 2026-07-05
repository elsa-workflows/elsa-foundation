using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Models;
using static Elsa.Activities.Flowchart.Internal.FlowchartStateMutator;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// Execution-scope resolution and scope-scoped path cancellation for the Flowchart engine: race-continuation
/// lookup, loop-iteration scope creation on backward edges, first-wins race completion, and Break-driven path
/// cancellation. Loop-iteration numbering derives from the cumulative loop-iteration scope count per owner
/// node (<see cref="ResolveTargetScope"/>) — the reason scopes are never pruned (#382). Mechanical extraction
/// of the former engine helpers; behavior, ordering, and the iteration-numbering rule are unchanged.
/// </summary>
internal static class FlowchartScopeResolver
{
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

        var iterationNumber = state.Scopes.Count(scope => scope.Kind == ExecutionScopeKind.LoopIteration && StringComparer.Ordinal.Equals(scope.OwnerNodeId, connection.Target.NodeId)) + 1;
        var iterationKey = $"{connection.Target.NodeId}:{iterationNumber}";
        var scope = new ExecutionScope(
            executionScopeId: NewId(state, "scope"),
            kind: ExecutionScopeKind.LoopIteration,
            parentExecutionScopeId: currentScope.ExecutionScopeId,
            createdByNodeId: connection.Source.NodeId,
            startConnectionId: graph.GetConnectionId(connection),
            ownerNodeId: connection.Target.NodeId,
            loopIterationKey: iterationKey);

        return scope;
    }
}
