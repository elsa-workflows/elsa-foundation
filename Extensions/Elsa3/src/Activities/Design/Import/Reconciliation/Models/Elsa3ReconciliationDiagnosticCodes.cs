namespace Elsa3.Activities.Design.Import.Reconciliation.Models;

/// <summary>
/// Codes for skips that only the import at engine start produces. Every other skip carries the import's own code.
/// </summary>
internal static class Elsa3ReconciliationDiagnosticCodes
{
    /// <summary>A blank source version id, or the same source version id or definition id and version more than once.</summary>
    public const string DuplicateInput = "ELS3-RECONCILE-DUPLICATE-INPUT";

    /// <summary>A reusable workflow this item uses was skipped or not published.</summary>
    public const string BlockedByDependency = "ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY";

    /// <summary>The imported workflow could not be published; it stays imported and the next start tries again.</summary>
    public const string PublishFailed = "ELS3-RECONCILE-PUBLISH-FAILED";
}

/// <summary>An input item left out of the import, and why.</summary>
/// <param name="BlockedBy">For a dependent: the source version id of the item it depends on.</param>
internal sealed record Elsa3SkippedItem(
    string Code,
    string FilePath,
    string DefinitionId,
    int Version,
    string Cause,
    string? BlockedBy = null);
