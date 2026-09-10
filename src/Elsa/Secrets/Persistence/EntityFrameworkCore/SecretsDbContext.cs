using System.Linq;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

/// <summary>
/// Shared Secrets model. Derived contexts own provider column types and each has its own
/// ModelSnapshot — one DbContext type cannot hold three provider migration sets.
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

    /// <summary>Provider-specific column types (json vs jsonb, blob vs varbinary vs bytea).</summary>
    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampConcurrencyTokens();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampConcurrencyTokens();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampConcurrencyTokens()
    {
        // IsRowVersion() on Sqlite inserts NULL. Stamp an explicit token on every provider.
        foreach (var entry in ChangeTracker.Entries<SecretRecord>()
                     .Where(candidate => candidate.State is EntityState.Added or EntityState.Modified))
        {
            entry.Property(record => record.ConcurrencyToken).CurrentValue = Guid.NewGuid().ToByteArray();
        }
    }
}
