namespace Elsa3.Migration.Tooling.Report;

/// <summary>
/// Turns what a run collected into <see cref="MigrationReport"/> (spec 1237 FR-015 to FR-018, data-model.md): category,
/// remedy, component kind and routine mark from the code table, buckets, roots of blocked definitions, the component
/// inventory, the summary and the ordering rules. Every list is ordinal-sorted, so equal input gives an equal report.
/// </summary>
internal static class ReportBuilder
{
    public const string MustFix = "mustFix";
    public const string Reconsider = "reconsider";
    public const string TakeIntoAccount = "takeIntoAccount";
    public const string ConvertAsIs = "convert-as-is";
    public const string ConvertWithAdjustments = "convert-with-adjustments";
    public const string Incompatible = "incompatible";

    private const string BehaviourPrefix = "ELS3-BEHAVIOUR-";

    public static MigrationReport Build(ReportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sourceVersions = input.Definitions
            .GroupBy(definition => definition.SourceVersionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var definitions = input.Definitions
            .Select(definition => BuildDefinition(definition, sourceVersions))
            .OrderBy(definition => definition.DefinitionId, StringComparer.Ordinal)
            .ThenBy(definition => definition.Version)
            .ThenBy(definition => definition.SourceVersionId, StringComparer.Ordinal)
            .ToArray();

        var runFindings = OrderFindings(input.RunFindings.Select(Classify)).ToArray();
        var inputFindings = input.InputFindings
            .Select(item => new ReportInputFinding(item.File, Classify(item.Finding)))
            .OrderBy(item => item.File, StringComparer.Ordinal)
            .ThenBy(item => item.Finding.Code, StringComparer.Ordinal)
            .ThenBy(item => item.Finding.Path ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

        var allFindings = definitions.SelectMany(definition => definition.Findings)
            .Concat(runFindings)
            .Concat(inputFindings.Select(item => item.Finding))
            .ToArray();

        var byCategory = new ReportCategoryCounts(
            allFindings.Count(finding => finding.Category == MustFix),
            allFindings.Count(finding => finding.Category == Reconsider),
            allFindings.Count(finding => finding.Category == TakeIntoAccount));

        var notConverted = input.NotConverted
            .OrderBy(item => item.DefinitionId, StringComparer.Ordinal)
            .ThenBy(item => item.Version)
            .ThenBy(item => item.SourceVersionId, StringComparer.Ordinal)
            .ToArray();

        var summary = new ReportSummary(
            definitions.Length,
            definitions.Count(GoesSmoothly),
            definitions.Count(definition => definition.Published),
            definitions.Count(definition => definition.ExportedTo is not null),
            definitions.Count(definition => definition.BlockedBy.Count > 0),
            notConverted.Length,
            byCategory,
            new ReportBucketCounts(
                definitions.Count(definition => definition.Bucket == ConvertAsIs),
                definitions.Count(definition => definition.Bucket == ConvertWithAdjustments),
                definitions.Count(definition => definition.Bucket == Incompatible)));

        return new MigrationReport(
            MigrationReport.CurrentSchemaVersion,
            new ReportTool(MigrationReport.ToolName, input.ToolVersion),
            input.Command,
            byCategory.MustFix + byCategory.Reconsider > 0 ? 1 : 0,
            Redacted: true,
            new ReportOptions(input.AllVersions),
            summary,
            BuildComponents(definitions),
            definitions,
            notConverted,
            input.MissingTargets
                .Select(target => new ReportMissingTarget(target.DefinitionId, target.ReferencedBy.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()))
                .OrderBy(target => target.DefinitionId, StringComparer.Ordinal)
                .ToArray(),
            runFindings,
            inputFindings,
            new ReportRun(
                input.Features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                input.SettingKeys.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                input.PersistenceOverridden));
    }

    /// <summary>A definition goes smoothly when every finding it carries is routine (FR-015).</summary>
    public static bool GoesSmoothly(ReportDefinition definition) => definition.Findings.All(finding => finding.Routine == true);

    /// <summary>The category of a finding (FR-015).</summary>
    public static string Categorize(string code, FindingSeverity severity) => severity switch
    {
        FindingSeverity.Error => MustFix,
        FindingSeverity.Warning when code.StartsWith(BehaviourPrefix, StringComparison.Ordinal) => Reconsider,
        _ => TakeIntoAccount
    };

    /// <summary>The bucket of a definition's findings (FR-017).</summary>
    public static string Bucket(IReadOnlyCollection<ReportFinding> findings)
    {
        if (findings.Any(finding => finding.Remedy == RemedyName(Elsa3MigrationRemedy.None)))
            return Incompatible;

        return findings.Any(finding => finding.Category is MustFix or Reconsider) ? ConvertWithAdjustments : ConvertAsIs;
    }

    /// <summary>Classifies one collected finding through the code table.</summary>
    public static ReportFinding Classify(CollectedFinding finding)
    {
        var entry = Elsa3MigrationCodeTable.Get(finding.Code);
        var category = Categorize(finding.Code, finding.Severity);
        var componentKind = entry.ComponentKind is { } kind && finding.ComponentName is not null ? KindName(kind) : null;
        var useFixedText = entry.FixedText is not null;

        return new ReportFinding(
            finding.Code,
            SeverityName(finding.Severity),
            category,
            category == MustFix ? RemedyName(entry.Remedy) : null,
            componentKind,
            componentKind is null ? null : finding.ComponentName,
            StageName(finding.Stage),
            finding.Path,
            finding.NodeId,
            useFixedText ? entry.FixedText! : finding.Message,
            useFixedText ? entry.FixedGuidance ?? string.Empty : finding.Guidance ?? string.Empty,
            finding.PublishStoppedHere ? true : null,
            entry.Routine ? true : null);
    }

    public static string RemedyName(Elsa3MigrationRemedy remedy) => remedy switch
    {
        Elsa3MigrationRemedy.PatchDefinition => "patch-definition",
        Elsa3MigrationRemedy.BuildComponent => "build-component",
        Elsa3MigrationRemedy.None => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(remedy), remedy, null)
    };

    public static string KindName(Elsa3MigrationComponentKind kind) => kind switch
    {
        Elsa3MigrationComponentKind.Activity => "activity",
        Elsa3MigrationComponentKind.JavaScriptFunction => "javascript-function",
        Elsa3MigrationComponentKind.VariableType => "variable-type",
        Elsa3MigrationComponentKind.StorageDriver => "storage-driver",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static string SeverityName(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Error => "error",
        FindingSeverity.Warning => "warning",
        FindingSeverity.Info => "info",
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, null)
    };

    private static string StageName(FindingStage stage) => stage switch
    {
        FindingStage.Input => "input",
        FindingStage.Conversion => "conversion",
        FindingStage.Import => "import",
        FindingStage.Publish => "publish",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
    };

    private static ReportDefinition BuildDefinition(CollectedDefinition definition, IReadOnlyDictionary<string, CollectedDefinition> sourceVersions)
    {
        var findings = OrderFindings(definition.Findings.Select(Classify)).ToArray();
        return new ReportDefinition(
            definition.DefinitionId,
            definition.Name,
            definition.Version,
            definition.SourceVersionId,
            definition.IsReusable,
            Bucket(findings),
            RootsOf(definition, sourceVersions),
            definition.Published,
            definition.ExportedTo,
            definition.WrapperExportedTo,
            findings);
    }

    /// <summary>
    /// The root refused definitions behind a blocked one: those reached through any chain of <c>BlockedBy</c> links that
    /// are not blocked themselves (FR-017). A link to a version outside the report is a root as well.
    /// </summary>
    private static string[] RootsOf(CollectedDefinition definition, IReadOnlyDictionary<string, CollectedDefinition> sourceVersions)
    {
        var roots = new SortedSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { definition.SourceVersionId };
        var pending = new Stack<string>(definition.BlockedBy);

        while (pending.TryPop(out var sourceVersionId))
        {
            if (!visited.Add(sourceVersionId))
                continue;

            if (sourceVersions.TryGetValue(sourceVersionId, out var blocker) && blocker.BlockedBy.Count > 0)
            {
                // A blocker that is itself blocked is a root only when it was also refused for a finding of its own.
                if (HasOwnError(blocker))
                    roots.Add(sourceVersionId);
                foreach (var next in blocker.BlockedBy)
                    pending.Push(next);
            }
            else
            {
                roots.Add(sourceVersionId);
            }
        }

        return [.. roots];
    }

    private static bool HasOwnError(CollectedDefinition definition) => definition.Findings.Any(finding =>
        finding.Severity == FindingSeverity.Error
        && !string.Equals(finding.Code, Elsa3.Activities.Design.Import.Reconciliation.Models.Elsa3ReconciliationDiagnosticCodes.BlockedByDependency, StringComparison.Ordinal));

    private static IEnumerable<ReportFinding> OrderFindings(IEnumerable<ReportFinding> findings) => findings
        .OrderBy(finding => finding.Stage, StringComparer.Ordinal)
        .ThenBy(finding => finding.Path ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(finding => finding.Code, StringComparer.Ordinal)
        .ThenBy(finding => finding.NodeId ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(finding => finding.ComponentName ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(finding => finding.Message, StringComparer.Ordinal)
        .ThenBy(finding => finding.Guidance, StringComparer.Ordinal);

    private static ReportComponent[] BuildComponents(IEnumerable<ReportDefinition> definitions) => definitions
        .SelectMany(definition => definition.Findings
            .Where(finding => finding.ComponentKind is not null)
            .Select(finding => (Kind: finding.ComponentKind!, Name: finding.ComponentName!, definition.DefinitionId)))
        .GroupBy(usage => (usage.Kind, usage.Name))
        .Select(group => new ReportComponent(
            group.Key.Kind,
            group.Key.Name,
            group.Select(usage => usage.DefinitionId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            group.Count()))
        .OrderBy(component => component.Kind, StringComparer.Ordinal)
        .ThenBy(component => component.Name, StringComparer.Ordinal)
        .ToArray();
}
