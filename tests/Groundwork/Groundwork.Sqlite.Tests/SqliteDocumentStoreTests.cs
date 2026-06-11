using System.Text.Json;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Groundwork.Relational.Documents;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.Materialization;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Groundwork.Sqlite.Tests;

public sealed class SqliteDocumentStoreTests
{
    [Fact]
    public async Task SaveLoadUpdateQueryAndDeleteMaintainIndexes()
    {
        await using var harness = await SqliteDocumentStoreHarness.Create();
        var store = harness.Store;

        var saved = await store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"alpha","category":"system","value":1}"""));

        Assert.Equal(DocumentStoreWriteStatus.Saved, saved.Status);
        Assert.Equal(1, saved.Document!.Version);

        var loaded = await store.LoadAsync("configurationDocument", "doc-1");
        Assert.NotNull(loaded);
        using var loadedContent = JsonDocument.Parse(loaded.ContentJson);
        Assert.Equal("alpha", loadedContent.RootElement.GetProperty("key").GetString());

        var byKey = await store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "alpha"));
        Assert.Single(byKey);

        var updated = await store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"beta","category":"application","value":2}""",
            ExpectedVersion: 1));

        Assert.Equal(DocumentStoreWriteStatus.Saved, updated.Status);
        Assert.Equal(2, updated.Document!.Version);

        Assert.Empty(await store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "alpha")));
        Assert.Single(await store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "beta")));
        Assert.Single(await store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-category", "application")));

        var deleted = await store.DeleteAsync(new DeleteDocumentRequest("configurationDocument", "doc-1", ExpectedVersion: 2));

        Assert.Equal(DocumentStoreWriteStatus.Deleted, deleted.Status);
        Assert.Null(await store.LoadAsync("configurationDocument", "doc-1"));
        Assert.Empty(await store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "beta")));
    }

    [Fact]
    public async Task UndeclaredIndexQueryFailsClearly()
    {
        await using var harness = await SqliteDocumentStoreHarness.Create();

        var exception = await Assert.ThrowsAsync<UndeclaredDocumentIndexException>(() =>
            harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "missing-index", "alpha")));

        Assert.Equal("configurationDocument", exception.DocumentKind);
        Assert.Equal("missing-index", exception.IndexName);
    }

    [Fact]
    public async Task UniqueIndexesAreEnforcedBySQLite()
    {
        await using var harness = await SqliteDocumentStoreHarness.Create();
        await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"alpha","category":"system"}"""));

        var duplicate = await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-2",
            "1.0.0",
            """{"key":"alpha","category":"system"}"""));

        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, duplicate.Status);
    }

    [Fact]
    public async Task CompoundIndexesAreNotQueryableUntilPortableSupportExists()
    {
        var manifest = WithCompoundIndex(SqliteTestManifests.MetadataManifest());
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await new SqliteGroundworkMaterializer(connection).MaterializeAsync(manifest, SqliteTestManifests.Provider);
        var store = new SqliteDocumentStore(connection, manifest);

        var exception = await Assert.ThrowsAsync<UndeclaredDocumentIndexException>(() =>
            store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key-and-category", "alpha")));

        Assert.Equal("configurationDocument", exception.DocumentKind);
        Assert.Equal("by-key-and-category", exception.IndexName);
    }

    [Fact]
    public async Task MissingRowDuringUnguardedUpdateReturnsNotFound()
    {
        await using var harness = await SqliteDocumentStoreHarness.Create();
        var store = new RelationalDocumentStore(harness.Connection, SqliteTestManifests.MetadataManifest(), new MissingUpdateDialect());

        await store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"alpha","category":"system"}"""));

        var result = await store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"beta","category":"system"}"""));

        Assert.Equal(DocumentStoreWriteStatus.NotFound, result.Status);
        Assert.Single(await store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "alpha")));
        Assert.Empty(await store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "beta")));
    }

    [Fact]
    public async Task MissingRowDuringUnguardedDeleteReturnsNotFound()
    {
        await using var harness = await SqliteDocumentStoreHarness.Create();
        var store = new RelationalDocumentStore(harness.Connection, SqliteTestManifests.MetadataManifest(), new MissingDeleteDialect());

        await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"alpha","category":"system"}"""));

        var result = await store.DeleteAsync(new DeleteDocumentRequest("configurationDocument", "doc-1"));

        Assert.Equal(DocumentStoreWriteStatus.NotFound, result.Status);
        Assert.NotNull(await harness.Store.LoadAsync("configurationDocument", "doc-1"));
        Assert.Single(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "alpha")));
    }

    [Fact]
    public async Task DependencyFailureDuringIndexRefreshReturnsNotFoundAndRollsBack()
    {
        await using var harness = await SqliteDocumentStoreHarness.Create();
        var store = new RelationalDocumentStore(harness.Connection, SqliteTestManifests.MetadataManifest(), new DependencyFailureDialect());

        var result = await store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"alpha","category":"system"}"""));

        Assert.Equal(DocumentStoreWriteStatus.NotFound, result.Status);
        Assert.Null(await harness.Store.LoadAsync("configurationDocument", "doc-1"));
        Assert.Empty(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "alpha")));
    }

    [Fact]
    public async Task StaleExpectedVersionDoesNotUpdateDocumentOrIndexes()
    {
        await using var harness = await SqliteDocumentStoreHarness.Create();
        await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"alpha","category":"system"}"""));
        await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"beta","category":"system"}""",
            ExpectedVersion: 1));

        var stale = await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"gamma","category":"system"}""",
            ExpectedVersion: 1));

        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, stale.Status);
        Assert.Single(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "beta")));
        Assert.Empty(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "gamma")));
    }

    [Fact]
    public async Task StaleExpectedVersionDoesNotDeleteDocumentOrIndexes()
    {
        await using var harness = await SqliteDocumentStoreHarness.Create();
        await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"alpha","category":"system"}"""));
        await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"beta","category":"system"}""",
            ExpectedVersion: 1));

        var stale = await harness.Store.DeleteAsync(new DeleteDocumentRequest("configurationDocument", "doc-1", ExpectedVersion: 1));

        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, stale.Status);
        Assert.NotNull(await harness.Store.LoadAsync("configurationDocument", "doc-1"));
        Assert.Single(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "beta")));
    }

    private sealed class SqliteDocumentStoreHarness : IAsyncDisposable
    {
        private SqliteDocumentStoreHarness(SqliteConnection connection, SqliteDocumentStore store)
        {
            Connection = connection;
            Store = store;
        }

        public SqliteConnection Connection { get; }
        public SqliteDocumentStore Store { get; }

        public static async Task<SqliteDocumentStoreHarness> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            var manifest = SqliteTestManifests.MetadataManifest();
            await new SqliteGroundworkMaterializer(connection).MaterializeAsync(manifest, SqliteTestManifests.Provider);
            return new SqliteDocumentStoreHarness(connection, new SqliteDocumentStore(connection, manifest));
        }

        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
    }

    private static StorageManifest WithCompoundIndex(StorageManifest manifest)
    {
        var unit = manifest.StorageUnits.Single();
        var compoundIndex = new IndexDeclaration(
            "by-key-and-category",
            [new IndexField("key"), new IndexField("category")],
            IndexValueKind.Keyword,
            true,
            true,
            MissingValueBehavior.Excluded,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal });

        return manifest with { StorageUnits = [unit with { Indexes = [compoundIndex] }] };
    }

    private sealed class MissingUpdateDialect : RelationalDocumentStoreDialect
    {
        public override string UpdateDocumentSql => $$"""
            UPDATE groundwork_documents
            SET schema_version = {{Parameter("schemaVersion")}},
                version = {{Parameter("version")}},
                content_json = {{Parameter("content")}},
                updated_utc = {{Parameter("updatedUtc")}}
            WHERE document_kind = {{Parameter("kind")}} AND id = '__missing__';
            """;
    }

    private sealed class MissingDeleteDialect : RelationalDocumentStoreDialect
    {
        public override string DeleteDocumentSql => $$"""
            DELETE FROM groundwork_documents
            WHERE document_kind = {{Parameter("kind")}} AND id = '__missing__';
            """;
    }

    private sealed class DependencyFailureDialect : RelationalDocumentStoreDialect
    {
        public override string InsertIndexSql => "SELECT missing_write_dependency;";

        public override bool IsWriteDependencyException(System.Data.Common.DbException exception) => exception is SqliteException;
    }
}
