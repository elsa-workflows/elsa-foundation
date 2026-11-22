using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;

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
    RuntimeInputEvaluationFailure? Failure = null,
    string? EvidenceId = null)
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
            Failure: failure,
            EvidenceId: null);

    /// <summary>
    /// Adds the withheld marker to a snapshot's metadata: the <see cref="WithheldValueKind"/> and, for a secret
    /// reference, its name. A reference name is not a value. Returns <paramref name="metadata"/> unchanged when nothing
    /// was withheld.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MarkWithheld(
        IReadOnlyDictionary<string, string> metadata,
        WithheldValue? withheld)
    {
        if (withheld is null)
            return metadata;

        var marked = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.WithheldKind] = withheld.Kind.ToString()
        };
        if (withheld.Secret is { } secret)
            marked[RuntimeMetadataKeys.SecretReferenceName] = secret.Name;
        return marked;
    }
}

public sealed record RuntimeInputEvaluationFailure(string Code, string Message, string? IncidentId = null);

public enum ActivityExecutionInspectionValueSubject
{
    ActivityInput,
    ActivityOutput,
    ContainerVariable
}
