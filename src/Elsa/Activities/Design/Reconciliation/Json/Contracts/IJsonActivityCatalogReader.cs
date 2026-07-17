using Elsa.Activities.Design.Reconciliation.Core.Models;

namespace Elsa.Activities.Design.Reconciliation.Json.Contracts;

/// <summary>
/// Reads a JSON file holding an array of <see cref="ActivityVersionReconciliationModel"/> and returns
/// the deserialized models. Exposed as a contract so a feature can replace the read/parse strategy in
/// isolation (e.g. a different file layout or an embedded-resource reader) without re-wiring the
/// reconciliation source that consumes it.
/// </summary>
/// <remarks>
/// The reader deserializes each model's <c>Descriptor</c> as a raw
/// <see cref="System.Text.Json.JsonElement"/> — it does NOT bind it to a concrete descriptor type.
/// The descriptor stays opaque to the design domain: the reconciling handler persists it together with
/// stable provider and consumer key/schema identities, and only the runtime consumer that owns that key ever
/// materializes it.
/// </remarks>
public interface IJsonActivityCatalogReader
{
    IReadOnlyList<ActivityVersionReconciliationModel> Read(string filePath, CancellationToken cancellationToken);
}
