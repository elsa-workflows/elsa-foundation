using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// The nearest closed activity execution scope above a node, and the view its body reads (spec 265). A closed
/// boundary (a graph activity compiled as a closed scope) replaces the started workflow for every node below it:
/// request inputs are the boundary's captured inputs and the root variable frame is the boundary's variables, both
/// read from the rows the boundary activation wrote. A node is never its own boundary, so a boundary's own bindings
/// resolve in its caller's scope.
/// </summary>
internal static class RuntimeClosedScope
{
    private const string FramePrefix = "activity-scope:";

    public static bool IsClosedBoundary(ActivityExecutionState state) =>
        state.Metadata.TryGetValue(RuntimeMetadataKeys.ActivityExecutionScope, out var scope) &&
        StringComparer.Ordinal.Equals(scope, RuntimeMetadataKeys.ActivityExecutionScopeClosed);

    /// <summary>Index of the innermost closed boundary in a root-first ancestor chain, or -1.</summary>
    public static int IndexOfNearest(IReadOnlyList<ActivityExecutionState> rootFirstAncestors)
    {
        for (var index = rootFirstAncestors.Count - 1; index >= 0; index--)
        {
            if (IsClosedBoundary(rootFirstAncestors[index]))
                return index;
        }

        return -1;
    }

    /// <summary>
    /// The read-only root frame of a closed scope: the boundary's variable rows under the boundary's placed node id,
    /// which is the scope placement remapped the body's variable references to. It is a projection of those rows,
    /// never stored; a capture writes the rows themselves.
    /// </summary>
    public static VariableFrameState CreateFrame(
        ActivityExecutionState boundary,
        IEnumerable<DurableValueState>? durableValues) => new(
        FrameId(boundary.InvocationId),
        boundary.Execution.ExecutableNodeId,
        boundary.InvocationId,
        parentFrameId: null,
        VariableFrameKind.Root,
        durableValues is null
            ? new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal)
            : RuntimeInputBindingStateProjection.ProjectBoundaryEnvelopes(
                durableValues,
                boundary.InvocationId,
                RuntimeInputBindingStateProjection.BoundaryVariableRole));

    /// <summary>
    /// Activity execution id of the nearest closed boundary above <paramref name="state"/> (never the state itself),
    /// walking scheduling parents through <paramref name="statesByInvocationId"/>; null outside every closed scope.
    /// </summary>
    public static string? NearestBoundaryId(
        ActivityExecutionState state,
        IReadOnlyDictionary<string, ActivityExecutionState> statesByInvocationId)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var parentId = state.ParentActivityExecutionId;
        while (!string.IsNullOrWhiteSpace(parentId) && visited.Add(parentId) &&
               statesByInvocationId.TryGetValue(parentId, out var parent))
        {
            if (IsClosedBoundary(parent))
                return parent.InvocationId;
            parentId = parent.ParentActivityExecutionId;
        }

        return null;
    }

    public static string FrameId(string boundaryActivityExecutionId) => $"{FramePrefix}{boundaryActivityExecutionId}";

    public static bool IsClosedScopeFrame(VariableFrameState frame) =>
        frame.Kind == VariableFrameKind.Root && frame.FrameId.StartsWith(FramePrefix, StringComparison.Ordinal);
}
