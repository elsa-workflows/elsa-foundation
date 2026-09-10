using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

public sealed class SecretsPostgreSqlDbContext(DbContextOptions<SecretsPostgreSqlDbContext> options)
    : SecretsDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SecretRecord>(entity =>
        {
            entity.Property(record => record.Payload).HasColumnType("jsonb");
            entity.Property(record => record.ConcurrencyToken).HasColumnType("bytea");
        });
    }
}
