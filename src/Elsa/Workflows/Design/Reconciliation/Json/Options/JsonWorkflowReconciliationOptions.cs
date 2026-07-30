namespace Elsa.Workflows.Design.Reconciliation.Json.Options;

/// <summary>
/// Options for the JSON-file workflow reconciliation source. A source contributes workflow-definition
/// version models read from JSON — either a single <see cref="FilePath"/> or an ordered set of
/// <see cref="Files"/>, but not both (the feature validates this at registration). The file is
/// author-authored design-time data (name, description, version, and the workflow <c>state</c> graph).
/// </summary>
public sealed class JsonWorkflowReconciliationOptions
{
    /// <summary>
    /// A single JSON file to read. The convenient shorthand for the common one-file case; mutually
    /// exclusive with <see cref="Files"/> — configure exactly one of the two.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// An ordered set of JSON files, read in ascending
    /// <see cref="JsonWorkflowReconciliationFileOption.Order"/> and concatenated. Use this (instead of
    /// <see cref="FilePath"/>) when reconciliation must be staged. Mutually exclusive with
    /// <see cref="FilePath"/> — configure exactly one of the two.
    /// </summary>
    public IEnumerable<JsonWorkflowReconciliationFileOption> Files { get; set; } = [];

    /// <summary>
    /// The source identity recorded for this source. Required — a multi-file source has no single path
    /// to derive identity from, so the feature rejects an empty value at registration.
    /// </summary>
    public string SourceId { get; set; } = string.Empty;
}
