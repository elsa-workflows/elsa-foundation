using System.Text.Json;

namespace Elsa.Workflows.Runtime.Api.Contracts;

/// <summary>Raw value evidence released by the separately-authorized and audited resolution boundary.</summary>
public sealed record ActivityExecutionValuePayloadView(
    string EvidenceId,
    string CaptureMode,
    JsonElement Payload);
