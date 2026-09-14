using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

/// <summary>
/// Secrets model bound to Oracle's MySQL EF Core provider. The provider package remains a host
/// dependency; this context only records the provider annotations consumed by that package.
/// The selected binary collation targets Oracle MySQL 8.0+ and is not a MariaDB contract.
/// </summary>
public sealed class SecretsMySqlDbContext(DbContextOptions<SecretsMySqlDbContext> options)
    : SecretsDbContext(options)
{
    private const string CharacterSetAnnotation = "MySQL:Charset";
    private const string CollationAnnotation = "MySQL:Collation";

    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    public const string CharacterSet = "utf8mb4";
    public const string Collation = "utf8mb4_0900_bin";

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.SetAnnotation(CharacterSetAnnotation, CharacterSet);
        modelBuilder.UseCollation(Collation);

        modelBuilder.Entity<SecretRecord>(entity =>
        {
            entity.Metadata.SetAnnotation(CollationAnnotation, Collation);
            foreach (var propertyName in new[]
                     {
                         nameof(SecretRecord.TenantId),
                         nameof(SecretRecord.NormalizedName),
                         nameof(SecretRecord.NameSearchKey),
                         nameof(SecretRecord.DisplayNameSearchKey),
                         nameof(SecretRecord.TypeNameLookupKey),
                         nameof(SecretRecord.StoreNameLookupKey),
                         nameof(SecretRecord.ScopeLookupKey)
                     })
                entity.Property(propertyName).Metadata.SetAnnotation(CollationAnnotation, Collation);

            entity.Property(record => record.Payload).HasColumnType("json");
            entity.Property(record => record.ConcurrencyToken).HasColumnType("varbinary(16)");
            // Persist UTC ticks rather than relying on provider-specific DateTimeOffset precision.
            entity.Property(record => record.MaxActiveVersionExpiresAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcTicks : (long?)null,
                    value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null)
                .HasColumnType("bigint");
        });
    }
}
