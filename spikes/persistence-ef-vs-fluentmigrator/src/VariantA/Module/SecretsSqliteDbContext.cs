using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.Spike.VariantA;

public sealed class SecretsSqliteDbContext(DbContextOptions<SecretsSqliteDbContext> options) : SecretsDbContext(options)
{
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SecretRecord>(entity =>
        {
            entity.Property(x => x.Payload).HasColumnType("TEXT");
            entity.Property(x => x.RowVersion).IsConcurrencyToken().HasColumnType("BLOB");
        });
    }
}
