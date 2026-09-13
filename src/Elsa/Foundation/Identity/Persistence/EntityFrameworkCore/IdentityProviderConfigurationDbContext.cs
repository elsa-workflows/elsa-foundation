using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Provider-neutral model for the two Identity provider-configuration storage units.</summary>
public abstract class IdentityProviderConfigurationDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<TenantProviderConfigurationEntity> TenantProviderConfigurations => Set<TenantProviderConfigurationEntity>();
    public DbSet<GlobalProviderConfigurationEntity> GlobalProviderConfigurations => Set<GlobalProviderConfigurationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderConfigurationEntity>().UseTpcMappingStrategy();
        modelBuilder.Entity<ProviderConfigurationEntity>().HasKey(record => record.Id);
        modelBuilder.ApplyConfiguration(new TenantProviderConfigurationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new GlobalProviderConfigurationEntityConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
