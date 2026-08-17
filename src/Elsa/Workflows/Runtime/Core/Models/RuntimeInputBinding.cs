using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Durable compiled declaration describing how an activity input is evaluated at execution time.
/// </summary>
public sealed class RuntimeInputBinding
{
    [JsonConstructor]
    public RuntimeInputBinding(
        string inputKey,
        ValueTypeDescriptor targetType,
        ValueProtectionPolicy effectivePolicy,
        RuntimeInputBindingSource source,
        ValueEnvelope? literal = null,
        RuntimeWorkflowRequestReference? workflowRequest = null,
        RuntimeVariableReference? variable = null,
        RuntimeActivityResultReference? activityResult = null,
        RuntimeExpressionBinding? expression = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        ValueConversionPlan? conversionPlan = null,
        RuntimeValueConversionRequest? conversionRequest = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputKey);
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(effectivePolicy);
        ValidateCanonical(source, literal, workflowRequest, variable, activityResult, expression);

        InputName = inputKey;
        TargetType = targetType;
        EffectivePolicy = effectivePolicy;
        Source = source;
        Literal = literal;
        LiteralValue = literal switch
        {
            { Presence: ValuePresence.Present } => literal.InlineValue,
            { Presence: ValuePresence.ExplicitNull } => JsonSerializer.SerializeToElement<object?>(null),
            _ => null
        };
        WorkflowRequest = workflowRequest;
        Variable = variable;
        ActivityResult = activityResult;
        Expression = expression;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        ConversionPlan = conversionPlan;
        ConversionRequest = conversionRequest;
    }

    public string InputName { get; }
    public string InputKey => InputName;
    public ValueTypeDescriptor TargetType { get; }
    public ValueProtectionPolicy EffectivePolicy { get; }
    public RuntimeInputBindingSource Source { get; }
    public ValueEnvelope? Literal { get; }
    public RuntimeWorkflowRequestReference? WorkflowRequest { get; }
    public RuntimeVariableReference? Variable { get; }
    public RuntimeActivityResultReference? ActivityResult { get; }
    public JsonElement? LiteralValue { get; }
    public RuntimeExpressionBinding? Expression { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    /// <summary>Null preserves the legacy identity/retyping behavior of artifacts published before conversion plans.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValueConversionPlan? ConversionPlan { get; }
    /// <summary>
    /// Authored conversion intent retained only until a later publication linker can resolve a source contract.
    /// Published executable bindings should prefer <see cref="ConversionPlan"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeValueConversionRequest? ConversionRequest { get; }

    private static void ValidateCanonical(
        RuntimeInputBindingSource source,
        ValueEnvelope? literal,
        RuntimeWorkflowRequestReference? workflowRequest,
        RuntimeVariableReference? variable,
        RuntimeActivityResultReference? activityResult,
        RuntimeExpressionBinding? expression)
    {
        var payloadCount =
            (literal is not null ? 1 : 0) +
            (workflowRequest is not null ? 1 : 0) +
            (variable is not null ? 1 : 0) +
            (activityResult is not null ? 1 : 0) +
            (expression is not null ? 1 : 0);

        if (payloadCount != 1)
            throw new ArgumentException("A canonical runtime input binding must carry exactly one role-owned source payload.");

        var valid = source switch
        {
            RuntimeInputBindingSource.Literal => literal is not null,
            RuntimeInputBindingSource.WorkflowRequest => workflowRequest is not null,
            RuntimeInputBindingSource.VariableRead => variable is not null,
            RuntimeInputBindingSource.ActivityResult => activityResult is not null,
            RuntimeInputBindingSource.Expression => expression is not null,
            _ => false
        };

        if (!valid)
            throw new ArgumentException($"Runtime input binding source '{source}' does not match its canonical payload.");
    }

}

public sealed record RuntimeValueConversionRequest(
    ValueConversionMode Mode,
    ValueConversionProfileReference? Profile = null,
    ValueConversionLimits? Limits = null,
    JsonElement? Options = null);

public enum RuntimeInputBindingSource
{
    Literal,
    Expression,
    WorkflowRequest,
    VariableRead,
    ActivityResult
}

public sealed class RuntimeWorkflowRequestReference
{
    public RuntimeWorkflowRequestReference(string memberKey, string? path = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberKey);

        MemberKey = memberKey;
        Path = path;
    }

    public string MemberKey { get; }
    public string? Path { get; }
}

public sealed class RuntimeVariableReference
{
    public RuntimeVariableReference(string variableKey, string declaringScopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variableKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(declaringScopeId);

        VariableKey = variableKey;
        DeclaringScopeId = declaringScopeId;
    }

    public string VariableKey { get; }
    public string DeclaringScopeId { get; }
}

public sealed class RuntimeActivityResultReference
{
    public RuntimeActivityResultReference(
        string producerExecutableNodeId,
        string projectionKey,
        string producerScopeId,
        bool isOptional = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(producerExecutableNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(producerScopeId);

        ProducerExecutableNodeId = producerExecutableNodeId;
        ProjectionKey = projectionKey;
        ProducerScopeId = producerScopeId;
        IsOptional = isOptional;
    }

    public string ProducerExecutableNodeId { get; }
    public string ProjectionKey { get; }
    public string ProducerScopeId { get; }
    public bool IsOptional { get; }
}

public sealed class RuntimeExpressionBinding
{
    public RuntimeExpressionBinding(
        string language,
        string expression,
        RuntimeValueTypeDescriptor? resultType = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyDictionary<string, ExpressionParameterBinding>? parameters = null,
        JsonElement? options = null,
        string? capabilityProfile = null,
        IReadOnlyDictionary<string, ValueConversionPlan>? parameterConversionPlans = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        Language = language;
        Expression = expression;
        ResultType = resultType;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
        Parameters = new ReadOnlyDictionary<string, ExpressionParameterBinding>(
            (parameters ?? new Dictionary<string, ExpressionParameterBinding>())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        ParameterConversionPlans = new ReadOnlyDictionary<string, ValueConversionPlan>(
            (parameterConversionPlans ?? new Dictionary<string, ValueConversionPlan>())
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        var unknownPlans = ParameterConversionPlans.Keys.Except(Parameters.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (unknownPlans.Length > 0)
            throw new ArgumentException($"Runtime expression conversion plans reference unknown parameters: [{string.Join(", ", unknownPlans)}].", nameof(parameterConversionPlans));
        Options = options?.Clone() ?? JsonSerializer.SerializeToElement(new { });
        if (Options.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Runtime expression evaluator options must be a JSON object.", nameof(options));
        CapabilityProfile = string.IsNullOrWhiteSpace(capabilityProfile)
            ? ExpressionCapabilityProfiles.BindingPureV1
            : capabilityProfile;
    }

    // System.Text.Json binds a constructor parameter to a property only when their declared types match
    // exactly, and Nullable<JsonElement> is not JsonElement. The public ctor's nullable 'options' therefore
    // cannot serve as the deserialization constructor. This private ctor's 'options' is exactly JsonElement
    // so every parameter binds; it chains through the public ctor to keep all invariants in one place.
    [JsonConstructor]
    private RuntimeExpressionBinding(
        string language,
        string expression,
        RuntimeValueTypeDescriptor? resultType,
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyDictionary<string, ExpressionParameterBinding>? parameters,
        JsonElement options,
        string? capabilityProfile,
        IReadOnlyDictionary<string, ValueConversionPlan>? parameterConversionPlans)
        : this(language, expression, resultType, metadata, parameters, (JsonElement?)options, capabilityProfile, parameterConversionPlans)
    {
    }

    public string Language { get; }
    public string Expression { get; }
    public RuntimeValueTypeDescriptor? ResultType { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public IReadOnlyDictionary<string, ExpressionParameterBinding> Parameters { get; }
    public IReadOnlyDictionary<string, ValueConversionPlan> ParameterConversionPlans { get; }
    public JsonElement Options { get; }
    public string CapabilityProfile { get; }
}

public sealed record RuntimeResolvedInput(
    string InputName,
    RuntimeInputBindingSource Source,
    RuntimeExpressionBinding? Expression)
{
    /// <summary>Canonical materialized source including its effective protection policy and payload location.</summary>
    public ValueEnvelope? Envelope { get; init; }
}
