using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.Physicalization;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Groundwork.MongoDb.Materialization;

public sealed class MongoDbGroundworkMaterializer(IMongoDatabase database, Action<string>? recordAdvisory = null)
{
    private const int IndexOptionsConflictErrorCode = 85;
    private const int IndexKeySpecsConflictErrorCode = 86;
    private const int NamespaceExistsErrorCode = 48;

    public async Task MaterializeAsync(StorageManifest manifest, ProviderIdentity provider, CancellationToken cancellationToken = default)
    {
        var collections = await GetCollectionNamesAsync(cancellationToken);
        await EnsureCollectionAsync(collections, MongoDbGroundworkNames.SchemaHistoryCollection, cancellationToken);
        await EnsureSchemaHistoryIndexAsync(cancellationToken);

        foreach (var unit in manifest.StorageUnits)
        {
            var collectionName = MongoDbGroundworkNames.CollectionName(unit);
            await EnsureCollectionAsync(collections, collectionName, cancellationToken);
            await EnsureDeclaredIndexesAsync(database.GetCollection<BsonDocument>(collectionName), unit, cancellationToken);
        }

        await RecordSchemaHistoryAsync(manifest, provider, cancellationToken);
    }

    private async Task<HashSet<string>> GetCollectionNamesAsync(CancellationToken cancellationToken)
    {
        var cursor = await database.ListCollectionNamesAsync(cancellationToken: cancellationToken);
        var names = await cursor.ToListAsync(cancellationToken);
        return names.ToHashSet(StringComparer.Ordinal);
    }

    private async Task EnsureCollectionAsync(HashSet<string> collections, string collectionName, CancellationToken cancellationToken)
    {
        if (collections.Contains(collectionName))
            return;

        try
        {
            await database.CreateCollectionAsync(collectionName, cancellationToken: cancellationToken);
        }
        catch (MongoCommandException ex) when (ex.Code == NamespaceExistsErrorCode)
        {
            // NamespaceExists means another materializer created the collection after our initial snapshot.
        }

        collections.Add(collectionName);
    }

    private async Task EnsureSchemaHistoryIndexAsync(CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(MongoDbGroundworkNames.SchemaHistoryCollection);
        var keys = Builders<BsonDocument>.IndexKeys
            .Ascending("manifest_id")
            .Ascending("manifest_version")
            .Ascending("provider_name")
            .Ascending("provider_version");
        var model = new CreateIndexModel<BsonDocument>(keys, new CreateIndexOptions
        {
            Name = "ux_groundwork_schema_history_identity",
            Unique = true
        });
        await CreateIndexWithAdvisoryAsync(collection, model, model.Options.Name, cancellationToken);
    }

    private async Task EnsureDeclaredIndexesAsync(IMongoCollection<BsonDocument> collection, StorageUnit unit, CancellationToken cancellationToken)
    {
        var physicalizedFields = PhysicalizationProjection.EligibleFields(unit)
            .ToDictionary(field => field.Name, StringComparer.Ordinal);

        foreach (var index in unit.Indexes.Where(index => index.Fields.Count == 1))
        {
            var path = !physicalizedFields.TryGetValue(index.Identity, out var physicalizedField)
                ? $"content.{index.Fields[0].Path}"
                : $"physicalized.{MongoDbGroundworkNames.PhysicalizedFieldName(physicalizedField)}";
            var keys = Builders<BsonDocument>.IndexKeys.Ascending(path);
            var options = new CreateIndexOptions
            {
                Name = index.Identity,
                Unique = index.IsUnique,
                Sparse = index.MissingValueBehavior == Groundwork.Core.Indexing.MissingValueBehavior.Excluded
            };
            await CreateIndexWithAdvisoryAsync(collection, new CreateIndexModel<BsonDocument>(keys, options), index.Identity, cancellationToken);
        }
    }

    private async Task CreateIndexWithAdvisoryAsync(
        IMongoCollection<BsonDocument> collection,
        CreateIndexModel<BsonDocument> model,
        string indexName,
        CancellationToken cancellationToken)
    {
        try
        {
            await collection.Indexes.CreateOneAsync(model, cancellationToken: cancellationToken);
        }
        catch (MongoCommandException ex) when (IsIndexConflictException(ex))
        {
            recordAdvisory?.Invoke(
                $"MongoDB index '{indexName}' on collection '{collection.CollectionNamespace.CollectionName}' conflicts with the declared Groundwork index. Drop or rebuild the existing index to apply changed index keys or options.");
        }
    }

    private static bool IsIndexConflictException(MongoCommandException exception) =>
        exception.Code is IndexOptionsConflictErrorCode or IndexKeySpecsConflictErrorCode;

    private async Task RecordSchemaHistoryAsync(StorageManifest manifest, ProviderIdentity provider, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(MongoDbGroundworkNames.SchemaHistoryCollection);
        var filter = Builders<BsonDocument>.Filter.Eq("manifest_id", manifest.Identity.Value) &
                     Builders<BsonDocument>.Filter.Eq("manifest_version", manifest.Version.Value) &
                     Builders<BsonDocument>.Filter.Eq("provider_name", provider.Name) &
                     Builders<BsonDocument>.Filter.Eq("provider_version", provider.Version);
        var update = Builders<BsonDocument>.Update
            .SetOnInsert("manifest_id", manifest.Identity.Value)
            .SetOnInsert("manifest_version", manifest.Version.Value)
            .SetOnInsert("provider_name", provider.Name)
            .SetOnInsert("provider_version", provider.Version)
            .SetOnInsert("applied_utc", DateTimeOffset.UtcNow.ToString("O"));

        await collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken);
    }
}
