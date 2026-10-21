using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Configuration;

public sealed class SecretRecordConfiguration : IEntityTypeConfiguration<SecretRecord>
{
    /// <summary>The longest secret id a deleted row can carry as its tombstone.</summary>
    public const int TombstoneMaximumLength = 64;

    /// <summary>
    /// Whether a deleted secret's <paramref name="id"/> can be its row's tombstone: not blank, which would read as the row
    /// holding the name, and within the column.
    /// </summary>
    internal static bool IsTombstone(string? id) => !string.IsNullOrWhiteSpace(id) && id.Length <= TombstoneMaximumLength;

    public void Configure(EntityTypeBuilder<SecretRecord> builder)
    {
        builder.ToTable(SecretsEfModule.TableName);
        // A soft delete frees the name. The secret holding a name has an empty tombstone and each deleted one carries
        // its own id, so the key itself admits one live row per tenant and name. It is a plain key on every provider:
        // no filtered index, which MySQL lacks, and no reliance on how a provider compares nulls in a unique index.
        builder.HasKey(record => new { record.TenantId, record.NormalizedName, record.Tombstone });
        builder.Property(record => record.TenantId).HasMaxLength(256).IsRequired();
        builder.Property(record => record.NormalizedName).HasMaxLength(SecretNameConstraints.MaximumLength).IsRequired();
        builder.Property(record => record.Tombstone).HasMaxLength(TombstoneMaximumLength).IsRequired();
        builder.Property(record => record.NameSearchKey).IsRequired();
        builder.Property(record => record.DisplayNameSearchKey).IsRequired();
        builder.Property(record => record.TypeNameLookupKey).IsRequired();
        builder.Property(record => record.StoreNameLookupKey).IsRequired();
        builder.Property(record => record.ScopeLookupKey);
        builder.Property(record => record.Status).HasMaxLength(32).IsRequired();
        builder.Property(record => record.HasNonExpiringActiveVersion).IsRequired();
        builder.Property(record => record.Payload).IsRequired();
        builder.Property(record => record.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(record => record.ConcurrencyToken).IsRequired().IsConcurrencyToken();
        builder.HasIndex(record => new { record.TenantId, record.Status, record.NormalizedName })
            .HasDatabaseName(SecretsEfModule.FilteredListIndex);
    }
}
