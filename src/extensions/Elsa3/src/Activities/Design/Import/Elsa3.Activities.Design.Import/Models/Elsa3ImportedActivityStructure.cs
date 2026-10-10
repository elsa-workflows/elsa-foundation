using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa3.Activities.Design.Import.Models;

/// <summary>
/// The structure the Elsa 3 mapping gives an activity node that has child activities: kind <see cref="Kind"/>, schema
/// <see cref="SchemaVersion"/>, whose payload holds the mapped children as <c>{ "activities": [ ... ] }</c>, each child
/// an <see cref="ActivityNode"/> that may carry the same structure in turn. This is the one definition of that shape:
/// the mapping writes it through <see cref="Create"/> and the collection import reads it back through
/// <see cref="Nodes"/>.
/// </summary>
/// <remarks>
/// No structure handler is registered for this kind, so the shared tree walkers (the design validators' walker and
/// publication's projector) project no children from it, and the executable compiler compiles it as an opaque structure
/// (spec 071 FR-008), because no activity's catalog design facets declare this kind; one that did would make publication
/// refuse the node instead. Registering a handler is not the way to reach these children: it would make every design
/// validator, the design commands' tree walks and the publishing compiler start processing them, and change what
/// publication emits. The extension that writes the shape therefore enumerates it itself.
/// </remarks>
public static class Elsa3ImportedActivityStructure
{
    /// <summary>The structure kind of a mapped Elsa 3 activity with child activities.</summary>
    public const string Kind = "elsa3.imported-activity.structure";

    /// <summary>The schema version of the structure payload.</summary>
    public const string SchemaVersion = "1.0.0";

    /// <summary>
    /// The deepest an activity may sit below its workflow's root, counted in containers (the root is at depth 0); the
    /// mapping refuses a deeper one. This payload, the stored workflow state (as the design stores' payload serializer
    /// writes and reads it) and a reusable activity's descriptor payload all keep the System.Text.Json default nesting limit
    /// of 64, and the import's boundary test proves those three at this depth. An activity at depth <c>d</c> sits
    /// <c>2 + 4d</c> levels down in a stored state and its binding's value three levels further, so at 14 a binding with a
    /// text, number or flat object value still fits; at 15 it does not. A binding value that is itself deeply nested can
    /// exceed the limit at any depth: the apply then fails with a 500 before anything is committed.
    /// </summary>
    public const int MaxNestingDepth = 14;

    private const string ActivitiesProperty = "activities";

    /// <summary>The structure holding <paramref name="children"/>, or null when there are none.</summary>
    public static ActivityNodeStructure? Create(IReadOnlyList<ActivityNode> children) =>
        children.Count == 0
            ? null
            : new ActivityNodeStructure(Kind, SchemaVersion, JsonSerializer.SerializeToElement(new { activities = children }));

    /// <summary>
    /// <paramref name="root"/> and every node nested under it through this structure, each exactly once, in document
    /// order (a node, then each of its children with their own descendants, sibling by sibling), each read back from its
    /// parent's payload with the serializer options it was written with. A node with no structure, or with another kind,
    /// contributes no children.
    /// </summary>
    public static IEnumerable<ActivityNode> Nodes(ActivityNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return EnumerateNodes(root);
    }

    private static IEnumerable<ActivityNode> EnumerateNodes(ActivityNode root)
    {
        var pending = new Stack<ActivityNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            yield return node;
            // Pushed last child first, so the children pop in document order.
            var children = Children(node);
            for (var index = children.Count - 1; index >= 0; index--)
                pending.Push(children[index]);
        }
    }

    private static IReadOnlyList<ActivityNode> Children(ActivityNode node) =>
        node.Structure is { } structure &&
        StringComparer.Ordinal.Equals(structure.Kind, Kind) &&
        StringComparer.Ordinal.Equals(structure.SchemaVersion, SchemaVersion)
            ? structure.Payload.GetProperty(ActivitiesProperty).Deserialize<ActivityNode[]>() ?? []
            : [];
}
