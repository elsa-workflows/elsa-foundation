using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Provider-neutral model for the two Identity provider-configuration storage units.</summary>
public abstract class IdentityProviderConfigurationDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<TenantProviderConfigurationEntity> TenantProviderConfigurations => Set<TenantProviderConfigurationEntity>();
    public DbSet<GlobalProviderConfigurationEntity> GlobalProviderConfigurations => Set<GlobalProviderConfigurationEntity>();

    protected abstract string ExpectedProviderNameValue { get; }

    /// <summary>Fails closed when a host binds a derived context to the wrong provider engine.</summary>
    public void EnsureProviderBinding() => EfProviderGuard.Ensure(this, ExpectedProviderNameValue);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The host's optional schema; nothing changes when none is configured.
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.ApplyConfiguration(new TenantProviderConfigurationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new GlobalProviderConfigurationEntityConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
