using System.Text.Json;

namespace Elsa.Agent.Specialists.Connections;

// Conservative shared SDK request shape. The original full Elsa schema remains authoritative for
// acceptance, including numeric constraints the SDK may express only as model instructions.
internal static class SpecialistProviderSchema
{
    internal const string Profile = "elsa.specialist.structured-object/1";
    internal static bool Supports(JsonElement schema)
    {
        try
        {
            var properties = 0;
            return schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("type", out var type) && type.ValueEquals("object") && Visit(schema, 0, ref properties);
        }
        catch (InvalidOperationException) { return false; }
    }

    private static bool Visit(JsonElement schema, int depth, ref int count)
    {
        if (depth > 5 || schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out var type)) return false;
        var types = type.ValueKind == JsonValueKind.String ? new[] { type.GetString()! } :
            type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray() : [];
        if (types.Length is < 1 or > 2 || types.Distinct(StringComparer.Ordinal).Count() != types.Length ||
            types.Any(x => x is not ("object" or "array" or "string" or "number" or "integer" or "boolean" or "null")) ||
            types.Length == 2 && !types.Contains("null")) return false;
        foreach (var property in schema.EnumerateObject())
            if (property.Name is not ("$schema" or "type" or "properties" or "required" or "additionalProperties" or "items" or "enum" or
                "minItems" or "maxItems" or "minLength" or "maxLength" or "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum" or
                "multipleOf" or "title" or "description")) return false;
        if (types.Contains("object"))
        {
            if (!schema.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False ||
                !schema.TryGetProperty("properties", out var members) || members.ValueKind != JsonValueKind.Object ||
                !schema.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array) return false;
            var names = members.EnumerateObject().Select(x => x.Name).ToArray();
            var declared = required.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();
            if (!names.Order(StringComparer.Ordinal).SequenceEqual(declared.Order(StringComparer.Ordinal)) || (count += names.Length) > 100) return false;
            foreach (var member in members.EnumerateObject()) if (!Visit(member.Value, depth + 1, ref count)) return false;
        }
        if (types.Contains("array") && (!schema.TryGetProperty("items", out var items) || !Visit(items, depth + 1, ref count))) return false;
        return true;
    }
}
