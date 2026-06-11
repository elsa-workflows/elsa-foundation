using System.Text.Json;
using Groundwork.Documents.Store;
using Groundwork.MongoDb.Documents;
using Groundwork.MongoDb.Materialization;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace Groundwork.MongoDb.Tests;

public sealed class MongoDbDocumentStoreTests : IAsyncLifetime
{
    private readonly MongoDbContainer container = new MongoDbBuilder("mongo:7.0.24").Build();

    public async Task InitializeAsync() => await container.StartAsync();

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact]
    public async Task SaveLoadUpdateQueryAndDeleteMaintainIndexes()
    {
        await using var harness = await MongoDbDocumentStoreHarness.Create(container.GetConnectionString());
        var store = harness.Store;
        var id = NewId();
        var firstKey = NewValue("alpha");
        var secondKey = NewValue("beta");

        var saved = await store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            id,
            "1.0.0",
            $$"""{"key":"{{firstKey}}","category":"system","value":1}"""));

        Assert.Equal(DocumentStoreWriteStatus.Saved, saved.Status);
        Assert.Equal(1, saved.Document!.Version);

        var loaded = await store.LoadAsync("configurationDocument", id);
        Assert.NotNull(loaded);
        using var loadedContent = JsonDocument.Parse(loaded.ContentJson);
        Assert.Equal(firstKey, loadedContent.RootElement.GetProperty("key").GetString());

        Assert.Single(await store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", firstKey)));

        var updated = await store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            id,
            "1.0.0",
            $$"""{"key":"{{secondKey}}","category":"application","value":2}""",
            ExpectedVersion: 1));

        Assert.Equal(DocumentStoreWriteStatus.Saved, updated.Status);
        Assert.Equal(2, updated.Document!.Version);

        Assert.Empty(await store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", firstKey)));
        Assert.Single(await store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", secondKey)));
        Assert.Contains(await store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-category", "application")), document => document.Id == id);

        var deleted = await store.DeleteAsync(new DeleteDocumentRequest("configurationDocument", id, ExpectedVersion: 2));

        Assert.Equal(DocumentStoreWriteStatus.Deleted, deleted.Status);
        Assert.Null(await store.LoadAsync("configurationDocument", id));
        Assert.Empty(await store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", secondKey)));
    }

    [Fact]
    public async Task UndeclaredIndexQueryFailsClearly()
    {
        await using var harness = await MongoDbDocumentStoreHarness.Create(container.GetConnectionString());

        var exception = await Assert.ThrowsAsync<UndeclaredDocumentIndexException>(() =>
            harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "missing-index", NewValue("alpha"))));

        Assert.Equal("configurationDocument", exception.DocumentKind);
        Assert.Equal("missing-index", exception.IndexName);
    }

    [Fact]
    public async Task UniqueIndexesAreEnforcedByMongoDb()
    {
        await using var harness = await MongoDbDocumentStoreHarness.Create(container.GetConnectionString());
        var key = NewValue("unique");

        await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            NewId(),
            "1.0.0",
            $$"""{"key":"{{key}}","category":"system"}"""));

        var duplicate = await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            NewId(),
            "1.0.0",
            $$"""{"key":"{{key}}","category":"system"}"""));

        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, duplicate.Status);
    }

    [Fact]
    public async Task QueryWithZeroTakeReturnsNoDocuments()
    {
        await using var harness = await MongoDbDocumentStoreHarness.Create(container.GetConnectionString());
        var key = NewValue("zero-take");

        await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            NewId(),
            "1.0.0",
            $$"""{"key":"{{key}}","category":"system"}"""));

        var documents = await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", key, take: 0));

        Assert.Empty(documents);
    }

    [Fact]
    public async Task SparseUniqueIndexesAllowMissingValues()
    {
        await using var harness = await MongoDbDocumentStoreHarness.Create(container.GetConnectionString());

        var first = await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            NewId(),
            "1.0.0",
            """{"category":"system"}"""));
        var second = await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            NewId(),
            "1.0.0",
            """{"category":"system"}"""));

        Assert.Equal(DocumentStoreWriteStatus.Saved, first.Status);
        Assert.Equal(DocumentStoreWriteStatus.Saved, second.Status);
    }

    [Fact]
    public async Task ConcurrentUnguardedSavesForSameIdReturnStructuredResults()
    {
        await using var harness = await MongoDbDocumentStoreHarness.Create(container.GetConnectionString());
        var id = NewId();
        var saves = Enumerable
            .Range(0, 8)
            .Select(index => harness.Store.SaveAsync(new SaveDocumentRequest(
                "configurationDocument",
                id,
                "1.0.0",
                $$"""{"key":"{{NewValue($"same-id-{index}")}}","category":"system"}""")));

        var results = await Task.WhenAll(saves);
        var expectedStatuses = new HashSet<DocumentStoreWriteStatus>
        {
            DocumentStoreWriteStatus.Saved,
            DocumentStoreWriteStatus.ConcurrencyConflict
        };

        Assert.All(results, result => Assert.Contains(
            result.Status,
            expectedStatuses));
        Assert.NotNull(await harness.Store.LoadAsync("configurationDocument", id));
    }

    [Fact]
    public async Task LoadedContentJsonRemainsStandardJsonForLargeNumbers()
    {
        await using var harness = await MongoDbDocumentStoreHarness.Create(container.GetConnectionString());
        var id = NewId();
        const long largeValue = 1717254000000;

        await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            id,
            "1.0.0",
            $$"""{"key":"{{NewValue("large")}}","category":"system","value":{{largeValue}}}"""));

        var loaded = await harness.Store.LoadAsync("configurationDocument", id);

        Assert.NotNull(loaded);
        using var content = JsonDocument.Parse(loaded.ContentJson);
        Assert.Equal(largeValue, content.RootElement.GetProperty("value").GetInt64());
    }

    [Fact]
    public async Task StaleExpectedVersionDoesNotUpdateDocument()
    {
        await using var harness = await MongoDbDocumentStoreHarness.Create(container.GetConnectionString());
        var id = NewId();
        var firstKey = NewValue("alpha");
        var secondKey = NewValue("beta");
        var staleKey = NewValue("gamma");

        await harness.Store.SaveAsync(new SaveDocumentRequest("configurationDocument", id, "1.0.0", $$"""{"key":"{{firstKey}}","category":"system"}"""));
        await harness.Store.SaveAsync(new SaveDocumentRequest("configurationDocument", id, "1.0.0", $$"""{"key":"{{secondKey}}","category":"system"}""", ExpectedVersion: 1));

        var stale = await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            id,
            "1.0.0",
            $$"""{"key":"{{staleKey}}","category":"system"}""",
            ExpectedVersion: 1));

        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, stale.Status);
        Assert.Single(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", secondKey)));
        Assert.Empty(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", staleKey)));
    }

    [Fact]
    public async Task StaleExpectedVersionDoesNotDeleteDocument()
    {
        await using var harness = await MongoDbDocumentStoreHarness.Create(container.GetConnectionString());
        var id = NewId();
        var key = NewValue("beta");

        await harness.Store.SaveAsync(new SaveDocumentRequest("configurationDocument", id, "1.0.0", $$"""{"key":"{{NewValue("alpha")}}","category":"system"}"""));
        await harness.Store.SaveAsync(new SaveDocumentRequest("configurationDocument", id, "1.0.0", $$"""{"key":"{{key}}","category":"system"}""", ExpectedVersion: 1));

        var stale = await harness.Store.DeleteAsync(new DeleteDocumentRequest("configurationDocument", id, ExpectedVersion: 1));

        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, stale.Status);
        Assert.NotNull(await harness.Store.LoadAsync("configurationDocument", id));
        Assert.Single(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", key)));
    }

    private static string NewId() => $"doc-{Guid.NewGuid():N}";

    private static string NewValue(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private sealed class MongoDbDocumentStoreHarness : IAsyncDisposable
    {
        private MongoDbDocumentStoreHarness(IMongoClient client, IMongoDatabase database, MongoDbDocumentStore store)
        {
            Client = client;
            Database = database;
            Store = store;
        }

        private IMongoClient Client { get; }
        private IMongoDatabase Database { get; }
        public MongoDbDocumentStore Store { get; }

        public static async Task<MongoDbDocumentStoreHarness> Create(string connectionString)
        {
            var client = new MongoClient(connectionString);
            var database = client.GetDatabase($"groundwork_{Guid.NewGuid():N}");
            var manifest = MongoDbTestManifests.MetadataManifest();
            await new MongoDbGroundworkMaterializer(database).MaterializeAsync(manifest, MongoDbTestManifests.Provider);
            return new MongoDbDocumentStoreHarness(client, database, new MongoDbDocumentStore(database, manifest));
        }

        public async ValueTask DisposeAsync() =>
            await Client.DropDatabaseAsync(Database.DatabaseNamespace.DatabaseName);
    }
}
