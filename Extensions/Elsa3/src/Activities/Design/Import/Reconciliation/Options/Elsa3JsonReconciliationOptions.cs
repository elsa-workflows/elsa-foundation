namespace Elsa3.Activities.Design.Import.Reconciliation.Options;

/// <summary>
/// Options for importing Elsa 3 workflow definitions from files at engine start. Configure exactly one of a single
/// <see cref="FilePath"/>, an ordered set of <see cref="Files"/>, or a scanned <see cref="FolderPath"/>; the feature
/// validates this at registration. Each file holds one Elsa 3 workflow definition or a JSON array of them.
/// </summary>
public sealed class Elsa3JsonReconciliationOptions
{
    /// <summary>A single JSON file to read. Mutually exclusive with <see cref="Files"/> and <see cref="FolderPath"/>.</summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// An ordered set of JSON files, read in ascending <see cref="Elsa3JsonFileOption.Order"/>. Mutually
    /// exclusive with <see cref="FilePath"/> and <see cref="FolderPath"/>.
    /// </summary>
    public IEnumerable<Elsa3JsonFileOption> Files { get; set; } = [];

    /// <summary>
    /// A directory scanned for <c>*.json</c> files, top level only, in ordinal file-name order. A missing folder fails
    /// the start naming this path; an empty folder is logged and imports nothing. Mutually exclusive with
    /// <see cref="FilePath"/> and <see cref="Files"/>.
    /// </summary>
    public string? FolderPath { get; set; }

    /// <summary>Names the input in logs and in the import's collection id. Required.</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>
    /// When <see langword="true"/>, a skipped item fails the start instead of being logged. Defaults to
    /// <see langword="false"/>.
    /// </summary>
    public bool FailOnUnconvertible { get; set; }
}

/// <summary>One file of <see cref="Elsa3JsonReconciliationOptions.Files"/>.</summary>
public sealed record Elsa3JsonFileOption(int Order, string FilePath);
