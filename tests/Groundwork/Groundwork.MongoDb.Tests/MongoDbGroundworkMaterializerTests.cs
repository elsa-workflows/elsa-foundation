using Groundwork.Core.Manifests;
using Groundwork.MongoDb.Materialization;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace Groundwork.MongoDb.Tests;

public sealed class MongoDbGroundworkMaterializerTests : IAsyncLifetime
{
    private readonly MongoDbContainer container = new MongoDbBuilder("mongo:7.0.24").Build();

    public async Task InitializeAsync() => await container.StartAsync();

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task MaterializeCreatesCollectionIndexesAndSchemaHistoryIdempotently()
    {
        var database = CreateDatabase();
        var manifest = MongoDbTestManifests.MetadataManifest();
        var materializer = new MongoDbGroundworkMaterializer(database);

        await materializer.MaterializeAsync(manifest, MongoDbTestManifests.Provider);
        await materializer.MaterializeAsync(manifest, MongoDbTestManifests.Provider);

        var collectionNames = await (await database.ListCollectionNamesAsync()).ToListAsync();
        Assert.Contains(MongoDbGroundworkNames.CollectionName(manifest.StorageUnits.Single()), collectionNames);
        Assert.Contains("groundwork_schema_history", collectionNames);
        Assert.Equal(1, await CountSchemaHistoryRows(database));

        var indexNames = await ReadIndexNames(database.GetCollection<BsonDocument>(MongoDbGroundworkNames.CollectionName(manifest.StorageUnits.Single())));
        Assert.Contains("by-key", indexNames);
        Assert.Contains("by-category", indexNames);
    }

    [Fact]
    public async Task MaterializeToleratesConcurrentCollectionCreation()
    {
        var database = CreateDatabase();
        var manifest = MongoDbTestManifests.MetadataManifest();
        var materializers = Enumerable
            .Range(0, 8)
            .Select(_ => new MongoDbGroundworkMaterializer(database));

        await Task.WhenAll(materializers.Select(materializer => materializer.MaterializeAsync(manifest, MongoDbTestManifests.Provider)));

        var collectionNames = await (await database.ListCollectionNamesAsync()).ToListAsync();
        Assert.Contains(MongoDbGroundworkNames.CollectionName(manifest.StorageUnits.Single()), collectionNames);
        Assert.Equal(1, await CountSchemaHistoryRows(database));
    }

    [Fact]
    public async Task MaterializeRecordsAdvisoryWhenExistingIndexOptionsConflict()
    {
        var database = CreateDatabase();
        var manifest = MongoDbTestManifests.MetadataManifest();
        var advisories = new List<string>();

        await new MongoDbGroundworkMaterializer(database).MaterializeAsync(manifest, MongoDbTestManifests.Provider);

        var changedManifest = WithUniqueCategoryIndex(manifest);
        await new MongoDbGroundworkMaterializer(database, advisories.Add).MaterializeAsync(changedManifest, MongoDbTestManifests.Provider);

        Assert.Contains(advisories, advisory => advisory.Contains("by-category", StringComparison.Ordinal));
        Assert.Equal(1, await CountSchemaHistoryRows(database));
    }

    [Fact]
    public async Task MaterializeRecordsAdvisoryWhenSchemaHistoryIndexConflicts()
    {
        var database = CreateDatabase();
        var advisories = new List<string>();
        await database.CreateCollectionAsync(MongoDbGroundworkNames.SchemaHistoryCollection);
        var schemaHistory = database.GetCollection<BsonDocument>(MongoDbGroundworkNames.SchemaHistoryCollection);
        await schemaHistory.Indexes.CreateOneAsync(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("manifest_id"),
            new CreateIndexOptions { Name = "ux_groundwork_schema_history_identity" }));

        await new MongoDbGroundworkMaterializer(database, advisories.Add)
            .MaterializeAsync(MongoDbTestManifests.MetadataManifest(), MongoDbTestManifests.Provider);

        Assert.Contains(advisories, advisory => advisory.Contains("ux_groundwork_schema_history_identity", StringComparison.Ordinal));
        Assert.Equal(1, await CountSchemaHistoryRows(database));
    }

    [Fact]
    public async Task MaterializeEncodesLongCollectionNamesDeterministically()
    {
        var database = CreateDatabase();
        var manifest = MongoDbTestManifests.MetadataManifest();
        var unit = manifest.StorageUnits.Single() with
        {
            Identity = new StorageUnitIdentity("RuntimeEntityInstance.Customer.Profile.WithSymbols$AndAnIntentionallyLongIdentityThatWouldOtherwiseRiskMongoNamespaceLimits")
        };
        manifest = manifest with { StorageUnits = [unit] };

        await new MongoDbGroundworkMaterializer(database).MaterializeAsync(manifest, MongoDbTestManifests.Provider);

        var collectionName = MongoDbGroundworkNames.CollectionName(unit);
        var collectionNames = await (await database.ListCollectionNamesAsync()).ToListAsync();
        Assert.Contains(collectionName, collectionNames);
        Assert.DoesNotContain("$", collectionName);
        Assert.True(collectionName.Length <= "groundwork_".Length + MongoDbGroundworkNames.MaxEncodedIdentityLength);
    }

    private IMongoDatabase CreateDatabase() =>
        new MongoClient(container.GetConnectionString()).GetDatabase($"groundwork_{Guid.NewGuid():N}");

    private static StorageManifest WithUniqueCategoryIndex(StorageManifest manifest)
    {
        var unit = manifest.StorageUnits.Single();
        return manifest with
        {
            StorageUnits =
            [
                unit with
                {
                    Indexes = unit.Indexes
                        .Select(index => index.Identity == "by-category" ? index with { IsUnique = true } : index)
                        .ToList()
                }
            ]
        };
    }

    private static async Task<long> CountSchemaHistoryRows(IMongoDatabase database)
    {
        var collection = database.GetCollection<BsonDocument>("groundwork_schema_history");
        return await collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("provider_name", MongoDbTestManifests.Provider.Name));
    }

    private static async Task<IReadOnlyList<string>> ReadIndexNames(IMongoCollection<BsonDocument> collection)
    {
        var cursor = await collection.Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();
        return indexes.Select(index => index.GetValue("name").AsString).ToList();
    }
}
