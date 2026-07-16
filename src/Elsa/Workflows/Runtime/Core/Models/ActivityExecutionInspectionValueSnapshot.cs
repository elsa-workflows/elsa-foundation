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
    IReadOnlyDictionary<string, string> Metadata,
    string? InputKey = null,
    string? EvaluationId = null,
    string? Phase = null,
    long? Sequence = null,
    RuntimeInputEvaluationFailure? Failure = null)
{
    public static ActivityExecutionInspectionValueSnapshot FromDecision(
        string name,
        ActivityExecutionInspectionValueSubject subject,
        RuntimePayloadCaptureDecision decision,
        RuntimeValueTypeDescriptor? type,
        DateTimeOffset capturedAt,
        JsonElement? payload,
        bool isSensitive,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? inputKey = null,
        string? evaluationId = null,
        string? phase = null,
        long? sequence = null,
        RuntimeInputEvaluationFailure? failure = null) =>
        new(
            Name: name,
            Subject: subject,
            CaptureMode: decision.Mode,
            Type: type,
            CapturedAt: capturedAt,
            Payload: decision.CapturesEvidence ? payload?.Clone() : null,
            CaptureReason: decision.Reason,
            IsSensitive: isSensitive,
            Metadata: RuntimeModelMetadata.Snapshot(metadata),
            InputKey: inputKey,
            EvaluationId: evaluationId,
            Phase: phase,
            Sequence: sequence,
            Failure: failure);
}

public sealed record RuntimeInputEvaluationFailure(string Code, string Message, string? IncidentId = null);

public enum ActivityExecutionInspectionValueSubject
{
    ActivityInput,
    ActivityOutput,
    ContainerVariable
}
