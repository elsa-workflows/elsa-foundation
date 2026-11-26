namespace Elsa3.Migration.Tooling.Report;

/// <summary>The severity a finding was emitted at. A skip is always reported at <see cref="Error"/> (FR-016).</summary>
internal enum FindingSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>The pipeline stage a finding comes from.</summary>
internal enum FindingStage
{
    Input,
    Conversion,
    Import,
    Publish
}

/// <summary>
/// A finding as the pipeline collected it, before the report builder classifies it. <see cref="Message"/> and
/// <see cref="Guidance"/> are the emitted texts; the builder keeps them only for codes the code table lets through and
/// otherwise writes the code table's fixed text (FR-019). <see cref="ComponentName"/> comes only from the diagnostic's
/// <c>ComponentName</c> metadata (FR-016a).
/// </summary>
internal sealed record CollectedFinding(
    string Code,
    FindingSeverity Severity,
    FindingStage Stage,
    string? Path,
    string? NodeId,
    string Message,
    string? Guidance,
    string? ComponentName = null,
    bool PublishStoppedHere = false);

/// <summary>One converted or refused definition version, as the run left it.</summary>
/// <param name="BlockedBy">
/// The source version ids of the definitions this one was skipped for directly. The builder follows them to the roots.
/// </param>
internal sealed record CollectedDefinition(
    string DefinitionId,
    string? Name,
    int Version,
    string SourceVersionId,
    bool IsReusable,
    bool Published,
    string? ExportedTo,
    string? WrapperExportedTo,
    IReadOnlyList<string> BlockedBy,
    IReadOnlyList<CollectedFinding> Findings);

/// <summary>A finding with no definition identity, on one input file (relative to the export root, '/' separators).</summary>
internal sealed record CollectedInputFinding(string File, CollectedFinding Finding);

/// <summary>Everything the report builder needs from one run.</summary>
internal sealed record ReportInput(
    string Command,
    string ToolVersion,
    bool AllVersions,
    IReadOnlyList<CollectedDefinition> Definitions,
    IReadOnlyList<ReportVersionReference> NotConverted,
    IReadOnlyList<ReportMissingTarget> MissingTargets,
    IReadOnlyList<CollectedFinding> RunFindings,
    IReadOnlyList<CollectedInputFinding> InputFindings,
    IReadOnlyList<string> Features,
    IReadOnlyList<string> SettingKeys,
    bool PersistenceOverridden);
