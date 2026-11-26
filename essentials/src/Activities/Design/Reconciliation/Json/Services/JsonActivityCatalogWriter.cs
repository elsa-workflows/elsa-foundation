using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Design.Reconciliation.Json.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Reconciliation.Json.Services;

/// <summary>
/// Default <see cref="IJsonActivityCatalogWriter"/>: serializes the models as a top-level array through the shared
/// <see cref="IPayloadSerializer"/> that <see cref="JsonActivityCatalogReader"/> reads with, so
/// read(write(x)) = x. The file is indented with <c>\n</c> line endings and is byte-identical for the same models.
/// </summary>
public sealed class JsonActivityCatalogWriter(
    IPayloadSerializer payloadSerializer,
    ILogger<JsonActivityCatalogWriter> logger) : IJsonActivityCatalogWriter
{
    public void Write(string filePath, IReadOnlyList<ActivityVersionReconciliationModel> models, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PayloadCatalogFile.WriteArray(filePath, models, payloadSerializer);
        logger.LogDebug("Wrote {Count} reconciliation model(s) to JSON catalog '{FilePath}'.", models.Count, filePath);
    }
}
