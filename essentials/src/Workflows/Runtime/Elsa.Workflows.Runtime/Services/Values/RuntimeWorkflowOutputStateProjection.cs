using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// Read-side projection of a workflow execution's named workflow outputs (#254 Seam R1): the durable values a
/// <c>SetOutput</c> leaf assigned via <see cref="RuntimeWorkflowStateSeed.BuildWorkflowOutputChanges"/>, i.e.
/// those under the reserved <see cref="RuntimeWorkflowStateSeed.OutputValueIdPrefix"/> value-id namespace and
/// tagged <see cref="RuntimeMetadataKeys.OutputName"/>. Mirrors the most-recent-capture-wins rule of
/// <see cref="RuntimeInputBindingStateProjection"/> but feeds the instance read API, not input materialization —
/// so every entry is routed through the configured <see cref="IRuntimePayloadCapturePolicy"/> before its payload
/// is exposed. A declined payload (including a value marked <see cref="RuntimeMetadataKeys.IsSensitive"/>)
/// projects as an explicit redacted marker — name present, no value, policy reason attached — never silently
/// dropped. The same never-absent rule holds for a matching value whose payload is not stored inline (external
/// or custom storage): it projects as a marker with <see cref="NotStoredInlineReason"/> rather than vanishing.
/// Other durable-value producers may share the <see cref="RuntimeMetadataKeys.OutputName"/> tag but derive their
/// value ids from the authored output reference, so the prefix filter keeps them out of this projection.
/// </summary>
public static class RuntimeWorkflowOutputStateProjection
{
    /// <summary>Marker reason for a workflow output whose payload is not stored inline (external/custom storage).</summary>
    public const string NotStoredInlineReason = "Output payload is not stored inline.";

    /// <summary>
    /// Projects the workflow outputs from the execution's durable values, routing each payload through
    /// <paramref name="payloadCapturePolicy"/>. When several captures share an output name the most recently
    /// captured value wins (the <c>SetOutput</c> upsert re-emits under the same value id with a later
    /// <c>capturedAt</c>). Entries are ordered by output name for a deterministic read surface.
    /// </summary>
    public static IReadOnlyCollection<WorkflowOutputProjection> Project(
        IEnumerable<DurableValueState> durableValues,
        IRuntimePayloadCapturePolicy payloadCapturePolicy)
    {
        ArgumentNullException.ThrowIfNull(durableValues);
        ArgumentNullException.ThrowIfNull(payloadCapturePolicy);

        return LatestByName(durableValues)
            .Select(entry => ProjectOne(entry.Key, entry.Value, payloadCapturePolicy))
            .ToArray();
    }

    /// <summary>
    /// Reads the committed workflow output values themselves, with the same latest-per-name selection as
    /// <see cref="Project"/> but without any payload capture policy (spec 1245 FR-003). A value whose protection policy
    /// is protected (sensitive, encryption, redaction mode or retention) or that is not stored inline is left out and
    /// named in <see cref="WorkflowOutputValues.WithheldNames"/>: carrying it raw would drop the policy (ADR 0045 §9).
    /// </summary>
    public static WorkflowOutputValues ProjectValues(IEnumerable<DurableValueState> durableValues)
    {
        ArgumentNullException.ThrowIfNull(durableValues);

        // Write each committed value through unchanged (ADR 0091); no node model re-reads its numbers.
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var withheld = new List<string>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer, new System.Text.Json.JsonWriterOptions { Encoder = Elsa.Serialization.Core.JsonBaseline.Options.Encoder }))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in LatestByName(durableValues))
            {
                var policy = DurableValuePolicies.Read(value);
                if (!value.InlineValue.HasValue || policy.Storage != DurableValueStorage.Inline ||
                    policy.IsSensitive || policy.RequiresEncryption || policy.RedactionMode is not null || policy.RetentionPolicy is not null)
                {
                    withheld.Add(name);
                    continue;
                }

                writer.WritePropertyName(name);
                value.InlineValue.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var document = System.Text.Json.JsonDocument.Parse(buffer.WrittenMemory);
        return new WorkflowOutputValues(document.RootElement, withheld);
    }

    private static IEnumerable<KeyValuePair<string, DurableValueState>> LatestByName(IEnumerable<DurableValueState> durableValues)
    {
        var latestByName = new Dictionary<string, DurableValueState>(StringComparer.Ordinal);
        foreach (var durableValue in durableValues.Where(IsWorkflowOutput).OrderBy(value => value.CapturedAt))
            latestByName[durableValue.Metadata[RuntimeMetadataKeys.OutputName]] = durableValue;

        return latestByName.OrderBy(entry => entry.Key, StringComparer.Ordinal);
    }

    private static bool IsWorkflowOutput(DurableValueState value) =>
        value.ValueId.StartsWith(RuntimeWorkflowStateSeed.OutputValueIdPrefix, StringComparison.Ordinal) &&
        value.Metadata.ContainsKey(RuntimeMetadataKeys.OutputName);

    private static WorkflowOutputProjection ProjectOne(
        string name,
        DurableValueState value,
        IRuntimePayloadCapturePolicy payloadCapturePolicy)
    {
        // A workflow output whose payload is not stored inline (external or custom storage) has nothing this
        // projection can expose — but the name must still surface (never absent). Project it as a marker; no
        // policy consult is needed because there is no payload to disclose. Set Output writes externalized workflow
        // outputs when the value's policy requires it.
        if (!value.InlineValue.HasValue)
            return new WorkflowOutputProjection(name, Value: null, IsRedacted: true, RedactionReason: NotStoredInlineReason, value.Type, value.CapturedAt);

        // Set Output writes the sensitivity marker from the value's policy; threading it into the policy request
        // means a sensitive-marked output is redacted even by a payload-capturing host policy.
        var isSensitive = value.Metadata.TryGetValue(RuntimeMetadataKeys.IsSensitive, out var marker) &&
                          bool.TryParse(marker, out var parsed) && parsed;

        var decision = payloadCapturePolicy.Decide(new RuntimePayloadCaptureRequest(
            RuntimePayloadCaptureSubject.WorkflowOutput,
            value.WorkflowExecutionId,
            requestedAt: value.CapturedAt,
            activityExecutionId: value.SourceActivityExecutionId,
            valueName: name,
            type: value.Type,
            isSensitive: isSensitive,
            metadata: value.Metadata));

        return decision.Mode switch
        {
            RuntimePayloadCaptureMode.Payload => new WorkflowOutputProjection(name, value.InlineValue, IsRedacted: false, RedactionReason: null, value.Type, value.CapturedAt) { CaptureMode = decision.Mode },
            RuntimePayloadCaptureMode.DiagnosticSnapshot => new WorkflowOutputProjection(name, DefaultDiagnosticSnapshotFactory.Capture(value.InlineValue.Value, name, value.Type), IsRedacted: false, RedactionReason: null, value.Type, value.CapturedAt) { CaptureMode = decision.Mode },
            _ => new WorkflowOutputProjection(name, Value: null, IsRedacted: true, RedactionReason: decision.Reason, value.Type, value.CapturedAt)
        };
    }
}
