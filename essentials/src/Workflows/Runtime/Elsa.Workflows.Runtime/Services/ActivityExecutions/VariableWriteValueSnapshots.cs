using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.ActivityExecutions;

/// <summary>
/// Spec 1221 FR-025: the value each applied variable write wrote, as an inspection value snapshot of the writing
/// activity. The payload capture policy decides it like any other inspected value, so capture, sensitivity, redaction
/// and retention follow the existing rules: a value not captured reads as omitted, a sensitive one as redacted, and one
/// past its retention as expired, never as <c>null</c>. A snapshot names its write: <see cref="ActivityExecutionInspectionValueSnapshot.Name"/>
/// is the variable key, <see cref="ActivityExecutionInspectionValueSnapshot.Phase"/> the scope id and
/// <see cref="ActivityExecutionInspectionValueSnapshot.Sequence"/> the version the write produced.
/// </summary>
/// <remarks>
/// Operational data does not depend on these snapshots: the value a lost-update warning overwrote and a refused write
/// stay on the write record, whatever the capture policy and its retention (FR-026).
/// </remarks>
public static class VariableWriteValueSnapshots
{
    public static IReadOnlyCollection<ActivityExecutionInspectionValueSnapshot> Build(
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        string workflowExecutionId,
        string activityExecutionId,
        string? executableNodeId,
        IReadOnlyList<VariableWriteRecord> records,
        IReadOnlyList<RecordedVariableWrite> values,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(payloadCapturePolicy);
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(values);
        var snapshots = new List<ActivityExecutionInspectionValueSnapshot>(records.Count);
        foreach (var record in records)
        {
            if (record.Outcome == VariableCommitOutcome.LostUpdateFault)
                continue;
            var value = values.LastOrDefault(item => StringComparer.Ordinal.Equals(item.VariableKey, record.VariableKey))?.Value;
            if (value is null)
                continue;
            snapshots.Add(Build(payloadCapturePolicy, workflowExecutionId, activityExecutionId, executableNodeId, record, value, capturedAt));
        }

        return snapshots;
    }

    public static ActivityExecutionInspectionValueSnapshot Build(
        IRuntimePayloadCapturePolicy payloadCapturePolicy,
        string workflowExecutionId,
        string activityExecutionId,
        string? executableNodeId,
        VariableWriteRecord record,
        ValueEnvelope value,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(payloadCapturePolicy);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(value);
        var type = new RuntimeValueTypeDescriptor("alias", value.Type.Alias, value.Type.Schema);
        var isSensitive = value.Policy.HidesValue();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RuntimeMetadataKeys.VariableWriteScopeId] = record.ScopeId,
            [RuntimeMetadataKeys.VariableWriteNewVersion] = record.NewVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (executableNodeId is not null)
            metadata[RuntimeMetadataKeys.ExecutableNodeId] = executableNodeId;
        // A value stored outside the document, or withheld, is not read here: the snapshot says it was not captured.
        var inline = value.Presence == ValuePresence.ExplicitNull || value.Presence == ValuePresence.Present && value.InlineValue is not null;
        var decision = !inline
            ? new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "The value is stored externally or withheld; the write history keeps no payload.")
            : payloadCapturePolicy.Decide(new RuntimePayloadCaptureRequest(
            RuntimePayloadCaptureSubject.VariableWrite,
            workflowExecutionId,
            capturedAt,
            activityExecutionId: activityExecutionId,
            valueName: record.VariableKey,
            type: type,
            isSensitive: isSensitive,
            metadata: metadata));
        JsonElement? payload = value.Presence switch
        {
            ValuePresence.ExplicitNull => Serialize(decision, null, record.VariableKey, type),
            ValuePresence.Present when value.InlineValue is { } inlineValue => Serialize(decision, inlineValue, record.VariableKey, type),
            _ => null
        };
        return ActivityExecutionInspectionValueSnapshot.FromDecision(
            record.VariableKey,
            ActivityExecutionInspectionValueSubject.VariableWrite,
            decision,
            type,
            capturedAt,
            payload,
            isSensitive,
            metadata: decision.Metadata,
            phase: record.ScopeId,
            sequence: record.NewVersion);
    }

    private static JsonElement? Serialize(RuntimePayloadCaptureDecision decision, JsonElement? value, string valueName, RuntimeValueTypeDescriptor type) =>
        decision.Mode switch
        {
            RuntimePayloadCaptureMode.Payload => value?.Clone() ?? JsonSerializer.SerializeToElement<object?>(null),
            RuntimePayloadCaptureMode.DiagnosticSnapshot => DefaultDiagnosticSnapshotFactory.Capture(value, valueName, type),
            _ => null
        };
}
