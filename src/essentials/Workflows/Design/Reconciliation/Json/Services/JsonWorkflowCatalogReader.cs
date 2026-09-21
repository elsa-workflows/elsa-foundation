using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Reconciliation.Json.Contracts;
using Elsa.Workflows.Design.Reconciliation.Json.Exceptions;
using Elsa.Workflows.Design.Reconciliation.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Reconciliation.Json.Services;

/// <summary>
/// Default <see cref="IJsonWorkflowCatalogReader"/>: reads the file text and deserializes it into an
/// array of <see cref="WorkflowVersionReconciliationModel"/> via the shared <see cref="IPayloadSerializer"/>
/// (so naming/casing conventions — including the embedded workflow <c>State</c> graph — match the rest of
/// the system). No raw IO/JSON exception escapes; every failure becomes an
/// <see cref="InvalidWorkflowCatalogJsonException"/> carrying the offending path.
/// </summary>
public sealed class JsonWorkflowCatalogReader(
    IPayloadSerializer payloadSerializer,
    ILogger<JsonWorkflowCatalogReader> logger) : IJsonWorkflowCatalogReader
{
    public IReadOnlyList<WorkflowVersionReconciliationModel> Read(string filePath, CancellationToken cancellationToken)
    {
        var models = PayloadCatalogFile.ReadArray<WorkflowVersionReconciliationModel>(
            filePath,
            payloadSerializer,
            static (path, reason, inner) => new InvalidWorkflowCatalogJsonException(path, reason, inner));

        logger.LogDebug("Read {Count} workflow reconciliation model(s) from JSON catalog '{FilePath}'.", models.Length, filePath);
        return models;
    }
}
