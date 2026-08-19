using Elsa.Activities.Flowchart.Models;

namespace Elsa.Activities.Flowchart.Internal;

/// <summary>
/// Appends audit-only <see cref="FlowchartDiagnosticEvent"/> records to the execution state. Named in #275 as
/// a distinct responsibility. Diagnostics are never read back by the engine — they are pruned/capped on
/// persistence (#382) — but each append still bumps <see cref="FlowchartExecutionState.Sequence"/> and derives
/// its id via <see cref="FlowchartStateMutator.NewId"/>, so their placement is part of the pinned id stream.
/// Mechanical extraction of the former engine helpers; behavior and ordering are unchanged.
/// </summary>
internal static class FlowchartDiagnosticAccumulator
{
    public static FlowchartExecutionState Add(
        FlowchartExecutionState state,
        FlowchartDiagnosticKind kind,
        string? nodeId,
        string? connectionId,
        string? executionPathId,
        string? executionScopeId,
        string message)
    {
        var diagnostic = new FlowchartDiagnosticEvent(
            FlowchartStateMutator.NewId(state, "diag"),
            kind,
            message,
            nodeId,
            connectionId,
            executionPathId,
            executionScopeId);

        return Append(state, diagnostic);
    }

    public static FlowchartExecutionState Append(FlowchartExecutionState state, FlowchartDiagnosticEvent diagnostic) =>
        state with { Diagnostics = state.Diagnostics.Append(diagnostic).ToArray(), Sequence = state.Sequence + 1 };
}
