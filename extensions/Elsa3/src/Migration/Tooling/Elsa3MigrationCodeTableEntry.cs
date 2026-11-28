namespace Elsa3.Migration.Tooling;

/// <summary>What a Must fix finding asks of the engineer (FR-016).</summary>
internal enum Elsa3MigrationRemedy
{
    /// <summary>Change the Elsa 3 definition.</summary>
    PatchDefinition,

    /// <summary>Supply the missing Elsa 4 component.</summary>
    BuildComponent,

    /// <summary>There is no Elsa 4 equivalent.</summary>
    None
}

/// <summary>The kind of custom component a component code names (FR-016a).</summary>
internal enum Elsa3MigrationComponentKind
{
    Activity,
    JavaScriptFunction,
    VariableType,
    StorageDriver
}

/// <summary>
/// One code's row in <see cref="Elsa3MigrationCodeTable"/>.
/// </summary>
/// <param name="Remedy">Used whenever the code is emitted at Error; constants carry no severity.</param>
/// <param name="ComponentKind">Set for component codes only.</param>
/// <param name="Routine">An Info the converter adds to every definition; it does not stop a definition going smoothly.</param>
/// <param name="FixedText">
/// The report text for a code whose emitted message may quote input the converter does not redact. When set, the report
/// never carries the emitted message or guidance (FR-019).
/// </param>
/// <param name="FixedGuidance">The report guidance that goes with <paramref name="FixedText"/>.</param>
internal sealed record Elsa3MigrationCodeTableEntry(
    Elsa3MigrationRemedy Remedy,
    Elsa3MigrationComponentKind? ComponentKind = null,
    bool Routine = false,
    string? FixedText = null,
    string? FixedGuidance = null);
