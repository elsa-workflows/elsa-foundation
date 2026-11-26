using Elsa.Workflows.Design.Reconciliation.Models;

namespace Elsa.Workflows.Design.Reconciliation.Json.Contracts;

/// <summary>
/// Reads a JSON file holding an array of <see cref="WorkflowVersionReconciliationModel"/> and returns
/// the deserialized models. Exposed as a contract so a feature can replace the read/parse strategy in
/// isolation (e.g. a different file layout or an embedded-resource reader) without re-wiring the
/// reconciliation source that consumes it.
/// </summary>
public interface IJsonWorkflowCatalogReader
{
    IReadOnlyList<WorkflowVersionReconciliationModel> Read(string filePath, CancellationToken cancellationToken);
}
