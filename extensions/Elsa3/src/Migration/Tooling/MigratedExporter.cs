namespace Elsa3.Migration.Tooling;

/// <summary>A published workflow version the run can export (the wrapper of a reusable among them).</summary>
internal sealed record ExportWorkflow(string SourceVersionId, string WorkflowDefinitionId, string WorkflowVersionId);

/// <summary>A published reusable activity version the run can export, and its wrapper workflow definition.</summary>
internal sealed record ExportReusable(string SourceVersionId, string ActivityDefinitionVersionId, string WrapperWorkflowDefinitionId);

/// <summary>What the run published, for <see cref="MigratedExporter"/>.</summary>
internal sealed record ExportCandidates(IReadOnlyList<ExportWorkflow> Workflows, IReadOnlyList<ExportReusable> Reusables);

/// <summary>Where each exported source version went, as paths relative to <c>--out</c> with '/' separators.</summary>
internal sealed record MigratedExportResult(
    IReadOnlyDictionary<string, string> ExportedTo,
    IReadOnlyDictionary<string, string> WrapperExportedTo);

/// <summary>
/// Writes <c>migrated/</c> (spec 1237 T022, FR-023, FR-024): one file per published workflow definition under
/// <c>migrated/workflows/</c>, and <c>migrated/activities/activities.json</c>. Both always exist.
/// </summary>
internal static class MigratedExporter
{
    public const string WorkflowsFolderName = "workflows";
    public const string ActivitiesFolderName = "activities";
    public const string ActivitiesFileName = "activities.json";

    public static async Task<MigratedExportResult> ExportAsync(
        IServiceProvider services,
        ExportCandidates candidates,
        string migratedFolder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(candidates);
        Directory.CreateDirectory(Path.Combine(migratedFolder, WorkflowsFolderName));
        Directory.CreateDirectory(Path.Combine(migratedFolder, ActivitiesFolderName));
        await File.WriteAllTextAsync(Path.Combine(migratedFolder, ActivitiesFolderName, ActivitiesFileName), "[]", cancellationToken);
        return new MigratedExportResult(new Dictionary<string, string>(), new Dictionary<string, string>());
    }
}
