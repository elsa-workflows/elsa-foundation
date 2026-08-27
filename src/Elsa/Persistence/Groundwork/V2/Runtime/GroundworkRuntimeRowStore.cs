using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>
/// Small domain-owned seam over one admitted runtime row session.
/// </summary>
/// <remarks>
/// Runtime stores own serialization and projection mapping; this type owns only the repeated v2 row
/// mechanics: the stable string identity key, the schema-version column, the JSON content column,
/// optimistic writes, deletes, and query forwarding. It intentionally does not emulate document-store
/// query routes or hide provider capability interfaces.
/// </remarks>
public sealed class GroundworkRuntimeRowStore
{
    private const string ProviderOwnedScopeColumn = "__groundwork_scope";

    public GroundworkRuntimeRowStore(IStorageSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!IsRuntimeKey(session.Unit.Key.Columns))
        {
            throw new ArgumentException(
                $"Runtime row sessions must use exactly the '{ElsaRuntimeV2StorageManifest.IdField}' key, optionally paired with the provider-owned scope column.",
                nameof(session));
        }

        Session = session;
    }

    public IStorageSession Session { get; }

    public StorageUnit Unit => Session.Unit;

    public StoredEntry? Read(string id)
    {
        return Session.Read(Key(id));
    }

    public WriteOutcome Insert(
        string id,
        string schemaVersion,
        object content,
        IReadOnlyDictionary<string, object?>? projections = null,
        WriteOptions? options = null)
    {
        return Session.Insert(Values(id, schemaVersion, content, projections), options);
    }

    public WriteOutcome Upsert(
        string id,
        string schemaVersion,
        object content,
        IReadOnlyDictionary<string, object?>? projections = null,
        WriteOptions? options = null)
    {
        return Session.Upsert(Values(id, schemaVersion, content, projections), options);
    }

    public WriteOutcome Update(
        string id,
        string schemaVersion,
        object content,
        IReadOnlyDictionary<string, object?>? projections = null,
        WriteOptions? options = null)
    {
        return Session.Update(Values(id, schemaVersion, content, projections), options);
    }

    public WriteOutcome ConditionalUpsert(
        string id,
        string schemaVersion,
        object content,
        long expectedVersion,
        IReadOnlyDictionary<string, object?>? projections = null)
    {
        if (Session is not IConcurrencyStorageSession concurrency)
            throw new NotSupportedException(
                "The selected Groundwork provider does not advertise optimistic row concurrency.");

        return concurrency.ConditionalUpsert(
            Values(id, schemaVersion, content, projections),
            new WriteOptions { Precondition = WritePrecondition.IfVersion(expectedVersion) });
    }

    public WriteOutcome Delete(string id, WriteOptions? options = null) => Session.Delete(Key(id), options);

    public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) =>
        Session.Query(request, options);

    public AggregationResult Aggregate(AggregationQuery query) => Session.Aggregate(query);

    public static StorageKey Key(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new StorageKey(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.IdField] = id
        });
    }

    public static StorageValues Values(
        string id,
        string schemaVersion,
        object content,
        IReadOnlyDictionary<string, object?>? projections = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        ArgumentNullException.ThrowIfNull(content);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.IdField] = id,
            [ElsaRuntimeV2StorageManifest.SchemaVersionField] = schemaVersion,
            [ElsaRuntimeV2StorageManifest.ContentField] = content
        };

        if (projections is not null)
        {
            foreach (var projection in projections)
            {
                if (projection.Key is ElsaRuntimeV2StorageManifest.IdField or
                    ElsaRuntimeV2StorageManifest.SchemaVersionField or
                    ElsaRuntimeV2StorageManifest.ContentField)
                {
                    throw new ArgumentException(
                        $"Projection '{projection.Key}' is owned by the runtime row envelope.",
                        nameof(projections));
                }

                values[projection.Key] = projection.Value;
            }
        }

        return new StorageValues(values);
    }

    private static bool IsRuntimeKey(IReadOnlyList<string> keyColumns) =>
        (keyColumns.Count == 1 &&
         StringComparer.Ordinal.Equals(keyColumns[0], ElsaRuntimeV2StorageManifest.IdField)) ||
        (keyColumns.Count == 2 &&
         keyColumns.Contains(ElsaRuntimeV2StorageManifest.IdField, StringComparer.Ordinal) &&
         keyColumns.Contains(ProviderOwnedScopeColumn, StringComparer.Ordinal));
}
