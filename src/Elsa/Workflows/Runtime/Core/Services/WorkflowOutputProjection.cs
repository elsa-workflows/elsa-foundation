using Elsa.Workflows.Runtime.Core.Models;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Services;

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
