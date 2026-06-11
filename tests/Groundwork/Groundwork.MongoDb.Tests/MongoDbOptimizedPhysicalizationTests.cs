using Groundwork.Core.Manifests;
using Groundwork.Core.Physicalization;
using Groundwork.Documents.Store;
using Groundwork.MongoDb.Documents;
using Groundwork.MongoDb.Materialization;
using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace Groundwork.MongoDb.Tests;

public sealed class MongoDbOptimizedPhysicalizationTests : IAsyncLifetime
{
    private readonly MongoDbContainer container = new MongoDbBuilder("mongo:7.0.24").Build();

    public async Task InitializeAsync() => await container.StartAsync();

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task OptimizedUnitsCreateMaintainAndQueryPhysicalizedFields()
    {
        await using var harness = await MongoDbOptimizedHarness.Create(container.GetConnectionString());
        var id = $"doc-{Guid.NewGuid():N}";
        var firstKey = $"alpha-{Guid.NewGuid():N}";
        var secondKey = $"beta-{Guid.NewGuid():N}";

        await harness.AssertIndexTargetsAsync("by-key", harness.PhysicalizedPath("by-key"));

        var saved = await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            id,
            "1.0.0",
            $$"""{"key":"{{firstKey}}","category":"system"}"""));

        Assert.Equal(DocumentStoreWriteStatus.Saved, saved.Status);
        Assert.Equal((firstKey, "system", 1L), await harness.LoadPhysicalizedAsync(id));
        Assert.Single(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", firstKey)));

        var updated = await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            id,
            "1.0.0",
            $$"""{"key":"{{secondKey}}","category":"application"}""",
            ExpectedVersion: 1));

        Assert.Equal(DocumentStoreWriteStatus.Saved, updated.Status);
        Assert.Equal((secondKey, "application", 2L), await harness.LoadPhysicalizedAsync(id));
        Assert.Empty(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", firstKey)));
        Assert.Single(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", secondKey)));
    }

    [Fact]
    public async Task StaleWritesDoNotUpdatePhysicalizedFields()
    {
        await using var harness = await MongoDbOptimizedHarness.Create(container.GetConnectionString());
        var id = $"doc-{Guid.NewGuid():N}";
        var key = $"alpha-{Guid.NewGuid():N}";

        await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            id,
            "1.0.0",
            $$"""{"key":"{{key}}","category":"system"}"""));

        var stale = await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            id,
            "1.0.0",
            $$"""{"key":"beta-{{Guid.NewGuid():N}}","category":"application"}""",
            ExpectedVersion: 2));

        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, stale.Status);
        Assert.Equal((key, "system", 1L), await harness.LoadPhysicalizedAsync(id));
    }

    [Fact]
    public void PhysicalizedFieldNamesAreLengthCapped()
    {
        var field = new PhysicalizedFieldPlan(
            "Runtime.Entity/Customer.Profile#Segment:With:Symbols.And.An.Intentionally.Long.Identity.That.Expands.When.Encoded",
            "customer.profile.segment",
            Groundwork.Core.Indexing.IndexValueKind.String,
            false,
            true);

        var fieldName = MongoDbGroundworkNames.PhysicalizedFieldName(field);

        Assert.True(fieldName.Length <= MongoDbGroundworkNames.MaxPhysicalizedFieldNameLength);
    }

    private sealed class MongoDbOptimizedHarness : IAsyncDisposable
    {
        private MongoDbOptimizedHarness(IMongoClient client, IMongoDatabase database, MongoDbDocumentStore store, StorageUnit unit)
        {
            Client = client;
            Database = database;
            Store = store;
            Unit = unit;
        }

        private IMongoClient Client { get; }
        private IMongoDatabase Database { get; }
        public MongoDbDocumentStore Store { get; }
        public StorageUnit Unit { get; }

        private IMongoCollection<BsonDocument> Collection =>
            Database.GetCollection<BsonDocument>(MongoDbGroundworkNames.CollectionName(Unit));

        public static async Task<MongoDbOptimizedHarness> Create(string connectionString)
        {
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase($"groundwork_{Guid.NewGuid():N}");
            var manifest = MongoDbTestManifests.MetadataManifest();
            var unit = manifest.StorageUnits.Single() with { Physicalization = PhysicalizationPolicy.Optimized };
            manifest = manifest with { StorageUnits = [unit] };
            await new MongoDbGroundworkMaterializer(database).MaterializeAsync(manifest, MongoDbTestManifests.Provider);
            return new MongoDbOptimizedHarness(client, database, new MongoDbDocumentStore(database, manifest), unit);
        }

        public string PhysicalizedPath(string indexName)
        {
            var field = PhysicalizationProjection.EligibleFields(Unit).Single(field => field.Name == indexName);
            return $"physicalized.{MongoDbGroundworkNames.PhysicalizedFieldName(field)}";
        }

        public async Task AssertIndexTargetsAsync(string indexName, string keyPath)
        {
            var indexes = await Collection.Indexes.List().ToListAsync();
            var index = Assert.Single(indexes, document => document.GetValue("name").AsString == indexName);
            Assert.True(index.GetValue("key").AsBsonDocument.Contains(keyPath));
        }

        public async Task<(string Key, string Category, long Version)> LoadPhysicalizedAsync(string id)
        {
            var document = await Collection
                .Find(Builders<BsonDocument>.Filter.Eq("_id", id))
                .SingleAsync();
            var physicalized = document.GetValue("physicalized").AsBsonDocument;
            var fields = PhysicalizationProjection.EligibleFields(Unit).ToDictionary(field => field.Name, StringComparer.Ordinal);
            return (
                physicalized.GetValue(MongoDbGroundworkNames.PhysicalizedFieldName(fields["by-key"])).AsString,
                physicalized.GetValue(MongoDbGroundworkNames.PhysicalizedFieldName(fields["by-category"])).AsString,
                document.GetValue("version").ToInt64());
        }

        public async ValueTask DisposeAsync() =>
            await Client.DropDatabaseAsync(Database.DatabaseNamespace.DatabaseName);
    }
}
