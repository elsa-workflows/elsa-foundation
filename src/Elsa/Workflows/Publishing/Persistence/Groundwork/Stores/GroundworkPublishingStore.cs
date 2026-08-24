using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using System.Text.Json;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;

/// <summary>
/// Row mapping shared by the publishing stores. Each document keeps its aggregate in the JSON payload
/// column and repeats only the values its routes index as first-class columns, so a route reads a column
/// rather than reaching into the payload.
/// </summary>
public abstract class GroundworkPublishingStore(
    IGroundworkStorageSessionSource sessions,
    IPersistenceAccessContextAccessor accessContextAccessor,
    PublishingGroundworkDocumentSerializer serializer,
    string unitId,
    string? targetName = null)
{
    protected GroundworkPublishingStorage Storage { get; } = new(
        sessions ?? throw new ArgumentNullException(nameof(sessions)),
        accessContextAccessor ?? throw new ArgumentNullException(nameof(accessContextAccessor)),
        targetName);

    protected IPersistenceAccessContextAccessor AccessContextAccessor { get; } = accessContextAccessor;

    protected PublishingGroundworkDocumentSerializer Serializer { get; } =
        serializer ?? throw new ArgumentNullException(nameof(serializer));

    protected string UnitId { get; } = !string.IsNullOrWhiteSpace(unitId)
        ? unitId
        : throw new ArgumentException("A publishing unit id is required.", nameof(unitId));

    protected (StoredEntry Entry, T Document)? Load<T>(string id)
    {
        var entry = Storage.Read(UnitId, id);
        return entry is null ? null : (entry, Read<T>(entry));
    }

    protected T Read<T>(StoredEntry entry)
    {
        var values = entry.Values.Values;
        return Serializer.Deserialize<T>(
            UnitId,
            Text(values, PublishingGroundworkStorageManifest.IdField) ?? "",
            Text(values, PublishingGroundworkStorageManifest.SchemaVersionField)
                ?? throw new InvalidOperationException($"Publishing document '{UnitId}' has no schema version."),
            Payload(values));
    }

    /// <summary>
    /// The row's JSON payload. A provider is free to hand a JSON column back as text, as a parsed
    /// element, or as a document, so all three are accepted rather than assuming the one the writer
    /// happened to pass in.
    /// </summary>
    private string Payload(IReadOnlyDictionary<string, object?> values) =>
        values.GetValueOrDefault(PublishingGroundworkStorageManifest.ContentField) switch
        {
            string text => text,
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            _ => throw new InvalidOperationException($"Publishing document '{UnitId}' has no payload.")
        };

    /// <summary>Builds the row for <paramref name="document"/>, with its route columns projected alongside.</summary>
    protected StorageValues Values<T>(string id, T document, IReadOnlyDictionary<string, object?>? projections = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var (schemaVersion, content) = Serializer.Serialize(UnitId, document);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PublishingGroundworkStorageManifest.IdField] = id,
            [PublishingGroundworkStorageManifest.SchemaVersionField] = schemaVersion,
            [PublishingGroundworkStorageManifest.ContentField] = content
        };
        if (projections is not null)
            foreach (var projection in projections)
                values[projection.Key] = projection.Value;
        return new StorageValues(values);
    }

    /// <summary>
    /// Writes the row under the version the caller read, or as a create when it read nothing. The outcome
    /// is returned rather than thrown, because every publishing caller resolves the conflict itself — by
    /// re-reading the winner and reporting it — and because a uniqueness refusal is a different answer
    /// from a lost CAS race.
    /// </summary>
    protected WriteOutcome Save<T>(string id, T document, long? expectedVersion, IReadOnlyDictionary<string, object?>? projections = null)
    {
        var options = expectedVersion is null ? WriteOptions.CreateOnly : WriteOptions.IfVersion(expectedVersion.Value);
        return Storage.ConditionalUpsert(UnitId, Values(id, document, projections), options);
    }

    /// <summary>Whether the write landed, for the callers that do not distinguish why it did not.</summary>
    protected bool SaveSucceeded<T>(string id, T document, long? expectedVersion, IReadOnlyDictionary<string, object?>? projections = null) =>
        Save(id, document, expectedVersion, projections).Succeeded;

    protected IReadOnlyList<T> QueryBy<T>(string field, string value, string index)
    {
        var rows = Storage.Query(
            UnitId,
            Storage.Equal(UnitId, field, value),
            [Storage.Order(UnitId, field), Storage.Order(UnitId, PublishingGroundworkStorageManifest.IdField)],
            index);
        return rows.Select(Read<T>).ToArray();
    }

    protected static string? Text(IReadOnlyDictionary<string, object?> values, string field) =>
        values.GetValueOrDefault(field) as string;
}
