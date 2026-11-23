using System.Text.Json;
using Elsa.Serialization.Core;

namespace Elsa.Expressions.Core.Models;

public sealed record ArgumentValue(
    JsonElement? Value,
    string? ExpressionType = null
)
{
    private JsonElement? _value = Snapshot(Value);

    /// <summary>The carried JSON, detached from its producer's document lifetime.</summary>
    public JsonElement? Value
    {
        get => _value;
        init => _value = Snapshot(value);
    }

    /// <summary>Converts a producer's CLR literal through its declared type contract before it enters an authored document.</summary>
    public static ArgumentValue FromClr(object? value, string? expressionType = null, Type? declaredType = null) =>
        value switch
        {
            null => new(null, expressionType),
            JsonElement raw => new(raw, expressionType),
            JsonDocument document => new(document.RootElement, expressionType),
            _ => new(JsonConversionContract.SerializeToElement(value, declaredType), expressionType)
        };

    // Retain the existing null/Undefined binding states. The serializer still refuses an undefined JSON value.
    private static JsonElement? Snapshot(JsonElement? value) =>
        value is { ValueKind: not JsonValueKind.Undefined } raw ? raw.Clone() : value;
}
