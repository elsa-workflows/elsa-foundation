using Elsa.Activities.Design.Core.Reconciliation.Models;

namespace Elsa.Activities.Design.Reconciliation.Json.Contracts;

/// <summary>
/// Writes an array of <see cref="ActivityVersionReconciliationModel"/> to a JSON file that
/// <see cref="IJsonActivityCatalogReader"/> reads back, so the JSON catalog format has one owner. The same
/// models always give the same bytes. JSON activity reconciliation does not use or register a writer; the tooling
/// that exports a catalog registers it.
/// </summary>
/// <remarks>
/// A model's <c>Descriptor</c> is written as the serializer emits it; a <see cref="System.Text.Json.JsonElement"/>
/// read by the reader is written back verbatim.
/// </remarks>
public interface IJsonActivityCatalogWriter
{
    void Write(string filePath, IReadOnlyList<ActivityVersionReconciliationModel> models, CancellationToken cancellationToken);
}
