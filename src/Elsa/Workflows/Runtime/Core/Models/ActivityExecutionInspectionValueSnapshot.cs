using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record ActivityExecutionInspectionValueSnapshot(
    string Name,
    ActivityExecutionInspectionValueSubject Subject,
    RuntimePayloadCaptureMode CaptureMode,
    RuntimeValueTypeDescriptor? Type,
    DateTimeOffset CapturedAt,
    JsonElement? Payload,
    string CaptureReason,
    bool IsSensitive,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static ActivityExecutionInspectionValueSnapshot FromDecision(
        string name,
        ActivityExecutionInspectionValueSubject subject,
        RuntimePayloadCaptureDecision decision,
        RuntimeValueTypeDescriptor? type,
        DateTimeOffset capturedAt,
        JsonElement? payload,
        bool isSensitive,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            Name: name,
            Subject: subject,
            CaptureMode: decision.Mode,
            Type: type,
            CapturedAt: capturedAt,
            Payload: decision.CapturesEvidence ? payload?.Clone() : null,
            CaptureReason: decision.Reason,
            IsSensitive: isSensitive,
            Metadata: RuntimeModelMetadata.Snapshot(metadata));
}

public enum ActivityExecutionInspectionValueSubject
{
    ActivityInput,
    ActivityOutput,
    ContainerVariable
}
