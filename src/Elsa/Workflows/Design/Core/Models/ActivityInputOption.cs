using System.Text.Json;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// A labeled JSON-scalar value offered by a design-time activity input options provider.
/// </summary>
public sealed record ActivityInputOption
{
    public ActivityInputOption(string label, object value)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("An activity input option label is required.", nameof(label));
        ArgumentNullException.ThrowIfNull(value);

        var normalizedValue = value.GetType().IsEnum ? value.ToString()! : value;
        var element = normalizedValue is JsonElement jsonElement
            ? jsonElement.Clone()
            : JsonSerializer.SerializeToElement(normalizedValue, normalizedValue.GetType());

        if (element.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False))
            throw new ArgumentException("An activity input option value must be a JSON string, number, or boolean.", nameof(value));

        Label = label;
        Value = element;
    }

    public string Label { get; }
    public JsonElement Value { get; }
}
