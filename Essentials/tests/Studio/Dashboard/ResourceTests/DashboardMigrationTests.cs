using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Elsa.Studio.Dashboard.ResourceTests;

public sealed class DashboardMigrationTests
{
    [Theory]
    [InlineData("Sqlite")]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    public void Every_declared_provider_has_its_own_migration_snapshot_and_executable_script(string provider)
    {
        using DashboardResourceDbContext context = provider switch {
            "Sqlite" => new DashboardSqliteMigrationFactory().CreateDbContext([]),
            "SqlServer" => new DashboardSqlServerMigrationFactory().CreateDbContext([]),
            "PostgreSql" => new DashboardPostgreSqlMigrationFactory().CreateDbContext([]),
            "MySql" => new DashboardMySqlMigrationFactory().CreateDbContext([]),
            _ => throw new InvalidOperationException() };
        Assert.Single(context.Database.GetMigrations());
        Assert.NotNull(context.GetService<IMigrationsAssembly>().ModelSnapshot);
        var script = context.GetService<IMigrator>().GenerateScript();
        Assert.Contains("elsa_studio_dashboard_resources", script, StringComparison.Ordinal);
        Assert.Contains("elsa_studio_dashboard_operations", script, StringComparison.Ordinal);
        Assert.Contains("elsa_studio_dashboard_scopes", script, StringComparison.Ordinal);
        Assert.False(context.Database.HasPendingModelChanges());
        foreach (var type in new[] { typeof(DashboardResourceRow), typeof(DashboardResourceScopeRow), typeof(DashboardResourceOperationRow) })
        {
            var entity = context.Model.FindEntityType(type)!;
            var stamp = entity.FindProperty("SchemaVersion")!;
            Assert.False(stamp.IsNullable); Assert.Equal(32, stamp.GetMaxLength());
            var index = entity.FindIndex(stamp)!;
            Assert.False(index.IsUnique); Assert.Single(index.Properties);
        }
    }
}
