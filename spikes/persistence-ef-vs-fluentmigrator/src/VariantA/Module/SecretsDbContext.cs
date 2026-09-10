using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.Spike.VariantA;

/// <summary>
/// Provider-neutral entity configuration. Derived contexts own provider-specific column types
/// and each has its own ModelSnapshot — one DbContext type cannot hold three provider migration sets.
/// </summary>
public abstract class SecretsDbContext : DbContext
{
    protected SecretsDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<SecretRecord> Secrets => Set<SecretRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SecretRecordConfiguration());
        ConfigureProvider(modelBuilder);
    }

    /// <summary>Provider-specific column types (json vs jsonb, rowversion vs xmin/bytea).</summary>
    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampRowVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampRowVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampRowVersions()
    {
        // SQLite/Npgsql have no SQL Server rowversion generator. IsRowVersion() inserts NULL.
        foreach (var entry in ChangeTracker.Entries<SecretRecord>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Property(x => x.RowVersion).CurrentValue = Guid.NewGuid().ToByteArray();
        }
    }
}
