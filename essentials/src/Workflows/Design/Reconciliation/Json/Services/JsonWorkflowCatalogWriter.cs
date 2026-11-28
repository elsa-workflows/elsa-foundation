using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Reconciliation.Json.Contracts;
using Elsa.Workflows.Design.Reconciliation.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Reconciliation.Json.Services;

/// <summary>
/// Default <see cref="IJsonWorkflowCatalogWriter"/>: serializes the models as a top-level array through the shared
/// <see cref="IPayloadSerializer"/> that <see cref="JsonWorkflowCatalogReader"/> reads with, so
/// read(write(x)) = x. The file is indented with <c>\n</c> line endings and is byte-identical for the same models.
/// </summary>
public sealed class JsonWorkflowCatalogWriter(
    IPayloadSerializer payloadSerializer,
    ILogger<JsonWorkflowCatalogWriter> logger) : IJsonWorkflowCatalogWriter
{
    public void Write(string filePath, IReadOnlyList<WorkflowVersionReconciliationModel> models, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PayloadCatalogFile.WriteArray(filePath, models, payloadSerializer);
        logger.LogDebug("Wrote {Count} workflow reconciliation model(s) to JSON catalog '{FilePath}'.", models.Count, filePath);
    }
}
