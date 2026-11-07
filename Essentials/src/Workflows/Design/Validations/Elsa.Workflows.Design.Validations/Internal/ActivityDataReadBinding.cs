using System.Text.Json;
using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Validations.Internal;

/// <summary>Reads existing authored roles without invoking serializers on arbitrary authored CLR values.</summary>
internal sealed record ActivityDataReadBinding(
    string Role,
    string Key = "",
    string? Scope = null,
    string? Projection = null,
    bool IsOptional = false,
    string? Path = null,
    string? Error = null)
{
    public static IEnumerable<ActivityDataReadBinding> Read(ArgumentValue? argument)
    {
        if (argument is null)
        {
            yield return new("", Error: "InvalidSource");
            yield break;
        }
        if (IsRole(argument.ExpressionType) || IsMemberRole(argument.ExpressionType))
        {
            yield return Direct(argument.ExpressionType, argument.Value);
            yield break;
        }

        if (new[] { "Literal", "Object", "Default" }.Contains(argument.ExpressionType, StringComparer.OrdinalIgnoreCase))
            yield break;

        if (argument.Value is ExpressionDefinition definition)
        {
            foreach (var parameter in definition.Parameters.Values)
            {
                var binding = Parameter(parameter);
                if (binding is not null) yield return binding;
            }
            yield break;
        }

        if (argument.Value is not JsonElement { ValueKind: JsonValueKind.Object } payload ||
            !TryProperty(payload, "language", out var language) || language.ValueKind != JsonValueKind.String ||
            !TryProperty(payload, "parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var parameter in parameters.EnumerateObject())
        {
            if (parameter.Value.ValueKind != JsonValueKind.Object) continue;
            var kind = String(parameter.Value, "kind");
            if (IsRole(kind) || IsMemberRole(kind))
                yield return Direct(kind!, parameter.Value, portable: true);
        }
    }

    public static ActivityDataReadBinding Direct(string? role, object? value, bool portable = false)
    {
        if (string.IsNullOrWhiteSpace(role)) return new("", Error: "InvalidSource");
        if (IsMemberRole(role)) return new(role, Error: "UnsupportedMemberBinding");
        var json = value is JsonElement element ? element : default;
        if (json.ValueKind == JsonValueKind.Object &&
            (HasProperty(json, "memberPath") ||
             (!role.Equals("WorkflowRequest", StringComparison.OrdinalIgnoreCase) && HasProperty(json, "path"))))
            return new(role, Error: "UnsupportedMemberSelection");

        if (role.Equals("Variable", StringComparison.OrdinalIgnoreCase))
        {
            if (portable && json.ValueKind == JsonValueKind.Object)
                return Required("Variable", String(json, "variableKey"), String(json, "declaringScopeNodeId"));
            return VariableReference.TryParse(value, out var reference) && reference is not null
                ? Required("Variable", reference.ReferenceKey, reference.DeclaringScopeId)
                : new("Variable", Error: "InvalidSource");
        }
        if (role.Equals("Input", StringComparison.OrdinalIgnoreCase))
            return VariableReference.TryParse(value, out var reference) && reference is not null
                ? Required("Input", reference.ReferenceKey, reference.DeclaringScopeId)
                : new("Input", Error: "InvalidSource");
        if (role.Equals("WorkflowRequest", StringComparison.OrdinalIgnoreCase))
            return Required("WorkflowRequest", String(json, "memberKey"), path: String(json, "path"));
        if (role.Equals("ActivityResult", StringComparison.OrdinalIgnoreCase))
        {
            var binding = Required("ActivityResult", String(json, "producerNodeId"), String(json, "producerScopeId"), String(json, "projectionKey"));
            return binding.Error is null && (string.IsNullOrWhiteSpace(binding.Projection) || (!portable && string.IsNullOrWhiteSpace(binding.Scope)))
                ? binding with { Error = "InvalidSource" }
                : binding with { IsOptional = json.ValueKind == JsonValueKind.Object && TryProperty(json, "isOptional", out var optional)
                    && optional.ValueKind == JsonValueKind.True };
        }
        return new(role, Error: "InvalidSource");
    }

    private static ActivityDataReadBinding? Parameter(ExpressionParameterBinding binding) => binding switch
    {
        WorkflowRequestExpressionParameterBinding request => new("WorkflowRequest", request.MemberKey, Path: request.Path),
        VariableExpressionParameterBinding variable => new("Variable", variable.VariableKey, variable.DeclaringScopeNodeId),
        ActivityResultExpressionParameterBinding result => new("ActivityResult", result.ProducerNodeId, Projection: result.ProjectionKey, IsOptional: result.IsOptional),
        _ => null
    };

    private static ActivityDataReadBinding Required(string role, string? key, string? scope = null, string? projection = null, string? path = null) =>
        string.IsNullOrWhiteSpace(key) ? new(role, Error: "InvalidSource") : new(role, key, scope, projection, Path: path);

    internal static string? String(JsonElement value, string key) =>
        TryProperty(value, key, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;

    // Portable expression JSON is admitted with the payload serializer's case-insensitive envelope
    // keys (kind, producerNodeId, memberKey, …). Literal memberPath segments stay ordinal.
    private static bool HasProperty(JsonElement value, string key) => TryProperty(value, key, out _);

    private static bool TryProperty(JsonElement value, string key, out JsonElement property)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            property = default;
            return false;
        }
        if (value.TryGetProperty(key, out property))
            return true;
        foreach (var candidate in value.EnumerateObject())
        {
            if (string.Equals(candidate.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }
        property = default;
        return false;
    }

    private static bool IsRole(string? role) => role is not null &&
        new[] { "Input", "WorkflowRequest", "Variable", "ActivityResult" }.Contains(role, StringComparer.OrdinalIgnoreCase);

    private static bool IsMemberRole(string? role) => role is not null &&
        new[] { "InputMember", "WorkflowRequestMember", "VariableMember", "ActivityResultMember" }.Contains(role, StringComparer.OrdinalIgnoreCase);
}
