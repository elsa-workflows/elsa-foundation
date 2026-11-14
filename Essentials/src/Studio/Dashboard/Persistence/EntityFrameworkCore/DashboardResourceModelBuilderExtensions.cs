using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore;

/// <summary>Installs Dashboard definition mappings in an ordinary consumer-owned context.</summary>
public static class DashboardResourceModelBuilderExtensions
{
    /// <summary>
    /// Adds the resource, operation-key and quota-fence tables without changing unrelated entities,
    /// their schema, comparison or payload conventions. The consumer owns its combined migrations.
    /// </summary>
    public static ModelBuilder AddDashboardResourcesModel(this ModelBuilder modelBuilder, string providerName)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        if (providerName is not (EfProviderNames.Sqlite or EfProviderNames.SqlServer or EfProviderNames.PostgreSql or EfProviderNames.MySql))
            throw new ArgumentOutOfRangeException(nameof(providerName), "Dashboard resources require a supported relational provider.");
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
            entity.HasIndex(row => row.SchemaVersion);
        });
        modelBuilder.Entity<DashboardResourceScopeRow>(entity => {
            entity.ToTable("elsa_studio_dashboard_scopes"); entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(64);
            entity.Property(row => row.Revision).HasMaxLength(64).IsConcurrencyToken();
            entity.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
            entity.HasIndex(row => row.SchemaVersion);
        });
        modelBuilder.Entity<DashboardResourceOperationRow>(entity => {
            entity.ToTable("elsa_studio_dashboard_operations"); entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasMaxLength(64);
            entity.Property(row => row.ResourceKey).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Fingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
            entity.HasIndex(row => row.SchemaVersion);
        });
        return modelBuilder;
    }
}
