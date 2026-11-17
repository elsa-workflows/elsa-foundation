using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// The nearest closed activity execution scope above a node, and the view its body reads. A closed
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
        IEnumerable<DurableValueState>? durableValues)
    {
        var rows = durableValues?.ToArray() ?? [];
        return new(
            FrameId(boundary.InvocationId),
            boundary.Execution.ExecutableNodeId,
            boundary.InvocationId,
            parentFrameId: null,
            VariableFrameKind.Root,
            RuntimeInputBindingStateProjection.ProjectBoundaryEnvelopes(rows, boundary.InvocationId, ActivityExecutionScopeValues.VariableRole),
            versions: RowVersions(rows, boundary.InvocationId));
    }

    /// <summary>
    /// The versions a closed scope's variable rows carry (spec 1221 T003): read through this frame, a body node's read
    /// set records them, and a capture into a row is judged against them, as for a frame.
    /// </summary>
    public static IReadOnlyDictionary<string, VariableVersion>? RowVersions(IEnumerable<DurableValueState> rows, string boundaryActivityExecutionId)
    {
        Dictionary<string, VariableVersion>? versions = null;
        foreach (var row in rows.Where(row => StringComparer.Ordinal.Equals(row.SourceActivityExecutionId, boundaryActivityExecutionId)))
        {
            if (!row.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryReferenceKey, out var key) ||
                !row.Metadata.TryGetValue(RuntimeMetadataKeys.RowVariableVersion, out var raw) ||
                !long.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var version))
                continue;
            var writer = row.Metadata.TryGetValue(RuntimeMetadataKeys.RowVariableWriter, out var json)
                ? System.Text.Json.JsonSerializer.Deserialize<VariableWriter>(json)
                : null;
            (versions ??= new Dictionary<string, VariableVersion>(StringComparer.Ordinal))[key] =
                new VariableVersion(version, row.Metadata.GetValueOrDefault(RuntimeMetadataKeys.RowVariableDigest), writer);
        }

        return versions;
    }

    /// <summary>The row metadata that stores <paramref name="version"/> (spec 1221 T003).</summary>
    public static IReadOnlyDictionary<string, string> WithRowVersion(IReadOnlyDictionary<string, string> metadata, VariableVersion version, bool hidesValue = false)
    {
        var result = metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        result[RuntimeMetadataKeys.RowVariableVersion] = version.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Row metadata is not scanned as a value: a hidden value keeps no digest there.
        if (version.Digest is not null && !hidesValue)
            result[RuntimeMetadataKeys.RowVariableDigest] = version.Digest;
        if (version.Writer is not null)
            result[RuntimeMetadataKeys.RowVariableWriter] = System.Text.Json.JsonSerializer.Serialize(version.Writer);
        return result;
    }

    /// <summary>
    /// The request a body node reads below <paramref name="boundary"/>: the boundary's captured inputs by reference key,
    /// and an explicit null for every input its pinned contract declares that was neither passed nor defaulted, so an
    /// optional input reads as null instead of being unavailable.
    /// </summary>
    public static IReadOnlyDictionary<string, ValueEnvelope> InputEnvelopes(
        ActivityExecutionState boundary,
        IEnumerable<DurableValueState> durableValues,
        WorkflowExecutable executable)
    {
        var envelopes = RuntimeInputBindingStateProjection.ProjectBoundaryEnvelopes(
                durableValues, boundary.InvocationId, ActivityExecutionScopeValues.InputRole)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (executable.NodesById.TryGetValue(boundary.Execution.ExecutableNodeId, out var node) &&
            node.ActivityContract is { } contract)
        {
            foreach (var input in contract.Inputs.Values.Where(input => !envelopes.ContainsKey(input.Key)))
                envelopes[input.Key] = ValueEnvelope.Null(input.Type, ValueProtectionPolicy.InstanceInline);
        }

        return envelopes;
    }

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

    /// <summary>
    /// Also addresses the closed scope's root variables under the <c>workflow</c> scope. A closed body never refers to
    /// <c>workflow</c> (placement remaps it to the boundary), but the body of a graph compiled before closed scopes and
    /// nested in a closed one does: its root, the caller it reads, is the enclosing closed scope.
    /// </summary>
    public static void AliasWorkflowScope(VariableFrameState rootFrame, IDictionary<RuntimeVariableValueAddress, ValueEnvelope> values)
    {
        foreach (var (key, value) in rootFrame.Values)
            values.TryAdd(new RuntimeVariableValueAddress(Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId, key), value);
    }

    public static string FrameId(string boundaryActivityExecutionId) => $"{FramePrefix}{boundaryActivityExecutionId}";

    public static bool IsClosedScopeFrame(VariableFrameState frame) =>
        frame.Kind == VariableFrameKind.Root && frame.FrameId.StartsWith(FramePrefix, StringComparison.Ordinal);
}
