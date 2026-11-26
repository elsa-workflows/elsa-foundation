using System.Text.Json;

namespace Elsa.Expressions.Core.Models;

public sealed record VariableReference(string ReferenceKey, string? DeclaringScopeId = null)
{
    public const string WorkflowScopeId = "workflow";

    public bool IsWorkflowScope =>
        string.IsNullOrEmpty(DeclaringScopeId) ||
        string.Equals(DeclaringScopeId, WorkflowScopeId, StringComparison.Ordinal);

    public static bool TryParse(object? value, out VariableReference? reference)
    {
        reference = value switch
        {
            VariableReference variableReference => variableReference,
            string referenceKey => new VariableReference(referenceKey),
            JsonElement { ValueKind: JsonValueKind.String } jsonElement => new VariableReference(jsonElement.GetString() ?? string.Empty),
            JsonElement { ValueKind: JsonValueKind.Object } jsonElement => ParseJsonObject(jsonElement),
            _ => null
        };

        return reference is not null && !string.IsNullOrEmpty(reference.ReferenceKey);
    }

    private static VariableReference? ParseJsonObject(JsonElement jsonElement)
    {
        if (!TryGetStringProperty(jsonElement, "referenceKey", out var referenceKey) &&
            !TryGetStringProperty(jsonElement, nameof(ReferenceKey), out referenceKey))
            return null;

        if (string.IsNullOrEmpty(referenceKey))
            return null;

        if (!TryGetStringProperty(jsonElement, "declaringScopeId", out var declaringScopeId) &&
            !TryGetStringProperty(jsonElement, nameof(DeclaringScopeId), out declaringScopeId))
            declaringScopeId = null;

        return new VariableReference(referenceKey, declaringScopeId);
    }

    private static bool TryGetStringProperty(JsonElement jsonElement, string propertyName, out string? value)
    {
        value = null;

        if (!jsonElement.TryGetProperty(propertyName, out var property))
            return false;

        value = property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
        return true;
    }
}
