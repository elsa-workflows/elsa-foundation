using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Builds a container's native Elsa 4 structure from its target version's structure design facet (research R-6): the
/// facet's kind, schema and initial payload, with each slot filled from the children normalization routed to it. A
/// single slot holds one node or null, a many slot a list, and a collection slot a list of
/// <c>{ label, child }</c> items (Parallel branches, Switch cases). Payloads are written the way the native structure
/// handlers read them: web (camelCase) JSON with camelCase enum strings.
/// </summary>
internal static class Elsa3NativeStructureBuilder
{
    internal static readonly JsonSerializerOptions PayloadJsonOptions = CreatePayloadJsonOptions();

    public static ActivityNodeStructure? Build(Elsa3NodePlan plan, IReadOnlyDictionary<Elsa3Activity, ActivityNode> mapped)
    {
        var structure = plan.Resolved?.Structure;
        if (structure is null)
            return null;

        var payload = new JsonObject();
        foreach (var (key, value) in structure.Payload.InitialPayload)
            payload[key] = value is null ? null : JsonSerializer.SerializeToNode(value, PayloadJsonOptions);

        foreach (var slot in structure.Payload.Slots)
        {
            var children = plan.Slots.Where(child => StringComparer.Ordinal.Equals(child.SlotName, slot.Name)).ToArray();
            if (slot.CollectionProperty is { } collection)
            {
                var items = new JsonArray();
                foreach (var child in children)
                {
                    items.Add(new JsonObject
                    {
                        [slot.LabelProperty ?? "name"] = child.Label,
                        [slot.ChildProperty ?? "activity"] = Node(child.Child, mapped)
                    });
                }
                payload[collection] = items;
            }
            else if (string.Equals(slot.Cardinality, "Many", StringComparison.OrdinalIgnoreCase))
            {
                payload[slot.Property] = new JsonArray(children.Select(child => Node(child.Child, mapped)).OfType<JsonNode>().ToArray());
            }
            else
            {
                payload[slot.Property] = children.Length == 0 ? null : Node(children[0].Child, mapped);
            }
        }

        if (structure.Payload.SupportsScopedVariables)
            payload["variables"] = JsonSerializer.SerializeToNode(plan.Variables, PayloadJsonOptions);

        foreach (var (key, value) in plan.StructureFields)
            payload[key] = value is null ? null : JsonSerializer.SerializeToNode(value, PayloadJsonOptions);

        return new ActivityNodeStructure(structure.Kind, structure.SchemaVersion, JsonSerializer.SerializeToElement(payload, PayloadJsonOptions));
    }

    private static JsonNode? Node(Elsa3Activity? child, IReadOnlyDictionary<Elsa3Activity, ActivityNode> mapped) =>
        child is not null && mapped.TryGetValue(child, out var node)
            ? JsonSerializer.SerializeToNode(node, PayloadJsonOptions)
            : null;

    private static JsonSerializerOptions CreatePayloadJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
