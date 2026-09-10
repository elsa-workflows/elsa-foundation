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
/// <see cref="System.Text.Json.JsonElement"/> rather than a concrete descriptor. The reconciling handler persists that
/// opaque payload together with the entry's stable provider and consumer key/schema identities; the
/// design domain never deserializes it. The JSON file may therefore carry arbitrary provider payload;
/// the identity fields and <c>descriptor</c> are the structurally meaningful runtime bridge here.
/// </remarks>
public sealed class JsonActivityCatalogReader(
    IPayloadSerializer payloadSerializer,
    ILogger<JsonActivityCatalogReader> logger) : IJsonActivityCatalogReader
{
    public IReadOnlyList<ActivityVersionReconciliationModel> Read(string filePath, CancellationToken cancellationToken)
    {
        var models = PayloadCatalogFile.ReadArray<ActivityVersionReconciliationModel>(
            filePath,
            payloadSerializer,
            static (path, reason, inner) => new InvalidActivityCatalogJsonException(path, reason, inner));

        logger.LogDebug("Read {Count} reconciliation model(s) from JSON catalog '{FilePath}'.", models.Length, filePath);
        return models;
    }
}
