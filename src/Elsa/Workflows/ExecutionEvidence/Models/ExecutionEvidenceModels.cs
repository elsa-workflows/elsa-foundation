using System.Text.Json;

namespace Elsa.Workflows.ExecutionEvidence.Models;

/// <summary>
/// One flat, immutable fact observed while a workflow executed. Deliberately a single denormalized shape rather than a
/// governed per-kind catalog: the only consumer is an automated test asserting over an ordered stream, and every
/// assertion it wants to write is a filter over <see cref="Kind"/> plus a handful of nullable columns.
/// </summary>
public sealed record ExecutionEvidenceRecord
{
    /// <summary>Store-assigned, strictly increasing per workflow execution. Doubles as the query cursor.</summary>
    public long Sequence { get; init; }

    public required string WorkflowExecutionId { get; init; }

    /// <summary>
    /// A <c>RuntimeCheckpointNames</c> value (<c>WorkflowStarted</c>, <c>ActivityCompleted</c>, …) for lifecycle facts,
    /// or one of <see cref="ExecutionEvidenceKinds"/> for the facts this module derives from the change set.
    /// </summary>
    public required string Kind { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>The runtime checkpoint this fact was derived from. Also the module's replay de-duplication key.</summary>
    public required string CheckpointId { get; init; }

    public string? CorrelationId { get; init; }
    public string? ActivityExecutionId { get; init; }
    public string? ActivityType { get; init; }
    public string? AuthoredActivityId { get; init; }

    /// <summary>Activity execution status for activity facts; workflow status for lifecycle facts.</summary>
    public string? Status { get; init; }

    public IReadOnlyList<string>? Outcomes { get; init; }

    /// <summary>Variable name for <see cref="ExecutionEvidenceKinds.VariableSet"/>.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// The variable's value, present only when <see cref="ValueDisposition"/> is
    /// <see cref="ExecutionEvidenceValueDisposition.Captured"/>. A null here is never self-explanatory — read the
    /// disposition to learn whether the value was absent, explicitly null, held externally, withheld as sensitive, or
    /// too large to inline.
    /// </summary>
    public JsonElement? Value { get; init; }

    /// <summary>Why <see cref="Value"/> holds what it holds. Null for facts that carry no value at all.</summary>
    public ExecutionEvidenceValueDisposition? ValueDisposition { get; init; }

    /// <summary>Incident message for <see cref="ExecutionEvidenceKinds.Incident"/>.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Why a variable fact carries — or does not carry — an inline value. Without this a reader cannot tell "the variable
/// was set to null" from "the value exists but we withheld it", which makes every value assertion ambiguous.
/// </summary>
public enum ExecutionEvidenceValueDisposition
{
    /// <summary>The inline value is on the record.</summary>
    Captured,

    /// <summary>The variable was explicitly assigned null.</summary>
    Null,

    /// <summary>The variable is declared but has never been assigned.</summary>
    Absent,

    /// <summary>The runtime holds the value by external reference; this module does not resolve external storage.</summary>
    External,

    /// <summary>The value's protection policy marks it sensitive, so it is withheld.</summary>
    Sensitive,

    /// <summary>The value exceeded <see cref="ExecutionEvidenceOptions.MaxInlineValueLength"/>.</summary>
    Truncated
}

/// <summary>Fact kinds this module derives itself. Lifecycle kinds are reused verbatim from <c>RuntimeCheckpointNames</c>.</summary>
public static class ExecutionEvidenceKinds
{
    public const string Activity = nameof(Activity);
    public const string VariableSet = nameof(VariableSet);
    public const string Incident = nameof(Incident);
}

/// <summary>
/// One root variable's captured state for a single checkpoint. The enricher owns every capture decision — disposition,
/// redaction, truncation — so the store stays a dumb ordered buffer that only compares <see cref="Comparand"/>.
/// </summary>
public sealed record ExecutionEvidenceVariableCapture
{
    public required ExecutionEvidenceValueDisposition Disposition { get; init; }

    public JsonElement? Value { get; init; }

    /// <summary>
    /// Deterministic change-detection key. Two captures represent the same variable state exactly when their comparands
    /// are ordinally equal, so a value that moves from inline to external — or from a value to null — still registers as
    /// a write even though neither carries an inline value.
    /// </summary>
    public required string Comparand { get; init; }
}

/// <summary>One checkpoint's worth of derived evidence, handed to the store as a unit.</summary>
public sealed record ExecutionEvidenceCommitBatch
{
    public required string WorkflowExecutionId { get; init; }
    public required string CheckpointId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? CorrelationId { get; init; }

    /// <summary>True once the workflow execution reached a terminal status, so a reader knows no more facts are coming.</summary>
    public bool Terminal { get; init; }

    public IReadOnlyList<ExecutionEvidenceRecord> Records { get; init; } = [];

    /// <summary>
    /// Captured root variable state, or null when this checkpoint carried no workflow state change. The store — not the
    /// enricher — diffs these against the previous snapshot, because it owns the only per-workflow lock and therefore
    /// the only place where a cross-checkpoint comparison is race-free.
    /// </summary>
    public IReadOnlyDictionary<string, ExecutionEvidenceVariableCapture>? RootVariables { get; init; }
}

/// <summary>A cursor-bounded slice of one workflow's (or one correlation's) evidence.</summary>
/// <param name="Records">Facts with a sequence greater than the requested cursor.</param>
/// <param name="FirstSequence">
/// The lowest sequence still retained, or 0 when nothing is retained. A caller MUST compare this against its cursor:
/// when <c>FirstSequence &gt; cursor + 1</c>, evidence between them was evicted by the buffer caps and any negative
/// assertion over that range is unsound.
/// </param>
/// <param name="LastSequence">Cursor to pass as <c>after</c> on the next read.</param>
/// <param name="Terminal">
/// For a single workflow, that it reached a terminal status. For a correlation, that every workflow <em>currently
/// matched</em> is terminal — which is not a completeness signal, because a child that has not yet committed its first
/// checkpoint is not matched at all. Use <see cref="MatchedWorkflows"/> to check the expected fan-out arrived.
/// </param>
/// <param name="MatchedWorkflows">How many workflow executions contributed to this page.</param>
public sealed record ExecutionEvidencePage(
    IReadOnlyList<ExecutionEvidenceRecord> Records,
    long FirstSequence,
    long LastSequence,
    bool Terminal,
    int MatchedWorkflows);
