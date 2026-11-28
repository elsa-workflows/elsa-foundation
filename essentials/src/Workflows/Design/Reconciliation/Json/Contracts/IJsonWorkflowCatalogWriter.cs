using Elsa.Workflows.Design.Reconciliation.Models;

namespace Elsa.Workflows.Design.Reconciliation.Json.Contracts;

/// <summary>
/// Writes an array of <see cref="WorkflowVersionReconciliationModel"/> to a JSON file that
/// <see cref="IJsonWorkflowCatalogReader"/> reads back, so the JSON catalog format has one owner. The same
/// models always give the same bytes. JSON workflow reconciliation does not use or register a writer; the tooling
/// that exports a catalog registers it.
/// </summary>
public interface IJsonWorkflowCatalogWriter
{
    void Write(string filePath, IReadOnlyList<WorkflowVersionReconciliationModel> models, CancellationToken cancellationToken);
}
