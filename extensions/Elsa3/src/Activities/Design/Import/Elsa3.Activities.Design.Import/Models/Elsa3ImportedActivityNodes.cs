using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa3.Activities.Design.Import.Models;

/// <summary>
/// Enumerates every activity node of an imported Elsa 3 tree. The mapping gives containers the native structure of
/// their Elsa 4 activity (Sequence, If, loops, Parallel, Switch), whose payloads hold child nodes in different shapes.
/// This walk finds the child nodes in any native payload without a structure handler, so the collection import can
/// judge every node the commit would store whatever handlers the host registers.
/// </summary>
public static class Elsa3ImportedActivityNodes
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = CreatePayloadJsonOptions();

    /// <summary>
    /// <paramref name="root"/> and every node nested under it, each exactly once, in document order (a node, then each
    /// of its children with their own descendants, in payload order), each read back from its parent's payload.
    /// </summary>
    public static IEnumerable<ActivityNode> Walk(ActivityNode root)
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

    /// <summary>The nodes a structure payload holds directly, in payload order; their own children are theirs.</summary>
    private static IReadOnlyList<ActivityNode> Children(ActivityNode node)
    {
        if (node.Structure is not { } structure)
            return [];
        var children = new List<ActivityNode>();
        Collect(structure.Payload, children);
        return children;
    }

    private static void Collect(JsonElement element, List<ActivityNode> children)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object when IsNode(element):
                if (element.Deserialize<ActivityNode>(PayloadJsonOptions) is { } child)
                    children.Add(child);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Collect(property.Value, children);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, children);
                break;
        }
    }

    private static bool IsNode(JsonElement element) =>
        element.TryGetProperty("nodeId", out var nodeId) && nodeId.ValueKind == JsonValueKind.String &&
        element.TryGetProperty("activityVersionId", out var versionId) && versionId.ValueKind == JsonValueKind.String;

    private static JsonSerializerOptions CreatePayloadJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
