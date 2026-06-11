using Groundwork.Sqlite.Materialization;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Groundwork.Sqlite.Tests;

public sealed class SqliteGroundworkMaterializerTests
{
    [Fact]
    public async Task MaterializeCreatesTablesAndSchemaHistoryIdempotently()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        var materializer = new SqliteGroundworkMaterializer(connection);
        var manifest = SqliteTestManifests.MetadataManifest();

        await materializer.MaterializeAsync(manifest, SqliteTestManifests.Provider);
        await materializer.MaterializeAsync(manifest, SqliteTestManifests.Provider);

        Assert.True(await TableExists(connection, "groundwork_documents"));
        Assert.True(await TableExists(connection, "groundwork_document_indexes"));
        Assert.True(await TableExists(connection, "groundwork_schema_history"));
        Assert.Equal(1, await CountRows(connection, "groundwork_schema_history"));
    }

    private static async Task<bool> TableExists(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<long> CountRows(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
