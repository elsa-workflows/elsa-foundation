using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Expressions.Api.Models;

[JsonConverter(typeof(ExpressionEditingModeViewJsonConverter))]
public enum ExpressionEditingModeView
{
    Literal,
    Text,
    Structured,
    Reference
}

internal sealed class ExpressionEditingModeViewJsonConverter : JsonConverter<ExpressionEditingModeView>
{
    public override ExpressionEditingModeView Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetString() switch
        {
            "literal" => ExpressionEditingModeView.Literal,
            "text" => ExpressionEditingModeView.Text,
            "structured" => ExpressionEditingModeView.Structured,
            "reference" => ExpressionEditingModeView.Reference,
            var value => throw new JsonException($"Unknown expression editing mode '{value}'.")
        };

    public override void Write(Utf8JsonWriter writer, ExpressionEditingModeView value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            ExpressionEditingModeView.Literal => "literal",
            ExpressionEditingModeView.Text => "text",
            ExpressionEditingModeView.Structured => "structured",
            ExpressionEditingModeView.Reference => "reference",
            _ => throw new JsonException($"Unknown expression editing mode '{value}'.")
        });
}

public sealed record ExpressionDescriptorView(
    string Type,
    string DisplayName,
    string? Description,
    ExpressionEditingModeView EditingMode);

public sealed record ExpressionDescriptorsResponse(IReadOnlyCollection<ExpressionDescriptorView> Items);

public sealed record VariableTypeDescriptorView(
    string Alias,
    string DisplayName,
    string? Category,
    string? DefaultEditor);

public sealed record VariableTypeDescriptorsResponse(IReadOnlyCollection<VariableTypeDescriptorView> Items);
