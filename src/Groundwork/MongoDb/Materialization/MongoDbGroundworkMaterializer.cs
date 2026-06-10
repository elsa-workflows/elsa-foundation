using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Groundwork.MongoDb.Materialization;

public sealed class MongoDbGroundworkMaterializer(IMongoDatabase database)
{
    public async Task MaterializeAsync(StorageManifest manifest, ProviderIdentity provider, CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(MongoDbGroundworkNames.SchemaHistoryCollection, cancellationToken);
        await EnsureSchemaHistoryIndexAsync(cancellationToken);

        foreach (var unit in manifest.StorageUnits)
        {
            var collectionName = MongoDbGroundworkNames.CollectionName(unit);
            await EnsureCollectionAsync(collectionName, cancellationToken);
            await EnsureDeclaredIndexesAsync(database.GetCollection<BsonDocument>(collectionName), unit, cancellationToken);
        }

        await RecordSchemaHistoryAsync(manifest, provider, cancellationToken);
    }

    private async Task EnsureCollectionAsync(string collectionName, CancellationToken cancellationToken)
    {
        var cursor = await database.ListCollectionNamesAsync(cancellationToken: cancellationToken);
        var names = await cursor.ToListAsync(cancellationToken);
        if (!names.Contains(collectionName, StringComparer.Ordinal))
            await database.CreateCollectionAsync(collectionName, cancellationToken: cancellationToken);
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
        await collection.Indexes.CreateOneAsync(model, cancellationToken: cancellationToken);
    }

    private static async Task EnsureDeclaredIndexesAsync(IMongoCollection<BsonDocument> collection, StorageUnit unit, CancellationToken cancellationToken)
    {
        foreach (var index in unit.Indexes.Where(index => index.Fields.Count == 1))
        {
            var keys = Builders<BsonDocument>.IndexKeys.Ascending($"content.{index.Fields[0].Path}");
            var options = new CreateIndexOptions
            {
                Name = index.Identity,
                Unique = index.IsUnique,
                Sparse = index.MissingValueBehavior == Groundwork.Core.Indexing.MissingValueBehavior.Excluded
            };
            await collection.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(keys, options), cancellationToken: cancellationToken);
        }
    }

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
