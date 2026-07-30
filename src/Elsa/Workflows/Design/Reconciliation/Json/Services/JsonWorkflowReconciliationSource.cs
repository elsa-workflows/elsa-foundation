using Elsa.Workflows.Design.Reconciliation.Contracts;
using Elsa.Workflows.Design.Reconciliation.Json.Contracts;
using Elsa.Workflows.Design.Reconciliation.Json.Options;
using Elsa.Workflows.Design.Reconciliation.Models;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Reconciliation.Json.Services;

/// <summary>
/// An <see cref="IWorkflowReconciliationSource"/> that contributes workflow-definition version rows read
/// from one or more JSON files on disk. The reconciler resolves this from DI alongside every other source
/// and calls <see cref="Read"/>; files are read lazily per call so a re-run picks up edits.
/// </summary>
/// <remarks>
/// The either/or shape of <see cref="JsonWorkflowReconciliationOptions"/> (a single <c>FilePath</c> or an
/// ordered <c>Files</c> list) and the required <c>SourceId</c> are validated by
/// <see cref="JsonWorkflowReconciliationFeature"/> at registration, so this source can assume a valid
/// configuration and simply read whichever was supplied.
/// </remarks>
public sealed class JsonWorkflowReconciliationSource(
    IJsonWorkflowCatalogReader reader,
    IOptions<JsonWorkflowReconciliationOptions> options) : IWorkflowReconciliationSource
{
    private readonly JsonWorkflowReconciliationOptions _options = options.Value;

    public string SourceId => _options.SourceId;

    public string SourceKind => "Json";

    public ValueTask<IEnumerable<WorkflowVersionReconciliationModel>> Read(CancellationToken cancellationToken)
    {
        var result = new List<WorkflowVersionReconciliationModel>();

        foreach (var file in EffectiveFiles())
            result.AddRange(reader.Read(file.FilePath, cancellationToken));

        return new ValueTask<IEnumerable<WorkflowVersionReconciliationModel>>(result);
    }

    /// <summary>
    /// The ordered set of files to read: the explicit <see cref="JsonWorkflowReconciliationOptions.Files"/>
    /// when present, otherwise the single <see cref="JsonWorkflowReconciliationOptions.FilePath"/> shorthand.
    /// </summary>
    private IEnumerable<JsonWorkflowReconciliationFileOption> EffectiveFiles()
    {
        if (_options.Files.Any())
            return _options.Files.OrderBy(f => f.Order);

        if (!string.IsNullOrWhiteSpace(_options.FilePath))
            return [new JsonWorkflowReconciliationFileOption(0, _options.FilePath)];

        return [];
    }
}
