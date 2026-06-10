using Groundwork.MongoDb.Materialization;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace Groundwork.MongoDb.Tests;

public sealed class MongoDbGroundworkMaterializerTests : IAsyncLifetime
{
    private readonly MongoDbContainer container = new MongoDbBuilder("mongo:7.0").Build();

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
        Assert.Contains("groundwork_configurationDocument", collectionNames);
        Assert.Contains("groundwork_schema_history", collectionNames);
        Assert.Equal(1, await CountSchemaHistoryRows(database));

        var indexNames = await ReadIndexNames(database.GetCollection<BsonDocument>("groundwork_configurationDocument"));
        Assert.Contains("by-key", indexNames);
        Assert.Contains("by-category", indexNames);
    }

    private IMongoDatabase CreateDatabase() =>
        new MongoClient(container.GetConnectionString()).GetDatabase($"groundwork_{Guid.NewGuid():N}");

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
