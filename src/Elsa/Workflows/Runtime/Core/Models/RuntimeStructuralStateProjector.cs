namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Applies the private metadata staged by a structural continuation to an activity execution state.
/// The returned state is still only a projection; callers own the atomic checkpoint that persists it.
/// </summary>
public static class RuntimeStructuralStateProjector
{
    public static ActivityExecutionState Apply(
        ActivityExecutionState state,
        RuntimeStructuralContinuation continuation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(continuation);

        if (continuation.PrivateMetadata.Count == 0)
            return state;

        var metadata = state.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        foreach (var (key, value) in continuation.PrivateMetadata)
            metadata[key] = value;

        return state with { Metadata = metadata };
    }
}
