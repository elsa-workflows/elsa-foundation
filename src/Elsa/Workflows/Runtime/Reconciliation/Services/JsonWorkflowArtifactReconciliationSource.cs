using System.Runtime.CompilerServices;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Exceptions;
using Elsa.Workflows.Runtime.Reconciliation.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Reconciliation.Services;

/// <summary>
/// An <see cref="IWorkflowArtifactReconciliationSource"/> that offers closure envelopes read from JSON files on
/// disk — the mounted-volume shape a design-free runtime is configured with.
/// </summary>
/// <remarks>
/// The exactly-one shape of <see cref="JsonWorkflowArtifactReconciliationOptions"/> and the required
/// <c>SourceId</c> are validated by <c>JsonWorkflowArtifactReconciliationFeature</c> at registration, so this
/// source assumes a valid configuration and simply reads whichever shape was supplied. Files are read lazily per
/// call, so a shell reload picks up edits.
/// </remarks>
public sealed class JsonWorkflowArtifactReconciliationSource(
    IWorkflowArtifactClosureReader reader,
    IOptions<JsonWorkflowArtifactReconciliationOptions> options,
    ILogger<JsonWorkflowArtifactReconciliationSource> logger) : IWorkflowArtifactReconciliationSource
{
    /// <summary>The <c>SourceKind</c> stamped on every reference this source's artifacts mint.</summary>
    public const string Kind = "Json";

    private readonly JsonWorkflowArtifactReconciliationOptions _options = options.Value;

    public string SourceId => _options.SourceId;

    public string SourceKind => Kind;

    public async IAsyncEnumerable<WorkflowArtifactClosureFile> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var file in EffectiveFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var closure = reader.Read(file.FilePath, cancellationToken);
            yield return new WorkflowArtifactClosureFile(file.FilePath, closure, _options.TenantId);
        }

        // The method is async-iterator-shaped for the contract's benefit (a blob or OCI source genuinely awaits
        // per item); the file system reads synchronously and there is nothing here to await.
        await Task.CompletedTask;
    }

    /// <summary>
    /// The ordered set of files to read: the explicit <see cref="JsonWorkflowArtifactReconciliationOptions.Files"/>
    /// when present, the single <see cref="JsonWorkflowArtifactReconciliationOptions.FilePath"/> shorthand, or the
    /// deterministic <see cref="JsonWorkflowArtifactReconciliationOptions.FolderPath"/> scan.
    /// </summary>
    private IEnumerable<JsonWorkflowArtifactReconciliationFileOption> EffectiveFiles()
    {
        if (_options.Files.Any())
            return _options.Files.OrderBy(file => file.Order).ThenBy(file => file.FilePath, StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(_options.FilePath))
            return [new JsonWorkflowArtifactReconciliationFileOption(0, _options.FilePath)];

        if (!string.IsNullOrWhiteSpace(_options.FolderPath))
            return ScanFolder(_options.FolderPath);

        return [];
    }

    /// <summary>
    /// Scans the configured folder's top level for <c>*.json</c> in ordinal file-name order.
    /// </summary>
    /// <remarks>
    /// A <b>missing</b> folder aborts the pass; an <b>empty</b> one is a logged no-op. The asymmetry is the point:
    /// an unmounted volume or a typo'd path would otherwise be indistinguishable from a healthy mount that
    /// currently holds nothing, and the runtime would silently keep serving whatever it happened to have.
    /// </remarks>
    private List<JsonWorkflowArtifactReconciliationFileOption> ScanFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            throw new WorkflowArtifactReconciliationException(SourceId, $"the configured folder '{folderPath}' does not exist.");

        List<JsonWorkflowArtifactReconciliationFileOption> files;
        try
        {
            files = Directory.EnumerateFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .Select((path, index) => new JsonWorkflowArtifactReconciliationFileOption(index, path))
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // §2.23.5: no raw IO exception escapes the source boundary.
            throw new WorkflowArtifactReconciliationException(SourceId, $"the configured folder '{folderPath}' could not be scanned.", exception);
        }

        if (files.Count == 0)
            logger.LogInformation(
                "Workflow artifact closure scan of '{FolderPath}' matched no *.json files; source '{SourceId}' contributes nothing this pass.",
                folderPath,
                SourceId);
        else
            logger.LogInformation(
                "Workflow artifact closure scan of '{FolderPath}' matched {Count} *.json file(s) for source '{SourceId}'.",
                folderPath,
                files.Count,
                SourceId);

        return files;
    }
}
