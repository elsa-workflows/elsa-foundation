using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Durable compiled declaration describing how an activity input is evaluated at execution time.
/// </summary>
public sealed class RuntimeInputBinding
{
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
        Source = source;
        LiteralValue = literalValue?.Clone();
        Expression = expression;
        ActivityOutput = activityOutput;
        DurableValue = durableValue;
        Reference = reference;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string InputName { get; }
    public RuntimeInputBindingSource Source { get; }
    public JsonElement? LiteralValue { get; }
    public RuntimeExpressionBinding? Expression { get; }
    public RuntimeActivityOutputReference? ActivityOutput { get; }
    public RuntimeDurableValueReference? DurableValue { get; }
    public RuntimeReferenceValue? Reference { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

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
    Reference
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
