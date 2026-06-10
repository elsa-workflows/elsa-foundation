using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class ActiveActivityOutputKey : IEquatable<ActiveActivityOutputKey>
{
    public ActiveActivityOutputKey(
        string workflowExecutionId,
        string activityExecutionId,
        string outputName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputName);

        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        OutputName = outputName;
    }

    public string WorkflowExecutionId { get; }
    public string ActivityExecutionId { get; }
    public string OutputName { get; }

    public bool Equals(ActiveActivityOutputKey? other) =>
        other is not null &&
        WorkflowExecutionId == other.WorkflowExecutionId &&
        ActivityExecutionId == other.ActivityExecutionId &&
        OutputName == other.OutputName;

    public override bool Equals(object? obj) => Equals(obj as ActiveActivityOutputKey);

    public override int GetHashCode() => HashCode.Combine(WorkflowExecutionId, ActivityExecutionId, OutputName);
}

/// <summary>
/// Execution-local activity output value. This is not durable workflow continuation state.
/// </summary>
public sealed class ActiveActivityOutput
{
    public ActiveActivityOutput(
        ActiveActivityOutputKey key,
        JsonElement value,
        RuntimeValueTypeDescriptor? type,
        DateTimeOffset recordedAt,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(key);

        Key = key;
        Value = value;
        Type = type;
        RecordedAt = recordedAt;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public ActiveActivityOutputKey Key { get; }
    public JsonElement Value { get; }
    public RuntimeValueTypeDescriptor? Type { get; }
    public DateTimeOffset RecordedAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed record RuntimeInputBindingValidationContext(
    string ExecutableNodeId,
    bool CrossesSuspensionBoundary,
    bool HasAmbiguousProducerExecution);

public sealed record RuntimeInputBindingDiagnostic(
    RuntimeInputBindingDiagnosticCode Code,
    string InputName,
    string ExecutableNodeId,
    string Message);

public enum RuntimeInputBindingDiagnosticCode
{
    AmbiguousActivityOutputReference,
    ActivityOutputCrossesSuspensionBoundary
}
