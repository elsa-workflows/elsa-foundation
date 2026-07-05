using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

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
/// Activity-output captures share the <see cref="RuntimeMetadataKeys.OutputName"/> tag but derive their
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

        var latestByName = new Dictionary<string, DurableValueState>(StringComparer.Ordinal);
        foreach (var durableValue in durableValues.Where(IsWorkflowOutput).OrderBy(value => value.CapturedAt))
            latestByName[durableValue.Metadata[RuntimeMetadataKeys.OutputName]] = durableValue;

        return latestByName
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => ProjectOne(entry.Key, entry.Value, payloadCapturePolicy))
            .ToArray();
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
        // policy consult is needed because there is no payload to disclose. Latent today: nothing writes
        // externalized workflow outputs yet, but the read contract must be honest from day one.
        if (!value.InlineValue.HasValue)
            return new WorkflowOutputProjection(name, Value: null, IsRedacted: true, RedactionReason: NotStoredInlineReason, value.Type, value.CapturedAt);

        // The sensitivity marker is a read-side contract today (nothing writes it yet); threading it into the
        // policy request means a sensitive-marked output is redacted even by a payload-capturing host policy.
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

        return decision.CapturesPayload
            ? new WorkflowOutputProjection(name, value.InlineValue, IsRedacted: false, RedactionReason: null, value.Type, value.CapturedAt)
            : new WorkflowOutputProjection(name, Value: null, IsRedacted: true, RedactionReason: decision.Reason, value.Type, value.CapturedAt);
    }
}

/// <summary>
/// One named workflow output as projected for the read surface: the payload when the capture policy exposes it,
/// or an explicit redacted marker (<see cref="IsRedacted"/> with the policy's <see cref="RedactionReason"/>)
/// when it does not. The name is always present either way.
/// </summary>
public sealed record WorkflowOutputProjection(
    string Name,
    JsonElement? Value,
    bool IsRedacted,
    string? RedactionReason,
    RuntimeValueTypeDescriptor Type,
    DateTimeOffset CapturedAt);
