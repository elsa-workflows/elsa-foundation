using System.Text.Json;
using Elsa.Activities.Design.Reconciliation.Core.Models;
using Elsa.Activities.Design.Reconciliation.Json.Contracts;
using Elsa.Activities.Design.Reconciliation.Json.Exceptions;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Reconciliation.Json.Services;

/// <summary>
/// Default <see cref="IJsonActivityCatalogReader"/>: reads the file text and deserializes it into an
/// array of <see cref="ActivityVersionReconciliationModel"/> via the shared
/// <see cref="IPayloadSerializer"/> (so naming/casing conventions match the rest of the system).
/// </summary>
/// <remarks>
/// Each model's <c>Descriptor</c> is typed <c>object</c>, so the serializer binds it to a
/// <see cref="JsonElement"/> rather than a concrete descriptor. The reconciling handler persists that
/// opaque payload together with the entry's <c>DescriptorType</c>; the design domain never deserializes
/// it. The JSON file may therefore carry arbitrary author UI metadata; only the
/// <c>descriptorType</c>/<c>descriptor</c> pair is structurally meaningful here.
/// </remarks>
public sealed class JsonActivityCatalogReader(
    IPayloadSerializer payloadSerializer,
    ILogger<JsonActivityCatalogReader> logger) : IJsonActivityCatalogReader
{
    public IReadOnlyList<ActivityVersionReconciliationModel> Read(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new InvalidActivityCatalogJsonException(filePath ?? string.Empty, "no file path was configured.");

        if (!File.Exists(filePath))
            throw new InvalidActivityCatalogJsonException(filePath, "the file does not exist.");

        string json;

        try
        {
            json = File.ReadAllText(filePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidActivityCatalogJsonException(filePath, "the file could not be read.", exception);
        }

        ActivityVersionReconciliationModel[]? models;

        try
        {
            models = payloadSerializer.Deserialize<ActivityVersionReconciliationModel[]>(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidActivityCatalogJsonException(filePath, "the file is not a valid JSON array of reconciliation models.", exception);
        }

        if (models is null)
            throw new InvalidActivityCatalogJsonException(filePath, "the file deserialized to null.");

        logger.LogDebug("Read {Count} reconciliation model(s) from JSON catalog '{FilePath}'.", models.Length, filePath);

        return models;
    }
}
