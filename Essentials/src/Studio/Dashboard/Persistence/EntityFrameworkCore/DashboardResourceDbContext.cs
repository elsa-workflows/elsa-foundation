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
        modelBuilder.Entity<DashboardResourceRow>(entity => {
            entity.ToTable("elsa_studio_dashboard_resources"); entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(64);
            entity.Property(row => row.ScopeId).HasMaxLength(64).IsRequired();
            entity.Property(row => row.OwnerKey).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Kind).HasMaxLength(16).IsRequired();
            entity.Property(row => row.Revision).HasMaxLength(64).IsConcurrencyToken();
            entity.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
            entity.Property(row => row.DocumentJson).IsRequired();
            entity.HasIndex(row => new { row.ScopeId, row.Kind, row.Deleted });
            entity.HasIndex(row => new { row.ScopeId, row.OwnerKey, row.Kind, row.Deleted });
        });
        modelBuilder.Entity<DashboardResourceScopeRow>(entity => {
            entity.ToTable("elsa_studio_dashboard_scopes"); entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(64);
            entity.Property(row => row.Revision).HasMaxLength(64).IsConcurrencyToken();
        });
        modelBuilder.Entity<DashboardResourceOperationRow>(entity => {
            entity.ToTable("elsa_studio_dashboard_operations"); entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(64);
            entity.Property(row => row.ResourceKey).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Fingerprint).HasMaxLength(64).IsRequired();
        });
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
}

public sealed class DashboardResourceOperationRow
{
    public string Id { get; set; } = "";
    public string ResourceKey { get; set; } = "";
    public string Fingerprint { get; set; } = "";
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
