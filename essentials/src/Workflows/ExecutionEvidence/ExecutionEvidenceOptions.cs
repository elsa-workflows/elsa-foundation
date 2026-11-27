namespace Elsa.Workflows.ExecutionEvidence;

/// <summary>
/// Bounds for the process-local evidence buffer. The module is always-on once its feature is enabled, so an unbounded
/// buffer on a long-lived test host is a leak; these caps trade the oldest evidence for a fixed ceiling. Every cap is
/// lossy by design — a reader detects the loss through <see cref="Models.ExecutionEvidencePage.FirstSequence"/>.
/// </summary>
public sealed class ExecutionEvidenceOptions
{
    /// <summary>Oldest facts are dropped once one workflow execution exceeds this many.</summary>
    public int MaxRecordsPerWorkflow { get; set; } = 10_000;

    /// <summary>Least-recently-written workflow executions are evicted whole once this many are tracked.</summary>
    public int MaxWorkflows { get; set; } = 1_000;

    /// <summary>
    /// How many checkpoint ids one workflow execution remembers for replay de-duplication. Bounded for the same reason
    /// the record buffer is: a long-running looping workflow would otherwise grow this set without limit. A replayed
    /// checkpoint older than this window can be recorded twice.
    /// </summary>
    public int MaxTrackedCheckpointsPerWorkflow { get; set; } = 10_000;

    /// <summary>Inline variable values longer than this (in raw JSON characters) are recorded as Truncated.</summary>
    public int MaxInlineValueLength { get; set; } = 8 * 1024;

    /// <summary>
    /// When true, variable values whose protection policy marks them sensitive are recorded with the
    /// <see cref="Models.ExecutionEvidenceValueDisposition.Sensitive"/> disposition and no inline value. Leave this on
    /// unless a test genuinely needs to assert over a secret's value on a host holding no real secrets.
    /// </summary>
    public bool RedactSensitiveValues { get; set; } = true;
}
