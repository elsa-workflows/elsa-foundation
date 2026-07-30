using System.Text.Json;
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
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidWorkflowCatalogJsonException(filePath ?? string.Empty, "no file path was configured.");

        if (!File.Exists(filePath))
            throw new InvalidWorkflowCatalogJsonException(filePath, "the file does not exist.");

        string json;

        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidWorkflowCatalogJsonException(filePath, "the file could not be read.", exception);
        }

        WorkflowVersionReconciliationModel[]? models;

        try
        {
            models = payloadSerializer.Deserialize<WorkflowVersionReconciliationModel[]>(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidWorkflowCatalogJsonException(filePath, "the file is not a valid JSON array of reconciliation models.", exception);
        }

        if (models is null)
            throw new InvalidWorkflowCatalogJsonException(filePath, "the file deserialized to null.");

        logger.LogDebug("Read {Count} workflow reconciliation model(s) from JSON catalog '{FilePath}'.", models.Length, filePath);

        return models;
    }
}
