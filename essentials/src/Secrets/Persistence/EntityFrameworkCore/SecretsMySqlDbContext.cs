using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore;

/// <summary>
/// Secrets model bound to Oracle's MySQL EF Core provider. The provider package remains a host
/// dependency; this context only records the provider annotations consumed by that package.
/// </summary>
public sealed class SecretsMySqlDbContext(DbContextOptions<SecretsMySqlDbContext> options)
    : SecretsDbContext(options)
{
    private const string CharacterSetAnnotation = "MySQL:Charset";

    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    public const string CharacterSet = "utf8mb4";

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Model.SetAnnotation(CharacterSetAnnotation, CharacterSet);

        modelBuilder.Entity<SecretRecord>(entity =>
        {
            entity.Property(record => record.Payload).HasColumnType("json");
            entity.Property(record => record.ConcurrencyToken).HasColumnType("varbinary(16)");
            // Persist UTC ticks rather than relying on provider-specific DateTimeOffset precision.
            entity.Property(record => record.MaxActiveVersionExpiresAt)
                .HasConversion(
                    value => value.HasValue ? value.Value.UtcTicks : (long?)null,
                    value => value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null)
                .HasColumnType("bigint");
        });
        ApplyOrdinalCollation(modelBuilder, ExpectedProviderName);
    }
}
