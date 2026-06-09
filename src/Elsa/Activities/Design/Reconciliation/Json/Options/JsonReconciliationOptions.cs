namespace Elsa.Activities.Design.Reconciliation.Json.Options;

/// <summary>
/// Options for the JSON-file reconciliation source. A source contributes activity-version models read
/// from JSON — either a single <see cref="FilePath"/> or an ordered set of <see cref="Files"/>, but not
/// both (the feature validates this at registration). Unlike the CLR source, the file is author-authored
/// design-time data, so it may carry arbitrary UI metadata (display name, category, description, …) with
/// no restrictions.
/// </summary>
public sealed class JsonReconciliationOptions
{
    /// <summary>
    /// A single JSON file to read. The convenient shorthand for the common one-file case; mutually
    /// exclusive with <see cref="Files"/> — configure exactly one of the two.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// An ordered set of JSON files, read in ascending
    /// <see cref="JsonActivityReconciliationFileOption.Order"/> and concatenated. Use this (instead of
    /// <see cref="FilePath"/>) when reconciliation must be staged — e.g. plain activities first, then
    /// <c>Workflow</c>-kind activities that depend on those versions. Mutually exclusive with
    /// <see cref="FilePath"/> — configure exactly one of the two.
    /// </summary>
    public IEnumerable<JsonActivityReconciliationFileOption> Files { get; set; } = [];

    /// <summary>
    /// The source identity recorded on every contributed row. Required — a multi-file source has no
    /// single path to derive identity from, so the feature rejects an empty value at registration.
    /// </summary>
    public string SourceId { get; set; } = string.Empty;
}
