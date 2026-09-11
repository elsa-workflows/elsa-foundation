using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

public sealed class SecretsSqlServerDbContext(DbContextOptions<SecretsSqlServerDbContext> options)
    : SecretsDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SecretRecord>(entity =>
        {
            // Default SQL Server collations are often CI_AS. Tenant and name identity is ordinal.
            entity.Property(record => record.TenantId).UseCollation("Latin1_General_BIN2");
            entity.Property(record => record.NormalizedName).UseCollation("Latin1_General_BIN2");
            // Lookup projections must compare ordinally rather than inheriting the host database's
            // default collation.
            entity.Property(record => record.TypeNameLookupKey).UseCollation("Latin1_General_BIN2");
            entity.Property(record => record.StoreNameLookupKey).UseCollation("Latin1_General_BIN2");
            entity.Property(record => record.ScopeLookupKey).UseCollation("Latin1_General_BIN2");
            entity.Property(record => record.Payload).HasColumnType("nvarchar(max)");
            entity.Property(record => record.ConcurrencyToken).HasColumnType("varbinary(16)");
        });
    }
}
