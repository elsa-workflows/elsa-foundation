using System.Text;
using System.Text.Json;

namespace Elsa.Agent.Specialists.JsonSchema;

internal static class SpecialistJsonBounds
{
    public static void Check(JsonElement value, int maximumBytes, int maximumNodes)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            throw Invalid("$", "The JSON value is missing.");
        var raw = value.GetRawText();
        if (raw.Length > maximumBytes || Encoding.UTF8.GetByteCount(raw) > maximumBytes)
            throw Invalid("$", "The JSON value exceeds the profile byte limit.");
        var remaining = maximumNodes;
        Visit(value, "$", 0, ref remaining);
    }

    private static void Visit(JsonElement value, string path, int depth, ref int remaining)
    {
        if (depth > SpecialistSchemaValidator.MaximumDepth || --remaining < 0)
            throw Invalid(path, "The JSON value exceeds the profile depth or node limit.");
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new SpecialistSchemaException(path, "specialist.json.duplicate", "JSON object member names must be unique.");
                    Visit(property.Value, SpecialistSchemaValidator.Path(path, property.Name), depth + 1, ref remaining);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    Visit(item, SpecialistSchemaValidator.Path(path, (index++).ToString(System.Globalization.CultureInfo.InvariantCulture)), depth + 1, ref remaining);
                break;
            case JsonValueKind.Number:
                var raw = value.GetRawText();
                var exponentAt = raw.IndexOfAny(['e', 'E']);
                if (raw.Length > 128 || !value.TryGetDouble(out var number) || !double.IsFinite(number) ||
                    exponentAt >= 0 && (!int.TryParse(raw.AsSpan(exponentAt + 1), System.Globalization.NumberStyles.AllowLeadingSign,
                        System.Globalization.CultureInfo.InvariantCulture, out var exponent) || exponent is < -1_024 or > 1_024))
                    throw Invalid(path, "The JSON number exceeds the finite numeric profile.");
                break;
        }
    }

    private static SpecialistSchemaException Invalid(string path, string message) => new(path, "specialist.json.limit", message);
}
