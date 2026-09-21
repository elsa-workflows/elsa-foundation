using Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests;

/// <summary>
/// Installs every first-party EF module into one fresh database per provider, then validates each module's
/// history.
/// </summary>
public sealed class NativeModuleMigrationTests
{
    [SkippableFact]
    public Task Every_module_installs_on_postgresql() =>
        ProviderDatabase.RunAsync("PostgreSql", connection => ModuleContextCatalog.InstallAllAsync("PostgreSql", connection));

    [SkippableFact]
    public Task Every_module_installs_on_sql_server() =>
        ProviderDatabase.RunAsync("SqlServer", connection => ModuleContextCatalog.InstallAllAsync("SqlServer", connection));

    [SkippableFact]
    public Task Every_module_installs_on_mysql() =>
        ProviderDatabase.RunAsync("MySql", connection => ModuleContextCatalog.InstallAllAsync("MySql", connection));
}
