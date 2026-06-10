namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimePayloadCaptureRequest
{
    public RuntimePayloadCaptureRequest(
        RuntimePayloadCaptureSubject subject,
        string workflowExecutionId,
        DateTimeOffset requestedAt,
        string? activityExecutionId = null,
        string? valueName = null,
        RuntimeValueTypeDescriptor? type = null,
        bool isSensitive = false,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        Subject = subject;
        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        ValueName = valueName;
        Type = type;
        IsSensitive = isSensitive;
        RequestedAt = requestedAt;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public RuntimePayloadCaptureSubject Subject { get; }
    public string WorkflowExecutionId { get; }
    public string? ActivityExecutionId { get; }
    public string? ValueName { get; }
    public RuntimeValueTypeDescriptor? Type { get; }
    public bool IsSensitive { get; }
    public DateTimeOffset RequestedAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class RuntimePayloadCaptureDecision
{
    public RuntimePayloadCaptureDecision(
        RuntimePayloadCaptureMode mode,
        string reason,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Mode = mode;
        Reason = reason;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public RuntimePayloadCaptureMode Mode { get; }
    public string Reason { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public bool CapturesPayload => Mode == RuntimePayloadCaptureMode.Payload;
}

public enum RuntimePayloadCaptureSubject
{
    WorkflowInput,
    WorkflowOutput,
    ActivityInput,
    ActivityOutput,
    DurableValue,
    Incident,
    Diagnostic
}

public enum RuntimePayloadCaptureMode
{
    None,
    MetadataOnly,
    Payload
}
