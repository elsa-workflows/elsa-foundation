using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;

public abstract class DashboardResourceDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<DashboardResourceRow> Resources => Set<DashboardResourceRow>();
    public DbSet<DashboardResourceScopeRow> Scopes => Set<DashboardResourceScopeRow>();
    public DbSet<DashboardResourceOperationRow> Operations => Set<DashboardResourceOperationRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.AddDashboardResourcesModel(Database.ProviderName!);
        modelBuilder.MapSchemaFinalization(DashboardResourcesEfModule.HistoryModuleName);
        ConfigureProvider(modelBuilder);
        modelBuilder.IndexSchemaVersionStamps();
    }

    protected virtual void ConfigureProvider(ModelBuilder modelBuilder) { }
}

public sealed class DashboardResourceRow
{
    public string Id { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public string OwnerKey { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Revision { get; set; } = "";
    public string SchemaVersion { get; set; } = DashboardResourcesEfModule.SchemaVersion;
    public string DocumentJson { get; set; } = "";
    public bool Deleted { get; set; }
}

public sealed class DashboardResourceScopeRow
{
    public string Id { get; set; } = "";
    public string Revision { get; set; } = "";
    public string SchemaVersion { get; set; } = DashboardResourcesEfModule.SchemaVersion;
}

public sealed class DashboardResourceOperationRow
{
    public string Id { get; set; } = "";
    public string ResourceKey { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string SchemaVersion { get; set; } = DashboardResourcesEfModule.SchemaVersion;
}

public sealed class DashboardResourcesSqliteDbContext(DbContextOptions<DashboardResourcesSqliteDbContext> options) : DashboardResourceDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.Sqlite;
}

public sealed class DashboardResourcesSqlServerDbContext(DbContextOptions<DashboardResourcesSqlServerDbContext> options) : DashboardResourceDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.SqlServer;
}

public sealed class DashboardResourcesPostgreSqlDbContext(DbContextOptions<DashboardResourcesPostgreSqlDbContext> options) : DashboardResourceDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.PostgreSql;
}

public sealed class DashboardResourcesMySqlDbContext(DbContextOptions<DashboardResourcesMySqlDbContext> options) : DashboardResourceDbContext(options)
{
    public const string ExpectedProviderName = EfProviderNames.MySql;
}
