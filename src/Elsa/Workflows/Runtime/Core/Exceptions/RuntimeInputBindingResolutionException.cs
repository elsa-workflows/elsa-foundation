namespace Elsa.Workflows.Runtime.Core.Exceptions;

public sealed class RuntimeInputBindingResolutionException : InvalidOperationException
{
    public RuntimeInputBindingResolutionException(
        string message,
        RuntimeInputBindingResolutionFailureReason reason,
        string inputName,
        string? activityExecutionId,
        string? outputName,
        string? valueId)
        : base(message)
    {
        Reason = reason;
        InputName = inputName;
        ActivityExecutionId = activityExecutionId;
        OutputName = outputName;
        ValueId = valueId;
    }

    public RuntimeInputBindingResolutionFailureReason Reason { get; }
    public string InputName { get; }
    public string? ActivityExecutionId { get; }
    public string? OutputName { get; }
    public string? ValueId { get; }
}

public enum RuntimeInputBindingResolutionFailureReason
{
    AmbiguousActivityOutput,
    ActivityOutputMissing,
    DurableValueMissing,
    DurableValueHasNoReadableValue
}
