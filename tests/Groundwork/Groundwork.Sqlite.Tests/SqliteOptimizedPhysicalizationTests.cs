using Groundwork.Core.Manifests;
using Groundwork.Core.Physicalization;
using Groundwork.Documents.Store;
using Groundwork.Relational.Physicalization;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.Materialization;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Groundwork.Sqlite.Tests;

public sealed class SqliteOptimizedPhysicalizationTests
{
    [Fact]
    public async Task OptimizedUnitsCreateMaintainAndQueryProjectionTables()
    {
        await using var harness = await SqliteOptimizedHarness.Create();
        var projectionTable = RelationalPhysicalizationNames.TableName(harness.Unit);

        Assert.True(await harness.TableExistsAsync(projectionTable));

        var saved = await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"alpha","category":"system"}"""));

        Assert.Equal(DocumentStoreWriteStatus.Saved, saved.Status);
        Assert.Equal(("alpha", "system", 1L), await harness.LoadProjectionAsync("doc-1"));
        Assert.Single(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "alpha")));

        var updated = await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"beta","category":"application"}""",
            ExpectedVersion: 1));

        Assert.Equal(DocumentStoreWriteStatus.Saved, updated.Status);
        Assert.Equal(("beta", "application", 2L), await harness.LoadProjectionAsync("doc-1"));
        Assert.Empty(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "alpha")));
        Assert.Single(await harness.Store.QueryAsync(new DocumentStoreQuery("configurationDocument", "by-key", "beta")));
    }

    [Fact]
    public async Task StaleWritesDoNotUpdateOptimizedProjectionRows()
    {
        await using var harness = await SqliteOptimizedHarness.Create();
        await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"alpha","category":"system"}"""));

        var stale = await harness.Store.SaveAsync(new SaveDocumentRequest(
            "configurationDocument",
            "doc-1",
            "1.0.0",
            """{"key":"beta","category":"application"}""",
            ExpectedVersion: 2));

        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, stale.Status);
        Assert.Equal(("alpha", "system", 1L), await harness.LoadProjectionAsync("doc-1"));
    }

    private sealed class SqliteOptimizedHarness : IAsyncDisposable
    {
        private SqliteOptimizedHarness(SqliteConnection connection, SqliteDocumentStore store, StorageUnit unit)
        {
            Connection = connection;
            Store = store;
            Unit = unit;
        }

        private SqliteConnection Connection { get; }
        public SqliteDocumentStore Store { get; }
        public StorageUnit Unit { get; }

        public static async Task<SqliteOptimizedHarness> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            var manifest = SqliteTestManifests.MetadataManifest();
            var unit = manifest.StorageUnits.Single() with { Physicalization = PhysicalizationPolicy.Optimized };
            manifest = manifest with { StorageUnits = [unit] };
            await new SqliteGroundworkMaterializer(connection).MaterializeAsync(manifest, SqliteTestManifests.Provider);
            return new SqliteOptimizedHarness(connection, new SqliteDocumentStore(connection, manifest), unit);
        }

        public async Task<bool> TableExistsAsync(string tableName)
        {
            await using var command = Connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $table;";
            command.Parameters.AddWithValue("$table", tableName);
            return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
        }

        public async Task<(string Key, string Category, long Version)> LoadProjectionAsync(string documentId)
        {
            var fields = PhysicalizationProjection.EligibleFields(Unit).ToDictionary(field => field.Name, StringComparer.Ordinal);
            var keyColumn = RelationalPhysicalizationNames.ColumnName(fields["by-key"]);
            var categoryColumn = RelationalPhysicalizationNames.ColumnName(fields["by-category"]);
            await using var command = Connection.CreateCommand();
            command.CommandText = $"""
                SELECT {keyColumn}, {categoryColumn}, document_version
                FROM {RelationalPhysicalizationNames.TableName(Unit)}
                WHERE document_kind = $kind AND document_id = $id;
                """;
            command.Parameters.AddWithValue("$kind", Unit.Identity.Value);
            command.Parameters.AddWithValue("$id", documentId);

            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            return (reader.GetString(0), reader.GetString(1), reader.GetInt64(2));
        }

        public async ValueTask DisposeAsync() => await Connection.DisposeAsync();
    }
}
