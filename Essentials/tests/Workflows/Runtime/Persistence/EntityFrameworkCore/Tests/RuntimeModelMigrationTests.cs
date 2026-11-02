using System.Data.Common;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class RuntimeModelMigrationTests
{
    [Fact]
    public async Task Additive_model_tables_upgrade_an_actual_legacy_store_without_changing_any_legacy_rows()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "HostingBaseline", "runtime.sqlite.gz");
        Assert.Equal("3fcd6e38a968d05026a2c57a188f5d5c9d6fdbc313afd9ce19c82ad3e2b21416",
            Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(fixture))));
        var directory = Directory.CreateTempSubdirectory("elsa-model-schema-upgrade-");
        try
        {
            var path = Path.Combine(directory.FullName, "runtime.db");
            await using (var input = new GZipStream(File.OpenRead(fixture), CompressionMode.Decompress))
            await using (var output = File.Create(path)) await input.CopyToAsync(output);
            var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
            await using var connection = new SqliteConnection(connectionString); await connection.OpenAsync();
            var before = await LegacyRows(connection);
            Assert.NotEmpty(before);
            Assert.DoesNotContain(RuntimeModelStateEfModule.StateTable, before.Keys);
            var history = await History(connection);
            Assert.Equal(["20261017151421_Initial"], history);
            await using (var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>()
                .UseSqlite(connection, sql => sql.MigrationsHistoryTable(RuntimeEfModule.HistoryTableName)).Options))
            {
                Assert.False(context.Database.HasPendingModelChanges());
                await context.Database.MigrateAsync();
            }
            var after = await LegacyRows(connection);
            foreach (var (table, digest) in before) Assert.Equal(digest, after[table]);
            Assert.Equal(before.Count + 2, after.Count);
            Assert.Contains(RuntimeModelStateEfModule.StateTable, after.Keys);
            Assert.Contains(RuntimeModelStateEfModule.InputTable, after.Keys);
            var upgradedHistory = await History(connection);
            Assert.Equal(history, upgradedHistory.Take(history.Length));
            Assert.Equal(["ModelExecutionState", "ModelMaterialKind"], upgradedHistory.Skip(history.Length).Select(x => x[(x.LastIndexOf('_') + 1)..]));
            Assert.Equal(history.Length + 2, upgradedHistory.Length);
            await using (var nativeKinds = connection.CreateCommand())
            {
                nativeKinds.CommandText = $"SELECT COUNT(*) FROM \"{RuntimeArtifactEfModule.WorkflowExecutableTableName}\" WHERE IsModelMaterial <> 0";
                Assert.Equal(0L, (long)(await nativeKinds.ExecuteScalarAsync())!);
            }
            // Schema upgrade only: no execution is resumed and no direct-host acceptance is implied.
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task<string[]> History(DbConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MigrationId FROM \"{RuntimeEfModule.HistoryTableName}\" ORDER BY MigrationId";
        await using var reader = await command.ExecuteReaderAsync();
        var ids = new List<string>(); while (await reader.ReadAsync()) ids.Add(reader.GetString(0)); return ids.ToArray();
    }

    private static async Task<Dictionary<string, string>> LegacyRows(DbConnection connection)
    {
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) if (reader.GetString(0) != RuntimeEfModule.HistoryTableName) tables.Add(reader.GetString(0));
        }
        var result = new Dictionary<string, string>();
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\"";
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                // Compare every original column; the newly added discriminator is verified separately above.
                var columns = Enumerable.Range(0, reader.FieldCount).Where(i =>
                    table != RuntimeArtifactEfModule.WorkflowExecutableTableName || reader.GetName(i) != "IsModelMaterial").ToArray();
                var row = columns.Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray();
                rows.Add(JsonSerializer.Serialize(row));
            }
            rows.Sort(StringComparer.Ordinal);
            result[table] = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows)));
        }
        return result;
    }
}
