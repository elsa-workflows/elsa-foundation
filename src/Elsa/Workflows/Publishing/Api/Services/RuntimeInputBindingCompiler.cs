using System.Globalization;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Compiles a single authored activity input into its durable <see cref="RuntimeInputBinding"/>. Owns the
/// closed role-owned sources, authored-type resolution against the well-known type registry, and literal
/// value conversion. Extracted from
/// <see cref="WorkflowExecutableCompiler"/> (W30b, #418) so binding compilation is independently
/// unit-testable and can evolve without touching activity-tree compilation.
/// </summary>
public sealed class RuntimeInputBindingCompiler(
    IWellKnownTypeRegistry wellKnownTypeRegistry,
    ValueConversionPlanResolver? conversionPlanResolver = null)
{
    private const string LiteralExpressionType = "Literal";
    private const string ObjectExpressionType = "Object";
    private const string VariableExpressionType = "Variable";
    private const string WorkflowRequestExpressionType = "WorkflowRequest";
    private const string ActivityResultExpressionType = "ActivityResult";
    private const string DefaultExpressionType = "Default";
    private const string ReferenceKeyMetadataKey = "referenceKey";

    private readonly ValueConversionPlanResolver resolvedConversionPlanResolver = conversionPlanResolver ?? new(wellKnownTypeRegistry: wellKnownTypeRegistry);

    public RuntimeInputBinding Compile(string nodeId, InputDefinition inputDefinition, ArgumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var owner = ValuePolicyCombiner.FromAuthoredStorage(inputDefinition.StorageDriverType);
        var authored = ValuePolicyCombiner.FromAuthoredStorage(state.StorageDriverType, state.IsSensitive == true);
        var effective = ValuePolicyCombiner.Combine(owner, authored, $"Input '{inputDefinition.ReferenceKey}' on activity node '{nodeId}'");
        return Compile(nodeId, inputDefinition, state.Value, ValuePolicyCombiner.ToProtectionPolicy(effective), state.Conversion);
    }

    public RuntimeInputBinding Compile(string nodeId, InputDefinition inputDefinition, ArgumentValue value) =>
        Compile(
            nodeId,
            inputDefinition,
            value,
            ValuePolicyCombiner.ToProtectionPolicy(ValuePolicyCombiner.FromAuthoredStorage(inputDefinition.StorageDriverType)),
            conversion: null);

    /// <summary>
    /// Preserves an omitted optional argument as a canonical absent literal. Only nullable CLR targets
    /// can represent omission; non-nullable inputs require an authored binding or pinned default.
    /// </summary>
    public RuntimeInputBinding CompileOmitted(InputDefinition inputDefinition)
    {
        ArgumentNullException.ThrowIfNull(inputDefinition);
        var inputType = ResolveInputType(inputDefinition);
        var targetType = ToValueTypeDescriptor(inputDefinition);
        var policy = ValuePolicyCombiner.ToProtectionPolicy(ValuePolicyCombiner.FromAuthoredStorage(inputDefinition.StorageDriverType));
        if (!AcceptsNull(inputDefinition))
        {
            throw new ArgumentException(
                $"VF-ACT-003: Optional input '{inputDefinition.ReferenceKey}' with non-nullable type alias '{inputDefinition.Type.Alias}' " +
                "requires an authored binding or pinned default.",
                nameof(inputDefinition));
        }

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: targetType,
            effectivePolicy: policy,
            source: RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Absent(targetType, policy),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private RuntimeInputBinding Compile(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy effectivePolicy,
        AuthoredValueConversionRequest? conversion)
    {
        if (string.Equals(value.ExpressionType, LiteralExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileLiteralInput(nodeId, inputDefinition, value, effectivePolicy, conversion);

        // Studio's object editor serializes arrays and objects as JSON under the "Object" syntax. The value is
        // authored data, not an executable expression, so preserve it as a durable literal. This is particularly
        // important for publish-time trigger metadata such as HttpEndpoint.SupportedMethods, which must be known
        // before a workflow instance exists.
        if (string.Equals(value.ExpressionType, ObjectExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileObjectInput(nodeId, inputDefinition, value, effectivePolicy, conversion);

        if (string.Equals(value.ExpressionType, VariableExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileVariableInput(nodeId, inputDefinition, value, effectivePolicy);

        if (string.Equals(value.ExpressionType, WorkflowRequestExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileWorkflowRequestInput(nodeId, inputDefinition, value, effectivePolicy);

        if (string.Equals(value.ExpressionType, ActivityResultExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileActivityResultInput(nodeId, inputDefinition, value, effectivePolicy, conversion);

        if (string.Equals(value.ExpressionType, DefaultExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileDefaultInput(nodeId, inputDefinition, effectivePolicy);

        return CompileExpressionInput(nodeId, inputDefinition, value, effectivePolicy);
    }

    /// <summary>
    /// Compiles the existing authored <c>Variable</c> syntax into the canonical variable-read role.
    /// A missing declaring scope retains the historical workflow-scope meaning.
    /// </summary>
    private RuntimeInputBinding CompileVariableInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value, ValueProtectionPolicy effectivePolicy)
    {
        var reference = ParseVariableReference(nodeId, inputDefinition, value.Value);

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: ToValueTypeDescriptor(inputDefinition),
            effectivePolicy: effectivePolicy,
            source: RuntimeInputBindingSource.VariableRead,
            variable: new RuntimeVariableReference(
                reference.ReferenceKey,
                reference.DeclaringScopeId ?? VariableReference.WorkflowScopeId),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static RuntimeInputBinding CompileWorkflowRequestInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value, ValueProtectionPolicy effectivePolicy)
    {
        var payload = RequireObjectPayload(nodeId, inputDefinition, value, WorkflowRequestExpressionType);
        var memberKey = RequireStringProperty(nodeId, inputDefinition, payload, WorkflowRequestExpressionType, "memberKey");
        var path = ReadOptionalStringProperty(payload, "path");

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: ToValueTypeDescriptor(inputDefinition),
            effectivePolicy: effectivePolicy,
            source: RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference(memberKey, path),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static RuntimeInputBinding CompileActivityResultInput(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy effectivePolicy,
        AuthoredValueConversionRequest? conversion)
    {
        var payload = RequireObjectPayload(nodeId, inputDefinition, value, ActivityResultExpressionType);
        var producerNodeId = RequireStringProperty(nodeId, inputDefinition, payload, ActivityResultExpressionType, "producerNodeId");
        var projectionKey = RequireStringProperty(nodeId, inputDefinition, payload, ActivityResultExpressionType, "projectionKey");
        var producerScopeId = RequireStringProperty(nodeId, inputDefinition, payload, ActivityResultExpressionType, "producerScopeId");
        var isOptional = payload.TryGetProperty("isOptional", out var optionalElement) &&
                         optionalElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                         optionalElement.GetBoolean();

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: ToValueTypeDescriptor(inputDefinition),
            effectivePolicy: effectivePolicy,
            source: RuntimeInputBindingSource.ActivityResult,
            activityResult: new RuntimeActivityResultReference(producerNodeId, projectionKey, producerScopeId, isOptional),
            metadata: BuildInputMetadata(inputDefinition),
            conversionRequest: AuthoredValueConversionMapper.ToRuntimeRequest(conversion));
    }

    private RuntimeInputBinding CompileDefaultInput(string nodeId, InputDefinition inputDefinition, ValueProtectionPolicy effectivePolicy)
    {
        if (!inputDefinition.DefaultValue.HasValue)
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' requests its contract default, but the pinned input contract declares no default.");

        var defaultSyntax = inputDefinition.DefaultSyntax;
        if (string.IsNullOrWhiteSpace(defaultSyntax) ||
            string.Equals(defaultSyntax, LiteralExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileLiteralInput(nodeId, inputDefinition, new ArgumentValue(inputDefinition.DefaultValue.Value, LiteralExpressionType), effectivePolicy, conversion: null);

        if (string.Equals(defaultSyntax, ObjectExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileObjectInput(nodeId, inputDefinition, new ArgumentValue(inputDefinition.DefaultValue.Value, ObjectExpressionType), effectivePolicy, conversion: null);

        throw new ArgumentException(
            $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' default syntax '{defaultSyntax}' is executable. Canonical activity defaults must be pinned literals.");
    }

    private static VariableReference ParseVariableReference(string nodeId, InputDefinition inputDefinition, object? value)
    {
        var unwrapped = value is JsonElement jsonElement ? jsonElement : JsonSerializer.SerializeToElement(value);
        if (!VariableReference.TryParse(unwrapped, out var reference) || reference is null)
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type 'Variable' but carries no resolvable variable reference (a reference key is required).");

        return reference;
    }

    private RuntimeInputBinding CompileLiteralInput(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy policy,
        AuthoredValueConversionRequest? conversion)
    {
        if (AuthoredValueConversionMapper.IsExplicitFormattedContent(conversion))
            return CompileFormattedLiteralInput(nodeId, inputDefinition, value, policy, conversion!);

        var inputType = ResolveInputType(inputDefinition);
        object? converted;
        try
        {
            converted = ConvertLiteral(value.Value, inputType);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidCastException or OverflowException)
        {
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' value '{value.Value}' cannot be converted to alias '{inputDefinition.Type.Alias}'.", exception);
        }

        ValidateNull(inputDefinition, inputType, converted, nodeId);
        var literal = JsonSerializer.SerializeToElement(converted, inputType);
        var targetType = ToValueTypeDescriptor(inputDefinition);
        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: targetType,
            effectivePolicy: policy,
            source: RuntimeInputBindingSource.Literal,
            literal: converted is null
                ? ValueEnvelope.Null(targetType, policy)
                : ValueEnvelope.Inline(targetType, literal, policy),
            metadata: BuildInputMetadata(inputDefinition),
            conversionPlan: resolvedConversionPlanResolver.Resolve(
                targetType,
                ValueRepresentationDefaults.Infer(targetType),
                targetType,
                AuthoredValueConversionMapper.Mode(conversion),
                AuthoredValueConversionMapper.Profile(conversion),
                AuthoredValueConversionMapper.Limits(conversion),
                AuthoredValueConversionMapper.Options(conversion)));
    }

    private RuntimeInputBinding CompileFormattedLiteralInput(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy policy,
        AuthoredValueConversionRequest conversion)
    {
        var inputType = ResolveInputType(inputDefinition);
        var raw = ExtractLiteralContent(value.Value);
        ValidateNull(inputDefinition, inputType, raw, nodeId);
        var sourceType = new ValueTypeDescriptor("String");
        var targetType = ToValueTypeDescriptor(inputDefinition);
        var literal = raw is null
            ? ValueEnvelope.Null(sourceType, policy)
            : ValueEnvelope.Inline(sourceType, JsonSerializer.SerializeToElement(raw), policy);

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: targetType,
            effectivePolicy: policy,
            source: RuntimeInputBindingSource.Literal,
            literal: literal,
            metadata: BuildInputMetadata(inputDefinition),
            conversionPlan: resolvedConversionPlanResolver.Resolve(
                sourceType,
                ValueRepresentation.FormattedContent,
                targetType,
                AuthoredValueConversionMapper.Mode(conversion),
                AuthoredValueConversionMapper.Profile(conversion),
                AuthoredValueConversionMapper.Limits(conversion),
                AuthoredValueConversionMapper.Options(conversion)));
    }

    private RuntimeInputBinding CompileObjectInput(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        ValueProtectionPolicy policy,
        AuthoredValueConversionRequest? conversion)
    {
        var inputType = ResolveInputType(inputDefinition);
        object? converted;
        try
        {
            var authored = value.Value is JsonElement jsonElement
                ? jsonElement
                : JsonSerializer.SerializeToElement(value.Value);
            var structured = authored.ValueKind == JsonValueKind.String
                ? JsonSerializer.Deserialize<JsonElement>(authored.GetString()!)
                : authored;
            converted = structured.Deserialize(inputType);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' object value cannot be converted to alias '{inputDefinition.Type.Alias}'.",
                exception);
        }

        // Keep the contract violation outside the conversion catch so VF-ACT-004 remains the direct,
        // stable publication diagnostic instead of being wrapped as malformed object syntax.
        ValidateNull(inputDefinition, inputType, converted, nodeId);
        try
        {
            var literal = JsonSerializer.SerializeToElement(converted, inputType);
            var targetType = ToValueTypeDescriptor(inputDefinition);
            return new RuntimeInputBinding(
                inputKey: inputDefinition.ReferenceKey,
                targetType: targetType,
                effectivePolicy: policy,
                source: RuntimeInputBindingSource.Literal,
                literal: converted is null
                    ? ValueEnvelope.Null(targetType, policy)
                    : ValueEnvelope.Inline(targetType, literal, policy),
                metadata: BuildInputMetadata(inputDefinition),
                conversionPlan: resolvedConversionPlanResolver.Resolve(
                    targetType,
                    ValueRepresentationDefaults.Infer(targetType),
                    targetType,
                    AuthoredValueConversionMapper.Mode(conversion),
                    AuthoredValueConversionMapper.Profile(conversion),
                    AuthoredValueConversionMapper.Limits(conversion),
                    AuthoredValueConversionMapper.Options(conversion)));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' object value cannot be converted to alias '{inputDefinition.Type.Alias}'.",
                exception);
        }
    }

    private RuntimeInputBinding CompileExpressionInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value, ValueProtectionPolicy effectivePolicy)
    {
        if (string.IsNullOrWhiteSpace(value.ExpressionType))
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' does not declare an expression type.");

        if (TryReadPortableExpressionDefinition(value.Value, out var definition))
            return CompilePortableExpressionInput(nodeId, inputDefinition, value.ExpressionType, definition, effectivePolicy);

        var expressionText = ExtractExpressionText(value.Value);
        if (string.IsNullOrWhiteSpace(expressionText))
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type '{value.ExpressionType}' but carries no expression text.");

        var inputType = ResolveInputType(inputDefinition);
        var resultType = new RuntimeValueTypeDescriptor("alias", inputDefinition.Type.Alias, null);

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: ToValueTypeDescriptor(inputDefinition),
            effectivePolicy: effectivePolicy,
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(value.ExpressionType, expressionText, resultType),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static RuntimeInputBinding CompilePortableExpressionInput(
        string nodeId,
        InputDefinition inputDefinition,
        string authoredExpressionType,
        ExpressionDefinition definition,
        ValueProtectionPolicy effectivePolicy)
    {
        if (!string.Equals(authoredExpressionType, definition.Language, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' declares expression type '{authoredExpressionType}', but its portable definition declares language '{definition.Language}'.");
        }

        if (!string.Equals(inputDefinition.Type.Alias, definition.ResultType.Alias, StringComparison.Ordinal) ||
            inputDefinition.Type.CollectionKind != definition.ResultType.CollectionKind)
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' expects '{inputDefinition.Type.Alias}', but its portable expression declares result type '{definition.ResultType.Alias}'.");
        }

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: ToValueTypeDescriptor(inputDefinition),
            effectivePolicy: effectivePolicy,
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(
                definition.Language,
                definition.Source,
                new RuntimeValueTypeDescriptor("alias", definition.ResultType.Alias, null),
                definition.Metadata,
                definition.Parameters,
                definition.Options,
                definition.CapabilityProfile),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static bool TryReadPortableExpressionDefinition(object? value, out ExpressionDefinition definition)
    {
        if (value is ExpressionDefinition instance)
        {
            definition = instance;
            return true;
        }

        var element = value as JsonElement? ?? (value is null ? null : JsonSerializer.SerializeToElement(value));
        if (element is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("language", out _) ||
            !payload.TryGetProperty("source", out _) ||
            !payload.TryGetProperty("capabilityProfile", out _))
        {
            definition = null!;
            return false;
        }

        definition = payload.Deserialize<ExpressionDefinition>()
            ?? throw new ArgumentException("Portable expression definition payload could not be deserialized.");
        return true;
    }

    // Closes the authored TypeReference (alias + collection kind) into a concrete CLR type via the
    // well-known type registry, mirroring VariableMapper's resolution (FR-007). Unknown alias → object.
    private Type ResolveInputType(InputDefinition inputDefinition) =>
        TypeReferenceFactory.Resolve(
            inputDefinition.Type,
            alias => wellKnownTypeRegistry.TryGetTypeOrDefault(alias, out var type) ? type : typeof(object));

    private static string? ExtractExpressionText(object? value)
    {
        if (value is null)
            return null;

        if (value is JsonElement jsonElement)
            return jsonElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null
                : jsonElement.ValueKind == JsonValueKind.String ? jsonElement.GetString() : jsonElement.ToString();

        return value.ToString();
    }

    private static JsonElement RequireObjectPayload(
        string nodeId,
        InputDefinition inputDefinition,
        ArgumentValue value,
        string expressionType)
    {
        var payload = value.Value is JsonElement element
            ? element
            : JsonSerializer.SerializeToElement(value.Value);
        if (payload.ValueKind != JsonValueKind.Object)
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type '{expressionType}' but carries no object reference payload.");

        return payload;
    }

    private static string RequireStringProperty(
        string nodeId,
        InputDefinition inputDefinition,
        JsonElement payload,
        string expressionType,
        string propertyName)
    {
        if (payload.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
            return property.GetString()!;

        throw new ArgumentException(
            $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type '{expressionType}' but carries no '{propertyName}'.");
    }

    private static string? ReadOptionalStringProperty(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static Dictionary<string, string> BuildInputMetadata(InputDefinition inputDefinition) =>
        new()
        {
            [ReferenceKeyMetadataKey] = inputDefinition.ReferenceKey
        };

    private static ValueTypeDescriptor ToValueTypeDescriptor(InputDefinition inputDefinition) =>
        new(inputDefinition.Type.Alias, inputDefinition.Type.CollectionKind);

    private static bool AcceptsNull(InputDefinition inputDefinition) =>
        inputDefinition.IsNullable;

    private static void ValidateNull(InputDefinition inputDefinition, Type inputType, object? value, string nodeId)
    {
        if (value is null && !AcceptsNull(inputDefinition))
        {
            throw new ArgumentException(
                $"VF-ACT-004: Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' does not accept null.");
        }
    }

    private static object? ConvertLiteral(object? value, Type targetType)
    {
        if (value is null)
            return null;

        var nullableTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return null;

            if (jsonElement.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                return jsonElement.Deserialize(nullableTargetType);

            value = jsonElement.ValueKind == JsonValueKind.String ? jsonElement.GetString() : jsonElement.ToString();
        }

        if (nullableTargetType.IsInstanceOfType(value))
            return value;

        if (nullableTargetType == typeof(string))
            return $"{value}";

        if (nullableTargetType.IsEnum)
            return Enum.Parse(nullableTargetType, $"{value}", ignoreCase: true);

        if (!typeof(IConvertible).IsAssignableFrom(nullableTargetType) || value is not IConvertible)
            return JsonSerializer.SerializeToElement(value).Deserialize(nullableTargetType);

        return Convert.ChangeType(value, nullableTargetType, CultureInfo.InvariantCulture);
    }

    private static string? ExtractLiteralContent(object? value)
    {
        if (value is null)
            return null;

        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return null;

            return jsonElement.ValueKind == JsonValueKind.String ? jsonElement.GetString() : jsonElement.GetRawText();
        }

        return value is string text ? text : JsonSerializer.Serialize(value);
    }
}
