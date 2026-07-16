using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Durable compiled declaration describing how an activity input is evaluated at execution time.
/// </summary>
public sealed class RuntimeInputBinding
{
    [JsonConstructor]
    public RuntimeInputBinding(
        string inputName,
        ValueTypeDescriptor targetType,
        ValueProtectionPolicy effectivePolicy,
        RuntimeInputBindingSource source,
        ValueEnvelope? literal,
        RuntimeWorkflowRequestReference? workflowRequest,
        RuntimeVariableReference? variable,
        RuntimeActivityResultReference? activityResult,
        JsonElement? literalValue,
        RuntimeExpressionBinding? expression,
        RuntimeActivityOutputReference? activityOutput,
        RuntimeDurableValueReference? durableValue,
        RuntimeReferenceValue? reference,
        IReadOnlyDictionary<string, string>? metadata)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputName);
        ArgumentNullException.ThrowIfNull(targetType);
        ArgumentNullException.ThrowIfNull(effectivePolicy);

        if (source is RuntimeInputBindingSource.WorkflowRequest or RuntimeInputBindingSource.VariableRead or RuntimeInputBindingSource.ActivityResult ||
            source is RuntimeInputBindingSource.Literal && literal is not null ||
            source is RuntimeInputBindingSource.Expression && expression is not null && targetType.Alias != "Elsa.Any")
            ValidateCanonical(source, literal, workflowRequest, variable, activityResult, expression);
        else
            Validate(source, literalValue, expression, activityOutput, durableValue, reference);

        InputName = inputName;
        TargetType = targetType;
        EffectivePolicy = effectivePolicy;
        Source = source;
        Literal = literal;
        WorkflowRequest = workflowRequest;
        Variable = variable;
        ActivityResult = activityResult;
        LiteralValue = literalValue?.Clone();
        Expression = expression;
        ActivityOutput = activityOutput;
        DurableValue = durableValue;
        Reference = reference;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    /// <summary>
    /// Creates a canonical role-owned binding. Legacy constructor overloads remain only while the
    /// invocation adapter is being migrated and are removed by spec 095's convergence phase.
    /// </summary>
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
        IReadOnlyDictionary<string, string>? metadata = null)
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
    }

    public RuntimeInputBinding(
        string inputName,
        RuntimeInputBindingSource source,
        JsonElement? literalValue = null,
        RuntimeExpressionBinding? expression = null,
        RuntimeActivityOutputReference? activityOutput = null,
        RuntimeDurableValueReference? durableValue = null,
        RuntimeReferenceValue? reference = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputName);
        Validate(source, literalValue, expression, activityOutput, durableValue, reference);

        InputName = inputName;
        TargetType = new ValueTypeDescriptor("Elsa.Any");
        EffectivePolicy = ValueProtectionPolicy.Transient;
        Source = source;
        LiteralValue = literalValue?.Clone();
        Literal = literalValue.HasValue
            ? ValueEnvelope.Inline(TargetType, literalValue.Value, EffectivePolicy)
            : null;
        Expression = expression;
        ActivityOutput = activityOutput;
        DurableValue = durableValue;
        Reference = reference;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
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
    public RuntimeActivityOutputReference? ActivityOutput { get; }
    public RuntimeDurableValueReference? DurableValue { get; }
    public RuntimeReferenceValue? Reference { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

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

    private static void Validate(
        RuntimeInputBindingSource source,
        JsonElement? literalValue,
        RuntimeExpressionBinding? expression,
        RuntimeActivityOutputReference? activityOutput,
        RuntimeDurableValueReference? durableValue,
        RuntimeReferenceValue? reference)
    {
        var payloadCount =
            (literalValue.HasValue ? 1 : 0) +
            (expression is not null ? 1 : 0) +
            (activityOutput is not null ? 1 : 0) +
            (durableValue is not null ? 1 : 0) +
            (reference is not null ? 1 : 0);

        if (payloadCount != 1)
            throw new ArgumentException("A runtime input binding must carry exactly one source payload.");

        var valid = source switch
        {
            RuntimeInputBindingSource.Literal => literalValue.HasValue,
            RuntimeInputBindingSource.Expression => expression is not null,
            RuntimeInputBindingSource.ActivityOutput => activityOutput is not null,
            RuntimeInputBindingSource.DurableValue => durableValue is not null,
            RuntimeInputBindingSource.Reference => reference is not null,
            _ => false
        };

        if (!valid)
            throw new ArgumentException($"Runtime input binding source '{source}' does not match its payload.");
    }
}

public enum RuntimeInputBindingSource
{
    Literal,
    Expression,
    ActivityOutput,
    DurableValue,
    Reference,
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
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        Language = language;
        Expression = expression;
        ResultType = resultType;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string Language { get; }
    public string Expression { get; }
    public RuntimeValueTypeDescriptor? ResultType { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class RuntimeActivityOutputReference
{
    public RuntimeActivityOutputReference(
        string? producerActivityExecutionId,
        string? producerExecutableNodeId,
        string outputName,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);

        ProducerActivityExecutionId = producerActivityExecutionId;
        ProducerExecutableNodeId = producerExecutableNodeId;
        OutputName = outputName;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string? ProducerActivityExecutionId { get; }
    public string? ProducerExecutableNodeId { get; }
    public string OutputName { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class RuntimeDurableValueReference
{
    public RuntimeDurableValueReference(string valueId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueId);

        ValueId = valueId;
    }

    public string ValueId { get; }
}

public sealed class RuntimeReferenceValue
{
    public RuntimeReferenceValue(
        string referenceType,
        string referenceId,
        JsonElement? payload = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);

        ReferenceType = referenceType;
        ReferenceId = referenceId;
        Payload = payload?.Clone();
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string ReferenceType { get; }
    public string ReferenceId { get; }
    public JsonElement? Payload { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed record RuntimeResolvedInput(
    string InputName,
    RuntimeInputBindingSource Source,
    JsonElement? Value,
    RuntimeExpressionBinding? Expression,
    DurableValueState? DurableValue,
    RuntimeReferenceValue? Reference);
