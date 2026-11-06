using System.Data;
using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

public sealed class RuntimePreparationRetentionMigrationTests
{
    private const string Table = "elsa_runtime_workflow_preparation_retention";
    private const string Refusal = "PreparationRetentionDowngradeNotSupportedOperation";

    public static TheoryData<string, string?> Compositions() => new()
    {
        { "Sqlite", null }, { "MySql", null }, { "PostgreSql", null }, { "SqlServer", null },
        { "PostgreSql", "elsa_alt" }, { "SqlServer", "elsa_alt" }
    };

    [Theory]
    [MemberData(nameof(Compositions))]
    public void Forward_construction_and_scripts_keep_the_original_migration_and_schema(string provider, string? schema)
    {
        using var context = Create(provider, schema);
        var assembly = context.GetService<IMigrationsAssembly>();
        if (schema is not null)
            Assert.IsType<EfSchemaMigrationsAssembly>(assembly);
        var (current, previous) = Boundary(context);
        var migration = assembly.CreateMigration(assembly.Migrations[current], context.Database.ProviderName!);
        Assert.Equal(assembly.Migrations[current].AsType(), migration.GetType());
        Assert.Equal(context.Database.ProviderName, migration.ActiveProvider);
        Assert.Equal(3, migration.UpOperations.Count);
        var table = Assert.IsType<CreateTableOperation>(migration.UpOperations[0]);
        Assert.Equal(Table, table.Name);
        Assert.Equal(schema, table.Schema);
        Assert.All(migration.UpOperations.Skip(1), operation =>
        {
            var index = Assert.IsType<CreateIndexOperation>(operation);
            Assert.Equal(Table, index.Table);
            Assert.Equal(schema, index.Schema);
        });
        var marker = Assert.Single(migration.DownOperations);
        Assert.Equal(Refusal, marker.GetType().Name);
        Assert.False(marker.GetType().IsVisible);
        Assert.False(marker is DropTableOperation or SqlOperation);

        var migrator = context.GetService<IMigrator>();
        var qualified = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(Table, schema);
        Assert.Contains(qualified, migrator.GenerateScript(previous, current), StringComparison.Ordinal);
        Assert.Contains(qualified, migrator.GenerateScript(), StringComparison.Ordinal);
        // SQLite's existing history implementation does not offer idempotent scripting.
        if (provider != "Sqlite")
        {
            Assert.Contains(qualified, migrator.GenerateScript(previous, current, MigrationsSqlGenerationOptions.Idempotent), StringComparison.Ordinal);
            Assert.Contains(qualified, migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent), StringComparison.Ordinal);
        }
        else
        {
            var existing = Assert.Throws<NotSupportedException>(() => context.GetService<IHistoryRepository>().GetBeginIfNotExistsScript(current));
            var unsupported = Assert.Throws<NotSupportedException>(() => migrator.GenerateScript(previous, current, MigrationsSqlGenerationOptions.Idempotent));
            Assert.Equal(existing.Message, unsupported.Message);
            Assert.DoesNotContain(Refusal, unsupported.Message, StringComparison.Ordinal);
        }
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Theory]
    [MemberData(nameof(Compositions))]
    public void Downgrade_generation_refuses_at_the_retention_boundary_but_preserves_historical_ranges(string provider, string? schema)
    {
        using var context = Create(provider, schema);
        var assembly = context.GetService<IMigrationsAssembly>();
        var (current, previous) = Boundary(context);
        var migration = assembly.CreateMigration(assembly.Migrations[current], context.Database.ProviderName!);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var migrator = context.GetService<IMigrator>();
        foreach (var options in new[] { MigrationsSqlGenerationOptions.Default, MigrationsSqlGenerationOptions.Idempotent })
        {
            AssertMarkerRefusal(() => generator.Generate(migration.DownOperations, options: options), generator);
            if (provider == "Sqlite" && options == MigrationsSqlGenerationOptions.Idempotent)
            {
                var existing = Assert.Throws<NotSupportedException>(() => context.GetService<IHistoryRepository>().GetBeginIfExistsScript(current));
                var unsupported = Assert.Throws<NotSupportedException>(() => migrator.GenerateScript(current, previous, options));
                Assert.Equal(existing.Message, unsupported.Message);
                Assert.DoesNotContain(Refusal, unsupported.Message, StringComparison.Ordinal);
            }
            else
                AssertMarkerRefusal(() => migrator.GenerateScript(current, previous, options), generator);
        }

        var ordered = assembly.Migrations.Keys.ToArray();
        var predecessor = ordered[Array.IndexOf(ordered, previous) - 1];
        // This range never crosses the new boundary; an unrelated failure is not a refusal pass.
        Assert.NotEmpty(migrator.GenerateScript(previous, predecessor));
        if (provider != "Sqlite")
            Assert.NotEmpty(migrator.GenerateScript(previous, predecessor, MigrationsSqlGenerationOptions.Idempotent));
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task Actual_sqlite_downgrade_preserves_synthetic_physical_rows_and_the_retention_history_entry()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-retention-downgrade-{Guid.NewGuid():N}.db");
        var connection = $"Data Source={path};Pooling=False";
        var type = RuntimeContext("Sqlite");
        var observer = new CommandObserver();
        string[] beforeRows;
        string[] beforeHistory;
        try
        {
            await using (var context = ModuleContextCatalog.Create(type, connection, configure: options => options.AddInterceptors(observer)))
            {
                await context.Database.MigrateAsync();
                // Physical synthetic rows test DDL/history preservation, not domain codec or owner acceptance.
                foreach (var (key, state) in new[] { ('a', "Retained"), ('b', "Transferred") })
                {
                    await context.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO elsa_runtime_workflow_preparation_retention
                        (Id,ScopeKey,ScopeKeyHash,OwnerAddress,OperationId,PreparationOrderKey,IncarnationId,Revision,State,SchemaVersion,ContentJson)
                        VALUES ({new string(key, 64)},{"synthetic-scope"},{new string('c', 64)},{"synthetic-owner"},{state},{new string(key, 64)},{new string('d', 64)},{1L},{state},{"1.2"},{"{}"})
                        """);
                }
                beforeRows = await ReadPhysicalRowsAsync(context);
                Assert.Equal(2, beforeRows.Length);
                beforeHistory = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
                var (current, previous) = Boundary(context);
                Assert.Contains(current, beforeHistory);
                observer.Commands.Clear();
                observer.Capture = true;
                var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.GetService<IMigrator>().MigrateAsync(previous));
                observer.Capture = false;
                Assert.Contains(Refusal, failure.Message, StringComparison.Ordinal);
                Assert.Contains(context.GetService<IMigrationsSqlGenerator>().GetType().Name, failure.Message, StringComparison.Ordinal);
                Assert.NotEmpty(observer.Commands);
                // Reads/locks/transaction infrastructure may occur; destructive retention commands may not.
                Assert.DoesNotContain(observer.Commands, sql => sql.Contains("DROP TABLE", StringComparison.OrdinalIgnoreCase) && sql.Contains(Table, StringComparison.Ordinal));
                var historyTable = ModuleContextCatalog.HistoryTable(type);
                Assert.DoesNotContain(observer.Commands, sql => sql.Contains("DELETE", StringComparison.OrdinalIgnoreCase) && sql.Contains(historyTable, StringComparison.Ordinal));
                Assert.Equal(beforeRows, await ReadPhysicalRowsAsync(context));
                Assert.Equal(beforeHistory, (await context.Database.GetAppliedMigrationsAsync()).ToArray());
            }

            await using var reopened = ModuleContextCatalog.Create(type, connection);
            Assert.Equal(beforeRows, await ReadPhysicalRowsAsync(reopened));
            Assert.Equal(beforeHistory, (await reopened.Database.GetAppliedMigrationsAsync()).ToArray());
        }
        finally
        {
            foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
                File.Delete(path + suffix);
        }
    }

    private static void AssertMarkerRefusal(Action action, IMigrationsSqlGenerator generator)
    {
        var failure = Assert.Throws<InvalidOperationException>(action);
        Assert.Contains(Refusal, failure.Message, StringComparison.Ordinal);
        Assert.Contains(generator.GetType().Name, failure.Message, StringComparison.Ordinal);
    }

    private static Type RuntimeContext(string provider) =>
        Assert.Single(ModuleContextCatalog.Contexts(provider), type => typeof(RuntimeDbContext).IsAssignableFrom(type));

    private static DbContext Create(string provider, string? schema) =>
        ModuleContextCatalog.Create(RuntimeContext(provider), ModuleContextCatalog.PlaceholderConnection(provider), schema: schema);

    private static (string Current, string Previous) Boundary(DbContext context)
    {
        var ordered = context.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();
        var current = Assert.Single(ordered, id => id.EndsWith("_PreparationRetention", StringComparison.Ordinal));
        Assert.Equal(current, ordered[^1]);
        return (current, ordered[Array.IndexOf(ordered, current) - 1]);
    }

    private static async Task<string[]> ReadPhysicalRowsAsync(DbContext context)
    {
        await context.Database.OpenConnectionAsync();
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT Id,ScopeKey,ScopeKeyHash,OwnerAddress,OperationId,PreparationOrderKey,IncarnationId,Revision,State,SchemaVersion,ContentJson FROM elsa_runtime_workflow_preparation_retention ORDER BY Id";
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows.Add(JsonSerializer.Serialize(values));
            }
            return rows.ToArray();
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private sealed class CommandObserver : DbCommandInterceptor
    {
        public bool Capture { get; set; }
        public List<string> Commands { get; } = [];

        private void Record(DbCommand command)
        {
            if (Capture)
                Commands.Add(command.CommandText);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        { Record(command); return result; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Record(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Record(command); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Record(command); return ValueTask.FromResult(result); }
        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        { Record(command); return result; }
        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        { Record(command); return ValueTask.FromResult(result); }
    }
}
