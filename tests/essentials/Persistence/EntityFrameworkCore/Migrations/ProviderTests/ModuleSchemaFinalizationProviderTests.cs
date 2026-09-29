using Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

/// <summary>
/// The finalization record through every module context on each server engine, against the tables every module's
/// migrations created in one fresh database: compare-and-set and the unique identity row depend on what the engine
/// reports for a lost update and a duplicate key, which SQLite cannot speak for.
/// </summary>
public sealed class ModuleSchemaFinalizationProviderTests
{
    [SkippableFact]
    public Task Every_module_keeps_its_own_finalization_record_on_postgresql() => RunAsync("PostgreSql");

    [SkippableFact]
    public Task Every_module_keeps_its_own_finalization_record_on_sql_server() => RunAsync("SqlServer");

    [SkippableFact]
    public Task Every_module_keeps_its_own_finalization_record_on_mysql() => RunAsync("MySql");

    private static Task RunAsync(string provider) => ProviderDatabase.RunAsync(provider, async connection =>
    {
        await ModuleSchemaFinalizationScenario.AssertRecordTablesAsync(provider, connection, expected: false);
        await ModuleContextCatalog.InstallAllAsync(provider, connection);
        await ModuleSchemaFinalizationScenario.AssertRecordTablesAsync(provider, connection, expected: true);
        await ModuleSchemaFinalizationScenario.RunAsync(provider, connection);
    });
}
