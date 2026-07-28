using System.Text.Json;

namespace Elsa.Workflows.Runtime.Api.Contracts;

/// <summary>
/// Resolves separately-authorized raw payload captured for activity-execution value evidence.
/// </summary>
public interface IActivityExecutionValuePayloadReader
{
    /// <summary>Resolves one evidence payload without disclosing whether hidden execution state exists.</summary>
    ValueTask<ActivityExecutionValuePayloadReadResult> ReadAsync(
        string workflowExecutionId,
        string activityExecutionId,
        string evidenceId,
        CancellationToken cancellationToken = default);
}

/// <summary>Raw value evidence released by the separately-authorized and audited resolution boundary.</summary>
public sealed record ActivityExecutionValuePayloadView(
    string EvidenceId,
    string CaptureMode,
    JsonElement Payload);

/// <summary>Represents the non-disclosing terminal result of an evidence-payload resolution attempt.</summary>
public sealed record ActivityExecutionValuePayloadReadResult(
    ActivityExecutionValuePayloadReadOutcome Outcome,
    ActivityExecutionValuePayloadView? Value)
{
    public static ActivityExecutionValuePayloadReadResult NotFound() => new(ActivityExecutionValuePayloadReadOutcome.NotFound, null);
    public static ActivityExecutionValuePayloadReadResult Denied() => new(ActivityExecutionValuePayloadReadOutcome.Denied, null);
    public static ActivityExecutionValuePayloadReadResult Unavailable() => new(ActivityExecutionValuePayloadReadOutcome.Unavailable, null);
    public static ActivityExecutionValuePayloadReadResult Resolved(ActivityExecutionValuePayloadView value) => new(ActivityExecutionValuePayloadReadOutcome.Resolved, value);
}

/// <summary>Identifies the terminal result of an evidence-payload resolution attempt.</summary>
public enum ActivityExecutionValuePayloadReadOutcome
{
    /// <summary>The captured payload was authorized, audited, and resolved.</summary>
    Resolved,

    /// <summary>The execution, inspection projection, or evidence was absent or hidden.</summary>
    NotFound,

    /// <summary>The caller lacks either resolution authorization or a stable attributable audit subject.</summary>
    Denied,

    /// <summary>The evidence exists, but Runtime did not capture a resolvable payload.</summary>
    Unavailable
}
