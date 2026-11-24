using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Mappings;
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

        // Keep authored objects until the final baseline write. A JsonNode intermediate
        // rewrites carried JsonElement tokens when its containing object is emitted.
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in structure.Payload.InitialPayload)
            payload[key] = value;

        foreach (var slot in structure.Payload.Slots)
        {
            var children = plan.Slots.Where(child => StringComparer.Ordinal.Equals(child.SlotName, slot.Name)).ToArray();
            if (slot.CollectionProperty is { } collection)
            {
                var items = new List<Dictionary<string, object?>>();
                foreach (var child in children)
                {
                    var item = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [slot.LabelProperty ?? "name"] = child.Label,
                        [slot.ChildProperty ?? "activity"] = Node(child.Child, mapped)
                    };
                    // A slot whose items carry an identity (Switch caseId) and a condition (a MatchAny case) gets them
                    // from the rewrite; the condition was lowered on the node like any other expression.
                    if (slot.ItemIdProperty is { } itemIdProperty && child.ItemId is { } itemId)
                        item[itemIdProperty] = itemId;
                    if (slot.ConditionProperty is { } conditionProperty &&
                        child.ConditionProperty is { } property &&
                        plan.Node.AdditionalProperties.TryGetValue(property, out var condition) &&
                        Elsa3ActivityToState.TryGetArgument(property, condition, out var argument))
                        item[conditionProperty] = argument;
                    items.Add(item);
                }
                payload[collection] = items;
            }
            else if (string.Equals(slot.Cardinality, "Many", StringComparison.OrdinalIgnoreCase))
            {
                payload[slot.Property] = children.Select(child => Node(child.Child, mapped)).OfType<ActivityNode>().ToArray();
            }
            else
            {
                payload[slot.Property] = children.Length == 0 ? null : Node(children[0].Child, mapped);
            }
        }

        if (structure.Payload.SupportsScopedVariables)
            payload["variables"] = plan.Variables;

        foreach (var (key, value) in plan.StructureFields)
            payload[key] = value;

        return new ActivityNodeStructure(structure.Kind, structure.SchemaVersion, JsonSerializer.SerializeToElement(payload, PayloadJsonOptions));
    }

    private static ActivityNode? Node(Elsa3Activity? child, IReadOnlyDictionary<Elsa3Activity, ActivityNode> mapped) =>
        child is not null && mapped.TryGetValue(child, out var node)
            ? node
            : null;

    private static JsonSerializerOptions CreatePayloadJsonOptions()
    {
        var options = new JsonSerializerOptions(Elsa.Serialization.Core.JsonBaseline.Options);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
