namespace Elsa.Workflows.Runtime.Reconciliation.Core.Exceptions;

/// <summary>
/// Raised (§2.23.5) when one closure input cannot be turned into a <c>WorkflowArtifactClosure</c> at all: it is
/// unreadable, is not valid JSON, deserializes to nothing, or declares a format version this build does not know.
/// </summary>
/// <remarks>
/// <para>
/// <b>File-scoped by design.</b> It carries the offending origin so the fault is actionable, and it exists so that
/// no raw <c>IOException</c> or <c>JsonException</c> escapes the reconciliation boundary. Mirrors the design-side
/// <c>InvalidWorkflowCatalogJsonException</c>.
/// </para>
/// <para>
/// This is the <em>only</em> import failure that is an exception rather than a diagnostic, and the reason is that
/// it happens before there is an artifact to attach a diagnostic to. Once an envelope parses, every rejection —
/// broken closure, hash mismatch, unmet requirement — is a named outcome on the pass result so one bad unit never
/// silences the rest of the batch.
/// </para>
/// </remarks>
public sealed class InvalidWorkflowArtifactClosureException(string origin, string reason, Exception? innerException = null)
    : Exception($"Could not read workflow artifact closure at '{origin}': {reason}", innerException)
{
    /// <summary>Where the unreadable envelope came from — a file path for the JSON source.</summary>
    public string Origin { get; } = origin;

    /// <summary>The failure, without the origin prefix, for callers that render their own message.</summary>
    public string Reason { get; } = reason;
}

/// <summary>
/// Raised (§2.23.5) when a reconciliation pass cannot meaningfully proceed — a misconfiguration or an
/// infrastructure fault that makes the source's entire contribution unknowable rather than empty.
/// </summary>
/// <remarks>
/// <para>
/// The canonical case is a configured folder that does not exist. That is deliberately <b>not</b> treated as "no
/// artifacts": a typo'd or unmounted volume would otherwise deactivate nothing, import nothing, and look exactly
/// like a healthy empty mount. An <em>existing</em> folder with no matching files is the genuine empty case and is
/// a logged no-op.
/// </para>
/// <para>
/// Pass-aborting, so it propagates out of the reconciler and fails the startup task. Per-artifact and per-closure
/// rejections never use this type.
/// </para>
/// </remarks>
public sealed class WorkflowArtifactReconciliationException(string sourceId, string reason, Exception? innerException = null)
    : Exception($"Workflow artifact reconciliation source '{sourceId}' could not complete its pass: {reason}", innerException)
{
    /// <summary>The source whose pass was aborted.</summary>
    public string SourceId { get; } = sourceId;

    /// <summary>The failure, without the source prefix, for callers that render their own message.</summary>
    public string Reason { get; } = reason;
}
