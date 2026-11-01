using System.Data.Common;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeModelMigrationPostgreSqlSmokeTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PostgreSql_model_migrations_install_or_preserve_captured_rows(bool retained) => RuntimeModelMigrationProviderSmoke.RunAsync(fixture,
        (connection, interceptors) => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>()
            .UseNpgsql(connection, sql => sql.MigrationsHistoryTable(RuntimeEfModule.HistoryTableName)).AddInterceptors(interceptors).Options), retained);
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeModelMigrationSqlServerSmokeTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SqlServer_model_migrations_install_or_preserve_captured_rows(bool retained) => RuntimeModelMigrationProviderSmoke.RunAsync(fixture,
        (connection, interceptors) => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>()
            .UseSqlServer(connection, sql => sql.MigrationsHistoryTable(RuntimeEfModule.HistoryTableName)).AddInterceptors(interceptors).Options), retained);
}

[Collection(RuntimeBookmarksMySqlFixture.CollectionName)]
public sealed class RuntimeModelMigrationMySqlSmokeTests(RuntimeBookmarksMySqlFixture fixture)
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MySql_model_migrations_install_or_preserve_captured_rows(bool retained) => RuntimeModelMigrationProviderSmoke.RunAsync(fixture,
        (connection, interceptors) => new RuntimeMySqlDbContext(new DbContextOptionsBuilder<RuntimeMySqlDbContext>()
            .UseMySQL(connection, sql => sql.MigrationsHistoryTable(RuntimeEfModule.HistoryTableName)).AddInterceptors(interceptors).Options), retained);
}

internal static class RuntimeModelMigrationProviderSmoke
{
    private static readonly string[] Tables = ["elsa_runtime_workflow_executable", "elsa_runtime_workflow_execution_state"];

    public static async Task RunAsync(RuntimeBookmarksProviderFixture fixture, Func<string, IInterceptor[], RuntimeDbContext> open, bool retained)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        // The fixture may already serve other contracts. Each migration case owns a distinct database.
        var connection = new DbConnectionStringBuilder { ConnectionString = fixture.ConnectionString };
        foreach (var key in connection.Keys.Cast<string>().Where(key => key.Equals("Database", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("Initial Catalog", StringComparison.OrdinalIgnoreCase)).ToArray()) connection.Remove(key);
        connection["Database"] = "elsa_model_migration_" + Guid.NewGuid().ToString("N");
        var selected = connection.ConnectionString;
        try
        {
            await using (var context = open(selected, []))
            {
                Assert.False(context.Database.HasPendingModelChanges());
                var migrations = context.Database.GetMigrations().ToArray();
                Assert.Equal(3, migrations.Length);
                Assert.EndsWith("_Initial", migrations[0]);
                Assert.EndsWith("_ModelExecutionState", migrations[1]);
                Assert.EndsWith("_ModelMaterialKind", migrations[2]);
                string? before = null;
                if (retained)
                {
                    await context.GetService<IMigrator>().MigrateAsync(migrations[0]);
                    Assert.Equal([migrations[0]], await context.Database.GetAppliedMigrationsAsync());
                    await CopyCapturedRowsAsync(context);
                    before = await RowsAsync(context);
                }
                await context.Database.MigrateAsync();
                Assert.Equal(migrations, await context.Database.GetAppliedMigrationsAsync());
                Assert.Empty(await context.Database.GetPendingMigrationsAsync());
                Assert.Empty(await context.Set<Entities.RuntimeModelExecutionEntity>().ToArrayAsync());
                Assert.Empty(await context.Set<Entities.RuntimeModelInputEntity>().ToArrayAsync());
                if (retained)
                {
                    Assert.Equal(before, await RowsAsync(context));
                    Assert.NotEmpty(await context.WorkflowExecutables.AsNoTracking().ToArrayAsync());
                    Assert.All(await context.WorkflowExecutables.AsNoTracking().ToArrayAsync(), row => Assert.False(row.IsModelMaterial));
                    foreach (var row in await context.WorkflowExecutionStates.AsNoTracking().ToArrayAsync())
                    {
                        var access = new Access(new(Decode(row.ScopeKey)));
                        var state = await new EfWorkflowExecutionStateStore(context, access, new Codec()).FindAsync(Decode(row.WorkflowExecutionId));
                        Assert.NotNull(state);
                        Assert.Null(state.ModelWorkOrigin);
                    }
                }
            }
            // Prove the migrated schema can run the real protected-input/model/receipt/outbox/marker writer.
            await RuntimeModelCheckpointProviderSmoke.AtomicAsync(fixture, (_, interceptors) => open(selected, interceptors), failMarker: false);
        }
        finally
        {
            await using var cleanup = open(selected, []);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task CopyCapturedRowsAsync(RuntimeDbContext target)
    {
        var input = Path.Combine(AppContext.BaseDirectory, "HostingBaseline", "runtime.sqlite.gz");
        Assert.Equal("3fcd6e38a968d05026a2c57a188f5d5c9d6fdbc313afd9ce19c82ad3e2b21416", Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(input))));
        var folder = Directory.CreateTempSubdirectory("elsa-model-migration-rows-");
        try
        {
            var path = Path.Combine(folder.FullName, "captured.sqlite");
            await using (var compressed = new GZipStream(File.OpenRead(input), CompressionMode.Decompress))
            await using (var output = File.Create(path)) await compressed.CopyToAsync(output);
            await using var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await source.OpenAsync(); await target.Database.OpenConnectionAsync();
            var sql = target.GetService<ISqlGenerationHelper>();
            await using var transaction = await target.Database.BeginTransactionAsync();
            foreach (var table in Tables)
            {
                await using var select = source.CreateCommand(); select.CommandText = "SELECT * FROM \"" + table + "\"";
                await using var reader = await select.ExecuteReaderAsync(); var rows = 0;
                var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
                Assert.DoesNotContain("IsModelMaterial", names);
                while (await reader.ReadAsync())
                {
                    await using var insert = target.Database.GetDbConnection().CreateCommand(); insert.Transaction = transaction.GetDbTransaction();
                    insert.CommandText = $"INSERT INTO {sql.DelimitIdentifier(table)} ({string.Join(',', names.Select(sql.DelimitIdentifier))}) VALUES ({string.Join(',', names.Select((_, index) => "@p" + index))})";
                    for (var index = 0; index < names.Length; index++)
                    { var parameter = insert.CreateParameter(); parameter.ParameterName = "@p" + index; parameter.Value = reader.GetValue(index); insert.Parameters.Add(parameter); }
                    await insert.ExecuteNonQueryAsync(); rows++;
                }
                Assert.True(rows > 0, "The frozen actual table must contain rows.");
            }
            await transaction.CommitAsync();
        }
        finally { folder.Delete(recursive: true); }
    }

    private static async Task<string> RowsAsync(RuntimeDbContext context)
    {
        await context.Database.OpenConnectionAsync(); var sql = context.GetService<ISqlGenerationHelper>(); var tables = new List<object>();
        foreach (var table in Tables)
        {
            await using var query = context.Database.GetDbConnection().CreateCommand(); query.CommandText = "SELECT * FROM " + sql.DelimitIdentifier(table);
            await using var reader = await query.ExecuteReaderAsync(); var rows = new List<string>();
            var columns = Enumerable.Range(0, reader.FieldCount).Where(i => reader.GetName(i) != "IsModelMaterial").ToArray();
            while (await reader.ReadAsync()) rows.Add(JsonSerializer.Serialize(columns.Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray()));
            rows.Sort(StringComparer.Ordinal); tables.Add(new { table, rows });
        }
        return JsonSerializer.Serialize(tables);
    }
    private static string Decode(string value) => Encoding.Unicode.GetString(Convert.FromBase64String(value));
    private sealed class Access(PersistenceScope scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(scope); }
    private sealed class Codec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public byte[] Decode(string purpose, string token) => throw new NotSupportedException();
    }
}
