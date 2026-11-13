using System.Text.Json;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Prevents complete-source authority from being discarded by a legacy typed payload reader.</summary>
internal static class BpmnLegacyStructureGuard
{
    private static readonly HashSet<string> CompleteSourceFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "documentXml", "processId", "bindings", "scopeHeaders"
    };

    public static void RejectCompleteSourceFields(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return; // The owning typed reader retains its existing malformed-payload handling.

        // Only the structure's own authority fields matter. Native literal values and opaque
        // metadata may legitimately contain these names and must never be recursively classified.
        foreach (var property in payload.EnumerateObject())
            if (CompleteSourceFields.Contains(property.Name))
                throw new JsonException($"Legacy BPMN schema 1.0.0 cannot contain complete-source field '{property.Name}'; the source must retain its declared authoring schema before typed deserialization.");
    }
}
