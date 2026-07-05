using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// The single home for immutable <see cref="FlowchartExecutionState"/> mutations that append or update
/// records. Every builder here bumps <see cref="FlowchartExecutionState.Sequence"/>, and <see cref="NewId"/>
/// derives every record id (<c>path:N</c>, <c>scope:N</c>, <c>arrival:N</c>, <c>diag:N</c>) from that
/// sequence. Keeping all sequence-bumping mutation in one place is what makes the persisted id stream a pure,
/// stable function of mutation order and count (the byte-shape the golden fixtures pin, §E6/#382). This is a
/// mechanical extraction of the former private helpers on <see cref="FlowchartExecutionEngine"/> — behavior,
/// order, and the sequence formula are unchanged.
/// </summary>
internal static class FlowchartStateMutator
{
    public static string NewId(FlowchartExecutionState state, string prefix) =>
        $"{prefix}:{state.Sequence + 1}";

    public static ExecutionPath NewPath(
        FlowchartExecutionState state,
        string? parentExecutionPathId,
        string executionScopeId,
        string? currentNodeId,
        string? incomingConnectionId,
        string? schedulingActivityExecutionId,
        ExecutionPathStatus status,
        string? iterationKey) =>
        new(
            NewId(state, "path"),
            executionScopeId,
            parentExecutionPathId,
            currentNodeId,
            incomingConnectionId,
            schedulingActivityExecutionId,
            status,
            iterationKey);

    public static FlowchartExecutionState AddPath(FlowchartExecutionState state, ExecutionPath path) =>
        state with { ExecutionPaths = state.ExecutionPaths.Append(path).ToArray(), Sequence = state.Sequence + 1 };

    public static FlowchartExecutionState AddScope(FlowchartExecutionState state, ExecutionScope scope) =>
        state with { Scopes = state.Scopes.Append(scope).ToArray(), Sequence = state.Sequence + 1 };

    public static FlowchartExecutionState UpdatePath(FlowchartExecutionState state, ExecutionPath path) =>
        state with
        {
            ExecutionPaths = state.ExecutionPaths
                .Select(existing => StringComparer.Ordinal.Equals(existing.ExecutionPathId, path.ExecutionPathId) ? path : existing)
                .ToArray(),
            Sequence = state.Sequence + 1
        };

    public static FlowchartExecutionState UpdateScope(FlowchartExecutionState state, ExecutionScope scope) =>
        state with
        {
            Scopes = state.Scopes
                .Select(existing => StringComparer.Ordinal.Equals(existing.ExecutionScopeId, scope.ExecutionScopeId) ? scope : existing)
                .ToArray(),
            Sequence = state.Sequence + 1
        };

    public static FlowchartExecutionState AddArrival(FlowchartExecutionState state, ExecutionPath path, FlowchartConnection connection, string connectionId, string producingActivityExecutionId)
    {
        var arrival = new FlowchartArrival(
            NewId(state, "arrival"),
            path.ExecutionPathId,
            path.ExecutionScopeId,
            connection.Source.NodeId,
            connection.Target.NodeId,
            connectionId,
            connection.Source.Port,
            producingActivityExecutionId,
            iterationKey: path.IterationKey);

        return state with { Arrivals = state.Arrivals.Append(arrival).ToArray(), Sequence = state.Sequence + 1 };
    }

    public static FlowchartExecutionState UpdateArrival(FlowchartExecutionState state, FlowchartArrival arrival) =>
        state with
        {
            Arrivals = state.Arrivals
                .Select(existing => StringComparer.Ordinal.Equals(existing.ArrivalId, arrival.ArrivalId) ? arrival : existing)
                .ToArray(),
            Sequence = state.Sequence + 1
        };

    public static FlowchartExecutionState RemoveActiveChild(FlowchartExecutionState state, string executionPathId, string nodeId) =>
        state with
        {
            ActiveChildren = state.ActiveChildren
                .Where(child => !StringComparer.Ordinal.Equals(child.ExecutionPathId, executionPathId) || !StringComparer.Ordinal.Equals(child.NodeId, nodeId))
                .ToArray(),
            Sequence = state.Sequence + 1
        };
}
