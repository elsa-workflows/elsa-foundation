using Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;
using Microsoft.Data.SqlClient;
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

    /// <summary>Azure SQL creates its databases with read-committed snapshot isolation on, and it changes what a plain read sees while a lock is held.</summary>
    [SkippableFact]
    public Task Every_module_keeps_its_own_finalization_record_on_sql_server_with_read_committed_snapshot() =>
        RunAsync("SqlServer", async connection =>
        {
            var database = new SqlConnectionStringBuilder(connection);
            await using var admin = new SqlConnection(new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" }.ConnectionString);
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{database.InitialCatalog}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE";
            await command.ExecuteNonQueryAsync();
        });

    private static Task RunAsync(string provider, Func<string, Task>? configure = null) => ProviderDatabase.RunAsync(provider, async connection =>
    {
        if (configure is not null)
            await configure(connection);
        await ModuleSchemaFinalizationScenario.AssertRecordTablesAsync(provider, connection, expected: false);
        await ModuleContextCatalog.InstallAllAsync(provider, connection);
        await ModuleSchemaFinalizationScenario.AssertRecordTablesAsync(provider, connection, expected: true);
        await ModuleSchemaFinalizationScenario.RunStartupRaceAsync(provider, connection);
        await ModuleSchemaFinalizationScenario.RunStartupStressAsync(provider, connection);
        await ModuleSchemaFinalizationScenario.RunAsync(provider, connection);
    });
}
