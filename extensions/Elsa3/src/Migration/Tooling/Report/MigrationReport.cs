using System.Text.Json.Serialization;

namespace Elsa3.Migration.Tooling.Report;

// The report.json model, schema version 1 (spec 1237 FR-018 to FR-022, contracts/report.schema.json). It is serialized
// with the JSON baseline (ADR 0091); optional members are left out rather than written as null.

internal sealed record MigrationReport(
    int SchemaVersion,
    ReportTool Tool,
    string Command,
    int ExitCode,
    bool Redacted,
    ReportOptions Options,
    ReportSummary Summary,
    IReadOnlyList<ReportComponent> Components,
    IReadOnlyList<ReportDefinition> Definitions,
    IReadOnlyList<ReportVersionReference> NotConverted,
    IReadOnlyList<ReportMissingTarget> MissingTargets,
    IReadOnlyList<ReportFinding> RunFindings,
    IReadOnlyList<ReportInputFinding> InputFindings,
    ReportRun Run)
{
    public const int CurrentSchemaVersion = 1;
    public const string ToolName = "elsa3-migration-cli";
}

internal sealed record ReportTool(string Name, string Version);

internal sealed record ReportOptions(bool AllVersions);

internal sealed record ReportSummary(
    int Definitions,
    int GoesSmoothly,
    int Published,
    int Exported,
    int BlockedByDependency,
    int NotConverted,
    ReportCategoryCounts ByCategory,
    ReportBucketCounts ByBucket);

internal sealed record ReportCategoryCounts(int MustFix, int Reconsider, int TakeIntoAccount);

internal sealed record ReportBucketCounts(int ConvertAsIs, int ConvertWithAdjustments, int Incompatible);

internal sealed record ReportComponent(string Kind, string Name, IReadOnlyList<string> Definitions, int Usages);

internal sealed record ReportDefinition(
    string DefinitionId,
    string? Name,
    int Version,
    string SourceVersionId,
    bool IsReusable,
    string Bucket,
    IReadOnlyList<string> BlockedBy,
    bool Published,
    string? ExportedTo,
    string? WrapperExportedTo,
    IReadOnlyList<ReportFinding> Findings);

internal sealed record ReportVersionReference(string DefinitionId, int Version, string SourceVersionId);

internal sealed record ReportMissingTarget(string DefinitionId, IReadOnlyList<string> ReferencedBy);

internal sealed record ReportInputFinding(string File, ReportFinding Finding);

internal sealed record ReportRun(IReadOnlyList<string> Features, IReadOnlyList<string> SettingKeys, bool PersistenceOverridden);

internal sealed record ReportFinding(
    string Code,
    string Severity,
    string Category,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Remedy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ComponentKind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ComponentName,
    string Stage,
    string? Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NodeId,
    string Message,
    string Guidance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? PublishStoppedHere,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Routine);
