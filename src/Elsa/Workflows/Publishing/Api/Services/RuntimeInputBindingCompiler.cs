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
/// three binding strategies (literal / variable-reference / expression), authored-type resolution against
/// the well-known type registry, and literal value conversion. Extracted from
/// <see cref="WorkflowExecutableCompiler"/> (W30b, #418) so binding compilation is independently
/// unit-testable and can evolve without touching activity-tree compilation.
/// </summary>
public sealed class RuntimeInputBindingCompiler(IWellKnownTypeRegistry wellKnownTypeRegistry)
{
    private const string LiteralExpressionType = "Literal";
    private const string ObjectExpressionType = "Object";
    private const string VariableExpressionType = "Variable";
    private const string ReferenceKeyMetadataKey = "referenceKey";

    public RuntimeInputBinding Compile(string nodeId, InputDefinition inputDefinition, ArgumentValue value)
    {
        if (string.Equals(value.ExpressionType, LiteralExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileLiteralInput(nodeId, inputDefinition, value);

        // Studio's object editor serializes arrays and objects as JSON under the "Object" syntax. The value is
        // authored data, not an executable expression, so preserve it as a durable literal. This is particularly
        // important for publish-time trigger metadata such as HttpEndpoint.SupportedMethods, which must be known
        // before a workflow instance exists.
        if (string.Equals(value.ExpressionType, ObjectExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileObjectInput(nodeId, inputDefinition, value);

        if (string.Equals(value.ExpressionType, VariableExpressionType, StringComparison.OrdinalIgnoreCase))
            return CompileVariableInput(nodeId, inputDefinition, value);

        return CompileExpressionInput(nodeId, inputDefinition, value);
    }

    /// <summary>
    /// Compiles a structured <c>Variable</c> reference input into a runtime expression binding whose
    /// language is <c>Variable</c> and whose expression text round-trips the reference (reference key
    /// plus optional declaring scope) as a JSON object. The runtime materializer feeds that object to
    /// the registered <c>VariableExpressionHandler</c>, which resolves it through the visible scope
    /// chain at execution time (ADR 0027).
    /// </summary>
    private RuntimeInputBinding CompileVariableInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value)
    {
        var reference = ParseVariableReference(nodeId, inputDefinition, value.Value);
        var referenceText = JsonSerializer.Serialize(new VariableReferencePayload(reference.ReferenceKey, reference.DeclaringScopeId));

        var inputType = ResolveInputType(inputDefinition);
        var resultType = new RuntimeValueTypeDescriptor("alias", inputDefinition.Type.Alias, null);

        return new RuntimeInputBinding(
            inputName: inputDefinition.Name,
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(VariableExpressionType, referenceText, resultType),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private static VariableReference ParseVariableReference(string nodeId, InputDefinition inputDefinition, object? value)
    {
        var unwrapped = value is JsonElement jsonElement ? jsonElement : JsonSerializer.SerializeToElement(value);
        if (!VariableReference.TryParse(unwrapped, out var reference) || reference is null)
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type 'Variable' but carries no resolvable variable reference (a reference key is required).");

        return reference;
    }

    private sealed record VariableReferencePayload(string referenceKey, string? declaringScopeId);

    private RuntimeInputBinding CompileLiteralInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value)
    {
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

        var literal = JsonSerializer.SerializeToElement(converted, inputType);
        var targetType = ToValueTypeDescriptor(inputDefinition);
        var policy = ValueProtectionPolicy.InstanceInline;

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: targetType,
            effectivePolicy: policy,
            source: RuntimeInputBindingSource.Literal,
            literal: converted is null
                ? ValueEnvelope.Null(targetType, policy)
                : ValueEnvelope.Inline(targetType, literal, policy),
            metadata: BuildInputMetadata(inputDefinition));
    }

    private RuntimeInputBinding CompileObjectInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value)
    {
        var inputType = ResolveInputType(inputDefinition);
        try
        {
            var authored = value.Value is JsonElement jsonElement
                ? jsonElement
                : JsonSerializer.SerializeToElement(value.Value);
            var structured = authored.ValueKind == JsonValueKind.String
                ? JsonSerializer.Deserialize<JsonElement>(authored.GetString()!)
                : authored;
            var converted = structured.Deserialize(inputType);
            var literal = JsonSerializer.SerializeToElement(converted, inputType);
            var targetType = ToValueTypeDescriptor(inputDefinition);
            var policy = ValueProtectionPolicy.InstanceInline;

            return new RuntimeInputBinding(
                inputKey: inputDefinition.ReferenceKey,
                targetType: targetType,
                effectivePolicy: policy,
                source: RuntimeInputBindingSource.Literal,
                literal: converted is null
                    ? ValueEnvelope.Null(targetType, policy)
                    : ValueEnvelope.Inline(targetType, literal, policy),
                metadata: BuildInputMetadata(inputDefinition));
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new ArgumentException(
                $"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' object value cannot be converted to alias '{inputDefinition.Type.Alias}'.",
                exception);
        }
    }

    private RuntimeInputBinding CompileExpressionInput(string nodeId, InputDefinition inputDefinition, ArgumentValue value)
    {
        if (string.IsNullOrWhiteSpace(value.ExpressionType))
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' does not declare an expression type.");

        var expressionText = ExtractExpressionText(value.Value);
        if (string.IsNullOrWhiteSpace(expressionText))
            throw new ArgumentException($"Activity node '{nodeId}' input '{inputDefinition.ReferenceKey}' uses expression type '{value.ExpressionType}' but carries no expression text.");

        var inputType = ResolveInputType(inputDefinition);
        var resultType = new RuntimeValueTypeDescriptor("alias", inputDefinition.Type.Alias, null);

        return new RuntimeInputBinding(
            inputKey: inputDefinition.ReferenceKey,
            targetType: ToValueTypeDescriptor(inputDefinition),
            effectivePolicy: ValueProtectionPolicy.InstanceInline,
            source: RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding(value.ExpressionType, expressionText, resultType),
            metadata: BuildInputMetadata(inputDefinition));
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

    private static Dictionary<string, string> BuildInputMetadata(InputDefinition inputDefinition) =>
        new()
        {
            [ReferenceKeyMetadataKey] = inputDefinition.ReferenceKey
        };

    private static ValueTypeDescriptor ToValueTypeDescriptor(InputDefinition inputDefinition) =>
        new(inputDefinition.Type.Alias, inputDefinition.Type.CollectionKind);

    private static object? ConvertLiteral(object? value, Type targetType)
    {
        if (value is null)
            return null;

        var nullableTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                return null;

            value = jsonElement.ValueKind == JsonValueKind.String ? jsonElement.GetString() : jsonElement.ToString();
        }

        if (nullableTargetType == typeof(string))
            return $"{value}";

        if (nullableTargetType.IsEnum)
            return Enum.Parse(nullableTargetType, $"{value}", ignoreCase: true);

        return Convert.ChangeType(value, nullableTargetType, CultureInfo.InvariantCulture);
    }
}
