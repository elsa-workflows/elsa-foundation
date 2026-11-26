namespace Elsa3.Migration.Tooling;

/// <summary>
/// The migration tooling's own finding codes (spec 1237 Q13). Valid JSON that holds no Elsa 3 definition reuses
/// <see cref="Elsa3.Models.Elsa3MigrationDiagnosticCodes.UnsupportedInputKind"/>.
/// </summary>
internal static class Elsa3MigrationToolingCodes
{
    /// <summary>An input file that is not valid JSON.</summary>
    public const string InputUnreadable = "ELS3-MIGRATION-INPUT-UNREADABLE";

    /// <summary>A definition with no published version; its latest version converts instead (Info).</summary>
    public const string NoPublishedVersion = "ELS3-MIGRATION-NO-PUBLISHED-VERSION";

    /// <summary>
    /// An older selected workflow version, or the wrapper of an older reusable version: imported for its findings, neither
    /// published nor exported, as in startup reconciliation (Info).
    /// </summary>
    public const string SupersededVersion = "ELS3-MIGRATION-SUPERSEDED-VERSION";

    /// <summary>A run-level import refusal that carries no code of its own.</summary>
    public const string ImportRefused = "ELS3-MIGRATION-IMPORT-REFUSED";

    /// <summary>A selected definition that a run-level refusal kept from being applied.</summary>
    public const string RunRefused = "ELS3-MIGRATION-RUN-REFUSED";
}
