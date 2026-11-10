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
    // Match the publication compiler's descriptor payload profile, including case-insensitive
    // properties, last-wins aliases and polymorphic metadata in canonicalized object order.
    // Only existing expression contract types are deserialized; arbitrary authored CLR values
    // must never be serialized or inspected through their getters here.
    private static readonly JsonSerializerOptions PortableExpressionOptions = new(JsonSerializerDefaults.Web)
        { AllowOutOfOrderMetadataProperties = true };

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

        if (new[] { "Literal", "Object", "Default", "Secret", "Connection" }.Contains(argument.ExpressionType, StringComparer.OrdinalIgnoreCase))
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

        if (argument.Value is not null and not string and not JsonElement)
        {
            yield return new("", Error: "UnsupportedClrExpressionPayload");
            yield break;
        }

        if (argument.Value is not JsonElement { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("language", out _) ||
            !payload.TryGetProperty("source", out _) ||
            !payload.TryGetProperty("capabilityProfile", out _))
            yield break;

        var portable = ReadPortable(payload);
        if (portable is null)
        {
            yield return new("", Error: "InvalidSource");
            yield break;
        }
        // Preserve the existing whole-role selection refusal on the effective parameters object.
        // The serializer chooses the last case-insensitive property alias; dictionary keys remain exact.
        var parameters = payload.EnumerateObject().Last(property =>
            property.Name.Equals("parameters", StringComparison.OrdinalIgnoreCase)).Value;
        foreach (var parameter in portable.Parameters)
        {
            var binding = Parameter(parameter.Value);
            if (binding is null) continue;
            if (parameters.TryGetProperty(parameter.Key, out var raw) && raw.ValueKind == JsonValueKind.Object &&
                (raw.TryGetProperty("memberPath", out _) ||
                 binding.Role != "WorkflowRequest" && raw.TryGetProperty("path", out _)))
                binding = binding with { Error = "UnsupportedMemberSelection" };
            yield return binding;
        }
    }

    private static ExpressionDefinition? ReadPortable(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<ExpressionDefinition>(PortableExpressionOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            // A payload admitted as portable by the compiler cannot silently become an opaque
            // expression here when its owned contract or discriminator is malformed.
            return null;
        }
    }

    public static ActivityDataReadBinding Direct(string? role, object? value, bool portable = false)
    {
        if (string.IsNullOrWhiteSpace(role)) return new("", Error: "InvalidSource");
        if (IsMemberRole(role)) return new(role, Error: "UnsupportedMemberBinding");
        var json = value is JsonElement element ? element : default;
        if (json.ValueKind == JsonValueKind.Object &&
            (json.TryGetProperty("memberPath", out _) ||
             (!role.Equals("WorkflowRequest", StringComparison.OrdinalIgnoreCase) && json.TryGetProperty("path", out _))))
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
                : binding with { IsOptional = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("isOptional", out var optional)
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
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() : null;

    private static bool IsRole(string? role) => role is not null &&
        new[] { "Input", "WorkflowRequest", "Variable", "ActivityResult" }.Contains(role, StringComparer.OrdinalIgnoreCase);

    private static bool IsMemberRole(string? role) => role is not null &&
        new[] { "InputMember", "WorkflowRequestMember", "VariableMember", "ActivityResultMember" }.Contains(role, StringComparer.OrdinalIgnoreCase);
}
