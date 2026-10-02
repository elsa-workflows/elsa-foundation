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
/// publication's projector) project no children from it, and publication compiles it as an opaque structure (spec 071
/// FR-008). Registering a handler is not the way to reach these children: it would make every design validator, the
/// design commands' tree walks and the publishing compiler start processing them, and change what publication emits.
/// The extension that writes the shape therefore enumerates it itself.
/// </remarks>
public static class Elsa3ImportedActivityStructure
{
    /// <summary>The structure kind of a mapped Elsa 3 activity with child activities.</summary>
    public const string Kind = "elsa3.imported-activity.structure";

    /// <summary>The schema version of the structure payload.</summary>
    public const string SchemaVersion = "1.0.0";

    private const string ActivitiesProperty = "activities";

    /// <summary>The structure holding <paramref name="children"/>, or null when there are none.</summary>
    public static ActivityNodeStructure? Create(IReadOnlyList<ActivityNode> children) =>
        children.Count == 0
            ? null
            : new ActivityNodeStructure(Kind, SchemaVersion, JsonSerializer.SerializeToElement(new { activities = children }));

    /// <summary>
    /// <paramref name="root"/> and every node nested under it through this structure, at any depth, each read back from
    /// its parent's payload with the serializer options it was written with. A node with no structure, or with another
    /// kind, contributes no children.
    /// </summary>
    public static IEnumerable<ActivityNode> Nodes(ActivityNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var pending = new Stack<ActivityNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            yield return node;
            foreach (var child in Children(node))
                pending.Push(child);
        }
    }

    private static IEnumerable<ActivityNode> Children(ActivityNode node) =>
        node.Structure is { } structure &&
        StringComparer.Ordinal.Equals(structure.Kind, Kind) &&
        StringComparer.Ordinal.Equals(structure.SchemaVersion, SchemaVersion)
            ? structure.Payload.GetProperty(ActivitiesProperty).Deserialize<ActivityNode[]>() ?? []
            : [];
}
