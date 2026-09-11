using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

public sealed class SecretsSqliteDbContext(DbContextOptions<SecretsSqliteDbContext> options)
    : SecretsDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SecretRecord>(entity =>
        {
            entity.Property(record => record.Payload).HasColumnType("TEXT");
            entity.Property(record => record.ConcurrencyToken).HasColumnType("BLOB");
            // Sqlite cannot translate DateTimeOffset comparisons against TEXT. Store UTC ticks.
            entity.Property(record => record.MaxActiveVersionExpiresAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcTicks : (long?)null,
                    value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null)
                .HasColumnType("INTEGER");
        });
    }
}
