namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Applies the typed private state staged by a structural continuation to an activity execution state.
/// The returned state is still only a projection; callers own the atomic checkpoint that persists it.
/// </summary>
public static class RuntimeStructuralStateProjector
{
    public static ActivityExecutionState Apply(
        ActivityExecutionState state,
        RuntimeStructuralContinuation continuation,
        DateTimeOffset committedAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(continuation);

        if (continuation.StateUpdate is not { } update)
            return state;

        var attempt = state.Attempts?.SingleOrDefault(candidate => candidate.EndedAt is null)
            ?? throw new InvalidOperationException(
                $"Structural activity invocation '{state.InvocationId}' must have exactly one open attempt before staging private state.");

        return state with
        {
            PrivateState = new ActivityPrivateState(
                state.InvocationId,
                update.StateVersion,
                update.Value,
                attempt.AttemptId,
                committedAt)
        };
    }
}
