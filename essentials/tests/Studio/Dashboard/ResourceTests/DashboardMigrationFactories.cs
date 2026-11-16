using Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Elsa.Studio.Dashboard.ResourceTests;

public sealed class DashboardSqliteMigrationFactory : IDesignTimeDbContextFactory<DashboardResourcesSqliteDbContext>
{
    public DashboardResourcesSqliteDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<DashboardResourcesSqliteDbContext>().UseSqlite("Data Source=dashboard-migrations.db").Options);
}
public sealed class DashboardSqlServerMigrationFactory : IDesignTimeDbContextFactory<DashboardResourcesSqlServerDbContext>
{
    public DashboardResourcesSqlServerDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<DashboardResourcesSqlServerDbContext>().UseSqlServer("Server=localhost;Database=dashboard-migrations;Integrated Security=true;TrustServerCertificate=true").Options);
}
public sealed class DashboardPostgreSqlMigrationFactory : IDesignTimeDbContextFactory<DashboardResourcesPostgreSqlDbContext>
{
    public DashboardResourcesPostgreSqlDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<DashboardResourcesPostgreSqlDbContext>().UseNpgsql("Host=localhost;Database=dashboard-migrations;Username=unused;Password=unused").Options);
}
public sealed class DashboardMySqlMigrationFactory : IDesignTimeDbContextFactory<DashboardResourcesMySqlDbContext>
{
    public DashboardResourcesMySqlDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<DashboardResourcesMySqlDbContext>().UseMySQL("Server=localhost;Database=dashboard-migrations;User=unused;Password=unused").Options);
}
