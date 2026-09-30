using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Spec 181, FR-002: every EF module keeps the finalization record in its own database, beside its own
/// migrations-history table, created by its own migrations, whichever membership provider a host composes.
/// </summary>
public sealed class ModuleSchemaFinalizationTests : IDisposable
{
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-ef-finalization-{Guid.NewGuid():N}.db");

    public static TheoryData<string> Providers() => [.. ModuleContextCatalog.Providers];

    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_module_context_maps_the_finalization_tables_beside_its_history_table(string provider)
    {
        var contexts = ModuleContextCatalog.Contexts(provider);
        Assert.Equal(ModuleContextCatalog.DeclaredHistoryTables().Count, contexts.Count);
        var tables = new List<string>();
        foreach (var type in contexts)
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
            var module = ModuleContextCatalog.HistoryTable(type)[EfMigrationsHistory.TablePrefix.Length..];
            var mapped = context.Model.GetEntityTypes()
                .Select(entity => entity.GetTableName()!)
                .Where(table => table.StartsWith("__Elsa", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal([EfSchemaFinalization.DatabaseIdentityTableName(module), EfSchemaFinalization.RecordTableName(module)], mapped);
            tables.AddRange(mapped);
        }

        Assert.Equal(tables.Count, tables.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Every_sqlite_module_keeps_its_own_finalization_record_in_one_shared_database()
    {
        var connection = $"Data Source={databasePath};Pooling=False";
        await ModuleSchemaFinalizationScenario.AssertRecordTablesAsync("Sqlite", connection, expected: false);
        await ModuleContextCatalog.InstallAllAsync("Sqlite", connection);
        await ModuleSchemaFinalizationScenario.AssertRecordTablesAsync("Sqlite", connection, expected: true);

        await ModuleSchemaFinalizationScenario.RunStartupRaceAsync("Sqlite", connection);
        await ModuleSchemaFinalizationScenario.RunStartupStressAsync("Sqlite", connection);
        await ModuleSchemaFinalizationScenario.RunAsync("Sqlite", connection);
    }

    public void Dispose()
    {
        if (File.Exists(databasePath))
            File.Delete(databasePath);
    }
}
