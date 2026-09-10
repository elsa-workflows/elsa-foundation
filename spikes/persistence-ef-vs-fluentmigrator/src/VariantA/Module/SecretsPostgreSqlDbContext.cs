using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.Spike.VariantA;

public sealed class SecretsPostgreSqlDbContext(DbContextOptions<SecretsPostgreSqlDbContext> options)
    : SecretsDbContext(options)
{
    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SecretRecord>(entity =>
        {
            entity.Property(x => x.Payload).HasColumnType("jsonb");
            // Npgsql maps byte[] + IsRowVersion to a bytea concurrency token. xmin is a further
            // specialization (UseXminAsConcurrencyToken) and is the same *pattern* as this override,
            // not a third context type. SqlServer would be a third derived context with rowversion.
            entity.Property(x => x.RowVersion).IsConcurrencyToken().HasColumnType("bytea");
        });
    }
}
