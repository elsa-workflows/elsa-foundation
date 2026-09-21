namespace Elsa.Workflows.Design.Reconciliation.Json.Options;

/// <summary>
/// Options for the JSON-file workflow reconciliation source. A source contributes workflow-definition
/// version models read from JSON — exactly one of a single <see cref="FilePath"/>, an ordered set of
/// <see cref="Files"/>, or a scanned <see cref="FolderPath"/> (the feature validates this at
/// registration). The file is author-authored design-time data (name, description, version, and the
/// workflow <c>state</c> graph).
/// </summary>
public sealed class JsonWorkflowReconciliationOptions
{
    /// <summary>
    /// A single JSON file to read. The convenient shorthand for the common one-file case; mutually
    /// exclusive with <see cref="Files"/> and <see cref="FolderPath"/> — configure exactly one of the
    /// three.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// An ordered set of JSON files, read in ascending
    /// <see cref="JsonWorkflowReconciliationFileOption.Order"/> and concatenated. Use this (instead of
    /// <see cref="FilePath"/>) when reconciliation must be staged. Mutually exclusive with
    /// <see cref="FilePath"/> and <see cref="FolderPath"/> — configure exactly one of the three.
    /// </summary>
    public IEnumerable<JsonWorkflowReconciliationFileOption> Files { get; set; } = [];

    /// <summary>
    /// A directory scanned for <c>*.json</c> definition files — the mounted-volume/GitOps shape
    /// (spec 147). The scan reads the folder's top level only (non-recursive, deliberately: mount
    /// implementations such as Kubernetes ConfigMap <c>..data</c> symlink trees would double-read
    /// files under recursion) in deterministic ordinal file-name order. A missing folder fails the
    /// pass with an error naming this path; an empty folder (or one with no <c>*.json</c> matches)
    /// is logged and contributes nothing. Mutually exclusive with <see cref="FilePath"/> and
    /// <see cref="Files"/> — configure exactly one of the three.
    /// </summary>
    public string? FolderPath { get; set; }

    /// <summary>
    /// The source identity recorded for this source. Required — a multi-file source has no single path
    /// to derive identity from, so the feature rejects an empty value at registration.
    /// </summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// When <see langword="true"/>, the latest reconciled version of each definition this source owns
    /// is published — made executable — after a successful pass, via the in-process publish engine
    /// (requires the <c>WorkflowsPublishing</c> feature in the same shell). Idempotent across
    /// restarts: versions whose publication slot already holds an active publication of that version
    /// are skipped. Defaults to <see langword="false"/> — import-only, today's behaviour.
    /// </summary>
    public bool PublishOnReconcile { get; set; }
}
