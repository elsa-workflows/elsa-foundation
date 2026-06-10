using System.Text.Json;
using Elsa.Persistence.Groundwork.RuntimeEntities;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.Materialization;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkRuntimeEntityStoreTests
{
    [Fact]
    public async Task StoreSavesLoadsQueriesAndDeletesRuntimeEntityInstances()
    {
        await using var harness = await RuntimeEntityHarness.Create();
        var definition = harness.Definition;
        var store = harness.Store;

        var savedDefinition = await store.SaveDefinitionAsync(definition);
        Assert.Equal(DocumentStoreWriteStatus.Saved, savedDefinition.Status);
        var loadedDefinition = await store.LoadDefinitionAsync(definition.EntityType);
        Assert.NotNull(loadedDefinition);
        Assert.Equal(definition.EntityType, loadedDefinition.EntityType);
        Assert.Equal(definition.DisplayName, loadedDefinition.DisplayName);
        Assert.Equal(definition.Indexes, loadedDefinition.Indexes);
        Assert.Equal(definition.Fields, loadedDefinition.Fields);

        var saved = await store.SaveInstanceAsync(
            definition,
            "customer-1",
            """{"email":"one@example.com","segment":"vip"}""");

        Assert.Equal(DocumentStoreWriteStatus.Saved, saved.Status);

        var loaded = await store.LoadInstanceAsync(definition, "customer-1");
        Assert.NotNull(loaded);
        using var loadedContent = JsonDocument.Parse(loaded.ContentJson);
        Assert.Equal("one@example.com", loadedContent.RootElement.GetProperty("email").GetString());

        var byEmail = await store.QueryInstancesAsync(definition, "by-email", "one@example.com");
        Assert.Single(byEmail);

        var updated = await store.SaveInstanceAsync(
            definition,
            "customer-1",
            """{"email":"one@example.com","segment":"standard"}""",
            expectedVersion: 1);

        Assert.Equal(DocumentStoreWriteStatus.Saved, updated.Status);
        Assert.Empty(await store.QueryInstancesAsync(definition, "by-segment", "vip"));
        Assert.Single(await store.QueryInstancesAsync(definition, "by-segment", "standard"));

        var deleted = await store.DeleteInstanceAsync(definition, "customer-1", expectedVersion: 2);

        Assert.Equal(DocumentStoreWriteStatus.Deleted, deleted.Status);
        Assert.Null(await store.LoadInstanceAsync(definition, "customer-1"));
    }

    [Fact]
    public async Task StaleExpectedVersionDoesNotUpdateRuntimeEntityInstance()
    {
        await using var harness = await RuntimeEntityHarness.Create();
        var definition = harness.Definition;

        await harness.Store.SaveInstanceAsync(definition, "customer-1", """{"email":"one@example.com","segment":"vip"}""");
        await harness.Store.SaveInstanceAsync(definition, "customer-1", """{"email":"one@example.com","segment":"standard"}""", expectedVersion: 1);

        var stale = await harness.Store.SaveInstanceAsync(
            definition,
            "customer-1",
            """{"email":"one@example.com","segment":"lost"}""",
            expectedVersion: 1);

        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, stale.Status);
        Assert.Single(await harness.Store.QueryInstancesAsync(definition, "by-segment", "standard"));
        Assert.Empty(await harness.Store.QueryInstancesAsync(definition, "by-segment", "lost"));
    }

    [Fact]
    public async Task UniqueRuntimeEntityIndexesAreEnforcedByProvider()
    {
        await using var harness = await RuntimeEntityHarness.Create();
        var definition = harness.Definition;

        await harness.Store.SaveInstanceAsync(definition, "customer-1", """{"email":"one@example.com","segment":"vip"}""");

        await Assert.ThrowsAnyAsync<Exception>(() =>
            harness.Store.SaveInstanceAsync(definition, "customer-2", """{"email":"one@example.com","segment":"vip"}"""));
    }

    private sealed class RuntimeEntityHarness : IAsyncDisposable
    {
        private RuntimeEntityHarness(SqliteConnection connection, RuntimeEntityDefinition definition, GroundworkRuntimeEntityStore store)
        {
            Connection = connection;
            Definition = definition;
            Store = store;
        }

        private SqliteConnection Connection { get; }
        public RuntimeEntityDefinition Definition { get; }
        public GroundworkRuntimeEntityStore Store { get; }

        public static async Task<RuntimeEntityHarness> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            var definition = RuntimeEntityManifestFactoryTests.CustomerProfileDefinition();
            var manifest = RuntimeEntityManifestFactory.Create(definition);
            await new SqliteGroundworkMaterializer(connection).MaterializeAsync(manifest, new("groundwork-sqlite", "1.0.0"));
            var documentStore = new SqliteDocumentStore(connection, manifest);
            return new RuntimeEntityHarness(connection, definition, new GroundworkRuntimeEntityStore(documentStore));
        }

        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
    }
}
