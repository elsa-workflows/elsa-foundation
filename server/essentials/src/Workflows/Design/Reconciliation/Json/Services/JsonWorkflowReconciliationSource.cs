using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Json.Contracts;
using Elsa.Workflows.Design.Reconciliation.Json.Exceptions;
using Elsa.Workflows.Design.Reconciliation.Json.Options;
using Elsa.Workflows.Design.Reconciliation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Reconciliation.Json.Services;

/// <summary>
/// An <see cref="IWorkflowReconciliationSource"/> that contributes workflow-definition version rows read
/// from one or more JSON files on disk. The reconciler resolves this from DI alongside every other source
/// and calls <see cref="Read"/>; files are read lazily per call so a re-run picks up edits.
/// </summary>
/// <remarks>
/// The exactly-one shape of <see cref="JsonWorkflowReconciliationOptions"/> (a single <c>FilePath</c>, an
/// ordered <c>Files</c> list, or a scanned <c>FolderPath</c>) and the required <c>SourceId</c> are
/// validated by <see cref="JsonWorkflowReconciliationFeature"/> at registration, so this source can assume
/// a valid configuration and simply read whichever was supplied.
/// </remarks>
public sealed class JsonWorkflowReconciliationSource(
    IJsonWorkflowCatalogReader reader,
    IOptions<JsonWorkflowReconciliationOptions> options,
    ILogger<JsonWorkflowReconciliationSource> logger) : IWorkflowReconciliationSource
{
    private readonly JsonWorkflowReconciliationOptions _options = options.Value;

    public string SourceId => _options.SourceId;

    public string SourceKind => "Json";

    /// <inheritdoc cref="IWorkflowReconciliationSource.RequestsPublication" />
    public bool RequestsPublication => _options.PublishOnReconcile;

    public ValueTask<IEnumerable<WorkflowVersionReconciliationModel>> Read(CancellationToken cancellationToken)
    {
        var result = new List<WorkflowVersionReconciliationModel>();

        foreach (var file in EffectiveFiles())
        {
            foreach (var model in reader.Read(file.FilePath, cancellationToken))
            {
                // An omitted definitionId mints a fresh random id per restart, duplicating the definition
                // on every deploy. Import proceeds (the file is valid), but the author should pin the id.
                if (string.IsNullOrWhiteSpace(model.DefinitionId))
                    LogMissingDefinitionId(file.FilePath, model.Name);
                result.Add(model);
            }
        }

        return new ValueTask<IEnumerable<WorkflowVersionReconciliationModel>>(result);
    }

    /// <summary>
    /// The ordered set of files to read: the explicit <see cref="JsonWorkflowReconciliationOptions.Files"/>
    /// when present, the single <see cref="JsonWorkflowReconciliationOptions.FilePath"/> shorthand, or the
    /// deterministic <see cref="JsonWorkflowReconciliationOptions.FolderPath"/> scan (top-level
    /// <c>*.json</c> only, ordinal file-name order — see the option's docs for the non-recursion rationale).
    /// </summary>
    private IEnumerable<JsonWorkflowReconciliationFileOption> EffectiveFiles()
    {
        if (_options.Files.Any())
            return _options.Files.OrderBy(f => f.Order);

        if (!string.IsNullOrWhiteSpace(_options.FilePath))
            return [new JsonWorkflowReconciliationFileOption(0, _options.FilePath)];

        if (!string.IsNullOrWhiteSpace(_options.FolderPath))
            return ScanFolder(_options.FolderPath);

        return [];
    }

    private List<JsonWorkflowReconciliationFileOption> ScanFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            throw new InvalidWorkflowCatalogJsonException(folderPath, "the folder does not exist.");

        var files = Directory.EnumerateFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select((path, index) => new JsonWorkflowReconciliationFileOption(index, path))
            .ToList();

        if (files.Count == 0)
            LogEmptyFolderScan(folderPath);
        else
            LogFolderScan(folderPath, files.Count);

        return files;
    }

    private void LogFolderScan(string folderPath, int count)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Workflow reconciliation folder scan of '{FolderPath}' matched {Count} *.json file(s).", folderPath, count);
    }

    private void LogEmptyFolderScan(string folderPath)
    {
        logger.LogInformation("Workflow reconciliation folder scan of '{FolderPath}' matched no *.json files; the source contributes nothing this pass.", folderPath);
    }

    private void LogMissingDefinitionId(string filePath, string name)
    {
        logger.LogWarning(
            "Workflow definition '{Name}' in '{FilePath}' has no definitionId. A fresh id is generated on every restart, creating duplicate definitions — pin a definitionId in the file.",
            name, filePath);
    }
}
