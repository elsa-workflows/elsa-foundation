using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;

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
    /// <summary>
    /// True for a result an input binds to directly, false for one an expression reads as a parameter. A direct
    /// binding without a <see cref="Scope"/> names only the producer.
    /// </summary>
    public bool IsDirect { get; init; }

    /// <summary>The exact member segments of a member read; empty for a whole-value read.</summary>
    public ImmutableArray<string> MemberPath { get; init; } = [];

    /// <summary>Whether this reads one member of its source rather than the whole value.</summary>
    public bool IsMemberRead => Role is "ActivityResultMember" or "VariableMember";

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

    public static ActivityDataReadBinding Direct(string? role, object? value)
    {
        if (string.IsNullOrWhiteSpace(role)) return new("", Error: "InvalidSource");
        var json = value is JsonElement element ? element : default;
        if (role.Equals("ActivityResultMember", StringComparison.OrdinalIgnoreCase))
            return ResultMember(json);
        if (role.Equals("VariableMember", StringComparison.OrdinalIgnoreCase))
            return VariableMember(json);
        if (IsMemberRole(role)) return new(role, Error: "UnsupportedMemberBinding");
        if (json.ValueKind == JsonValueKind.Object &&
            (json.TryGetProperty("memberPath", out _) ||
             (!role.Equals("WorkflowRequest", StringComparison.OrdinalIgnoreCase) && json.TryGetProperty("path", out _))))
            return new(role, Error: "UnsupportedMemberSelection");

        if (role.Equals("Variable", StringComparison.OrdinalIgnoreCase))
            return VariableReference.TryParse(value, out var reference) && reference is not null
                ? Required("Variable", reference.ReferenceKey, reference.DeclaringScopeId)
                : new("Variable", Error: "InvalidSource");
        if (role.Equals("Input", StringComparison.OrdinalIgnoreCase))
            return VariableReference.TryParse(value, out var reference) && reference is not null
                ? Required("Input", reference.ReferenceKey, reference.DeclaringScopeId)
                : new("Input", Error: "InvalidSource");
        if (role.Equals("WorkflowRequest", StringComparison.OrdinalIgnoreCase))
            return Required("WorkflowRequest", String(json, "memberKey"), path: String(json, "path"));
        if (role.Equals("ActivityResult", StringComparison.OrdinalIgnoreCase))
        {
            var binding = Required("ActivityResult", String(json, "producerNodeId"), String(json, "producerScopeId"), String(json, "projectionKey"));
            // A scope is optional: a binding the server supplied declares none. One that is declared must be usable.
            var declaresScope = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("producerScopeId", out var scope)
                && scope.ValueKind != JsonValueKind.Null;
            return binding.Error is null && (string.IsNullOrWhiteSpace(binding.Projection) || (declaresScope && string.IsNullOrWhiteSpace(binding.Scope)))
                ? binding with { Error = "InvalidSource" }
                : binding with { IsDirect = true, IsOptional = json.ValueKind == JsonValueKind.Object && json.TryGetProperty("isOptional", out var optional)
                    && optional.ValueKind == JsonValueKind.True };
        }
        return new(role, Error: "InvalidSource");
    }

    // A member read of an activity result names its producer by lineage alone, so it never declares a scope.
    private static ActivityDataReadBinding ResultMember(JsonElement json)
    {
        const string role = "ActivityResultMember";
        if (json.ValueKind != JsonValueKind.Object || json.TryGetProperty("path", out _) ||
            json.TryGetProperty("producerScopeId", out var scope) && scope.ValueKind != JsonValueKind.Null)
            return new(role, Error: "InvalidSource");
        var binding = Required(role, String(json, "producerNodeId"), projection: String(json, "projectionKey"));
        if (binding.Error is not null || string.IsNullOrWhiteSpace(binding.Projection))
            return new(role, Error: "InvalidSource");
        if (!ActivityDataMemberPath.TryRead(json, out var memberPath))
            return new(role, Error: "InvalidMemberPath");
        return binding with
        {
            IsDirect = true,
            MemberPath = memberPath,
            IsOptional = json.TryGetProperty("isOptional", out var optional) && optional.ValueKind == JsonValueKind.True
        };
    }

    // A member read of a variable names its declaring scope explicitly: unlike a whole-value read, it has no default.
    private static ActivityDataReadBinding VariableMember(JsonElement json)
    {
        const string role = "VariableMember";
        if (json.ValueKind != JsonValueKind.Object || json.TryGetProperty("path", out _))
            return new(role, Error: "InvalidSource");
        var key = String(json, "referenceKey");
        var scope = String(json, "declaringScopeId");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(scope))
            return new(role, Error: "InvalidSource");
        return ActivityDataMemberPath.TryRead(json, out var memberPath)
            ? new(role, key, scope) { MemberPath = memberPath }
            : new(role, Error: "InvalidMemberPath");
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
