using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>Provider-neutral model for tenant-local Foundation Identity IAM repositories.</summary>
public abstract class IdentityIamDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<ApplicationEntity> Applications => Set<ApplicationEntity>();
    public DbSet<CredentialEntity> Credentials => Set<CredentialEntity>();

    protected abstract string ExpectedProviderNameValue { get; }

    /// <summary>Fails closed when a host binds a derived context to the wrong provider engine.</summary>
    public void EnsureProviderBinding() => EfProviderGuard.Ensure(this, ExpectedProviderNameValue);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ApplicationEntityConfiguration());
        modelBuilder.ApplyConfiguration(new CredentialEntityConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
