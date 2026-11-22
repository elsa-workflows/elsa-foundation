using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Serialization.Core;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Reads and writes the Elsa 3 typed-value shape <c>{ typeName, expression: { type, value }, memoryReference: { id } }</c>
/// that every activity property carries.
/// </summary>
internal static class Elsa3TypedValues
{
    public const string Literal = "Literal";

    /// <summary>The expression type and value, when the property carries one.</summary>
    public static bool TryGetExpression(JsonElement value, out string type, out JsonElement expressionValue)
    {
        type = string.Empty;
        expressionValue = default;
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("expression", out var expression) ||
            expression.ValueKind != JsonValueKind.Object ||
            !expression.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String ||
            !expression.TryGetProperty("value", out expressionValue) ||
            expressionValue.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return false;

        type = typeElement.GetString() ?? string.Empty;
        return true;
    }

    /// <summary>The literal text, when the property is bound to a literal (or carries a bare JSON scalar).</summary>
    public static bool TryGetLiteral(JsonElement value, out string? literal)
    {
        literal = null;
        if (value.ValueKind == JsonValueKind.String)
        {
            literal = value.GetString();
            return true;
        }

        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number)
        {
            literal = value.GetRawText();
            return true;
        }

        if (!TryGetExpression(value, out var type, out var expressionValue) ||
            !string.Equals(type, Literal, StringComparison.OrdinalIgnoreCase))
            return false;

        literal = expressionValue.ValueKind == JsonValueKind.String ? expressionValue.GetString() : expressionValue.GetRawText();
        return true;
    }

    /// <summary>Whether the property is bound to a non-literal expression.</summary>
    public static bool IsExpression(JsonElement value) =>
        TryGetExpression(value, out var type, out _) &&
        !string.Equals(type, Literal, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a memory reference captures the property (an output written somewhere).</summary>
    public static bool IsCaptured(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("memoryReference", out var memory) &&
        memory.ValueKind == JsonValueKind.Object &&
        memory.TryGetProperty("id", out var id) &&
        id.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(id.GetString());

    /// <summary>
    /// Whether the property carries a value: a non-blank literal, an expression or a memory reference. Null, an empty
    /// string and an empty JSON array or object count as unbound.
    /// </summary>
    public static bool IsBound(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return false;
        if (IsCaptured(value))
            return true;
        if (TryGetLiteral(value, out var literal))
            return !IsEmptyLiteral(literal);
        return TryGetExpression(value, out _, out _);
    }

    /// <summary>Whether a literal is empty: null, blank, <c>null</c>, <c>[]</c> or <c>{}</c>.</summary>
    public static bool IsEmptyLiteral(string? literal) =>
        string.IsNullOrWhiteSpace(literal) || literal.Trim() is "null" or "[]" or "{}";

    /// <summary>The declared Elsa 3 type alias (last segment of <c>typeName</c>, without generic or assembly suffix).</summary>
    public static string? TypeAlias(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("typeName", out var typeName) ||
            typeName.ValueKind != JsonValueKind.String)
            return null;

        var alias = typeName.GetString() ?? string.Empty;
        var comma = alias.IndexOf(',');
        if (comma >= 0)
            alias = alias[..comma];
        var tick = alias.IndexOf('`');
        if (tick >= 0)
            alias = alias[..tick];
        var dot = alias.LastIndexOf('.');
        return dot >= 0 ? alias[(dot + 1)..] : alias;
    }

    /// <summary>A literal-bound typed value of type <paramref name="typeAlias"/>.</summary>
    public static JsonElement CreateLiteral(string typeAlias, string literal) =>
        JsonSerializer.SerializeToElement(new JsonObject
        {
            ["typeName"] = typeAlias,
            ["expression"] = new JsonObject { ["type"] = Literal, ["value"] = literal }
        }, JsonBaseline.Options);

    /// <summary>A literal-bound typed value of type <paramref name="typeAlias"/> holding <paramref name="literal"/> as JSON.</summary>
    public static JsonElement CreateLiteral(string typeAlias, JsonNode? literal) =>
        JsonSerializer.SerializeToElement(new JsonObject
        {
            ["typeName"] = typeAlias,
            ["expression"] = new JsonObject { ["type"] = Literal, ["value"] = literal }
        }, JsonBaseline.Options);

    /// <summary>A typed value of type <paramref name="typeAlias"/> bound to a literal null (spec 267).</summary>
    public static JsonElement CreateNull(string typeAlias) =>
        JsonSerializer.SerializeToElement(new JsonObject
        {
            ["typeName"] = typeAlias,
            ["expression"] = new JsonObject { ["type"] = Literal, ["value"] = null }
        }, JsonBaseline.Options);

    /// <summary>Whether the property is a literal holding null: <c>{ expression: { type: "Literal", value: null } }</c>.</summary>
    public static bool IsNullLiteral(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty("expression", out var expression) &&
        expression.ValueKind == JsonValueKind.Object &&
        expression.TryGetProperty("type", out var type) &&
        type.ValueKind == JsonValueKind.String &&
        string.Equals(type.GetString(), Literal, StringComparison.OrdinalIgnoreCase) &&
        expression.TryGetProperty("value", out var literal) &&
        literal.ValueKind == JsonValueKind.Null;

    /// <summary>A typed value bound to an expression of <paramref name="expressionType"/>.</summary>
    public static JsonElement CreateExpression(string typeAlias, string expressionType, string expression) =>
        JsonSerializer.SerializeToElement(new JsonObject
        {
            ["typeName"] = typeAlias,
            ["expression"] = new JsonObject { ["type"] = expressionType, ["value"] = expression }
        }, JsonBaseline.Options);

    /// <summary>The same typed value with its literal replaced by <paramref name="literal"/>.</summary>
    public static JsonElement WithLiteral(JsonElement value, string literal) =>
        WithExpression(value, Literal, JsonSerializer.SerializeToElement(literal, JsonBaseline.Options));

    internal static JsonElement WithoutMemoryReference(JsonElement value) => RewriteWrapper(value, null, default, removeMemoryReference: true);

    internal static JsonElement WithExpression(JsonElement value, string expressionType, JsonElement expressionValue, bool removeMemoryReference = false) =>
        RewriteWrapper(value, expressionType, expressionValue, removeMemoryReference);

    private static JsonElement RewriteWrapper(JsonElement value, string? expressionType, JsonElement expressionValue, bool removeMemoryReference)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException("An Elsa 3 typed value must be an object.");
        if (expressionType is null && !value.TryGetProperty("memoryReference", out _)) return value.Clone();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new() { Encoder = JsonBaseline.Options.Encoder }))
        {
            writer.WriteStartObject();
            var wroteExpression = false;
            foreach (var property in value.EnumerateObject())
            {
                if (removeMemoryReference && property.Name == "memoryReference") continue;
                writer.WritePropertyName(property.Name);
                if (expressionType is not null && property.Name == "expression")
                {
                    WriteExpression(writer, property.Value); wroteExpression = true;
                }
                else writer.WriteRawValue(property.Value.GetRawText());
            }
            if (expressionType is not null && !wroteExpression)
            {
                writer.WritePropertyName("expression"); WriteExpression(writer, default);
            }
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();

        void WriteExpression(Utf8JsonWriter writer, JsonElement original)
        {
            writer.WriteStartObject();
            writer.WriteString("type", expressionType);
            writer.WritePropertyName("value"); writer.WriteRawValue(expressionValue.GetRawText());
            if (original.ValueKind == JsonValueKind.Object)
                foreach (var property in original.EnumerateObject().Where(property => property.Name is not ("type" or "value")))
                { writer.WritePropertyName(property.Name); writer.WriteRawValue(property.Value.GetRawText()); }
            writer.WriteEndObject();
        }
    }
}
