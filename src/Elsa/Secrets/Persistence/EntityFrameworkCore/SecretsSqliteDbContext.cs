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
        });
    }
}
