using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.Spike.VariantB;

/// <summary>
/// One context type is enough when FluentMigrator owns schema history. Mapping can still
/// branch on the live provider so jsonb vs TEXT stay honest for the drift check.
/// </summary>
public sealed class SecretsDbContext(DbContextOptions<SecretsDbContext> options) : DbContext(options)
{
    public DbSet<SecretRecord> Secrets => Set<SecretRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SecretRecordConfiguration());

        if (Database.ProviderName == ProviderGuard.NpgsqlProviderName)
        {
            modelBuilder.Entity<SecretRecord>().Property(x => x.Payload).HasColumnType("jsonb");
        }
        else if (Database.ProviderName == ProviderGuard.SqliteProviderName)
        {
            modelBuilder.Entity<SecretRecord>().Property(x => x.Payload).HasColumnType("TEXT");
        }
    }

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
        foreach (var entry in ChangeTracker.Entries<SecretRecord>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Property(x => x.RowVersion).CurrentValue = Guid.NewGuid().ToByteArray();
        }
    }
}
