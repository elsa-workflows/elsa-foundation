using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Configuration;

public sealed class SecretRecordConfiguration : IEntityTypeConfiguration<SecretRecord>
{
    public void Configure(EntityTypeBuilder<SecretRecord> builder)
    {
        builder.ToTable(SecretsEfModule.TableName);
        builder.HasKey(record => new { record.TenantId, record.NormalizedName });
        builder.Property(record => record.TenantId).HasMaxLength(256).IsRequired();
        builder.Property(record => record.NormalizedName).HasMaxLength(SecretNameConstraints.MaximumLength).IsRequired();
        builder.Property(record => record.NameSearchKey).IsRequired();
        builder.Property(record => record.DisplayNameSearchKey).IsRequired();
        builder.Property(record => record.TypeNameLookupKey).IsRequired();
        builder.Property(record => record.StoreNameLookupKey).IsRequired();
        builder.Property(record => record.ScopeLookupKey);
        builder.Property(record => record.Status).HasMaxLength(32).IsRequired();
        builder.Property(record => record.HasNonExpiringActiveVersion).IsRequired();
        builder.Property(record => record.Payload).IsRequired();
        builder.Property(record => record.ConcurrencyToken).IsRequired().IsConcurrencyToken();
        builder.HasIndex(record => new { record.TenantId, record.Status, record.NormalizedName })
            .HasDatabaseName(SecretsEfModule.FilteredListIndex);
    }
}
