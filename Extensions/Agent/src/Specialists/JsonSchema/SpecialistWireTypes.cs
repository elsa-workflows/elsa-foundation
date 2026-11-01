using System.Text.Json;
using Elsa.Primitives.Models;

namespace Elsa.Agent.Specialists.JsonSchema;

internal sealed record SpecialistJsonMember(string Name, string Alias, CollectionKind Collection, bool Required, bool Nullable);

// Schema validity and conversion into Elsa's declared CLR boundary are separate requirements.
// Match the ordinary activity hydrator's System.Text.Json conversion; never normalize the accepted JSON.
internal static class SpecialistWireTypes
{
    public static IReadOnlyList<SpecialistSchemaProblem> Validate(JsonElement value, IEnumerable<SpecialistJsonMember> members)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return [new("$", "specialist.value.object", "Specialist business values must be a JSON object.")];
        var problems = new List<SpecialistSchemaProblem>();
        foreach (var member in members)
        {
            var path = SpecialistSchemaValidator.Path("$", member.Name);
            if (!value.TryGetProperty(member.Name, out var field))
            {
                if (member.Required) problems.Add(new(path, "specialist.value.required", "A required business member is absent."));
                continue;
            }
            if (field.ValueKind == JsonValueKind.Null)
            {
                if (!member.Nullable) problems.Add(new(path, "specialist.value.null", "This business member does not allow null."));
                continue;
            }
            if (!CanConvert(field, member.Alias, member.Collection))
                problems.Add(new(path, "specialist.value.type", "The business value cannot be represented by its declared native type."));
        }
        return problems;
    }

    internal static bool CanConvert(JsonElement value, string alias, CollectionKind collection)
    {
        if (collection is CollectionKind.Array or CollectionKind.List)
            return value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(x => CanConvertItem(x, alias));
        if (collection == CollectionKind.Dictionary)
            return value.ValueKind == JsonValueKind.Object && value.EnumerateObject().All(x => CanConvertItem(x.Value, alias));
        if (collection != CollectionKind.Single) return false;
        var type = alias switch
        {
            "String" => typeof(string), "Boolean" => typeof(bool), "Int32" => typeof(int), "Int64" => typeof(long),
            "Double" => typeof(double), "Decimal" => typeof(decimal), "DateTime" => typeof(DateTime),
            "DateTimeOffset" => typeof(DateTimeOffset), "TimeSpan" => typeof(TimeSpan), "Guid" => typeof(Guid),
            "Object" or "JsonElement" or "System.Text.Json.JsonElement" => typeof(JsonElement), _ => null
        };
        if (type is null) return false;
        try
        {
            var result = value.Deserialize(type);
            return result is not double number || double.IsFinite(number);
        }
        catch (JsonException) { return false; }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
    }

    private static bool CanConvertItem(JsonElement item, string alias) =>
        item.ValueKind == JsonValueKind.Null
            ? alias is "Object" or "JsonElement" or "System.Text.Json.JsonElement"
            : CanConvert(item, alias, CollectionKind.Single);
}
