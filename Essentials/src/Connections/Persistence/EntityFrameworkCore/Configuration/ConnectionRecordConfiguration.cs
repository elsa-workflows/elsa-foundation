using Elsa.Connections.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Connections.Persistence.EntityFrameworkCore.Configuration;

internal sealed class ConnectionRecordConfiguration : IEntityTypeConfiguration<ConnectionRecord>, IEntityTypeConfiguration<ConnectionSecretReferenceRecord>
{
    public void Configure(EntityTypeBuilder<ConnectionRecord> builder)
    {
        builder.ToTable(ConnectionsEfModule.TableName);
        builder.HasKey(x => new { x.TenantId, x.NormalizedName });
        builder.Property(x => x.TenantId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.NameSearchKey).IsRequired();
        builder.Property(x => x.DisplayNameSearchKey).IsRequired();
        builder.Property(x => x.TypeLookupKey).IsRequired();
        builder.Property(x => x.SchemeLookupKey).IsRequired();
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
    }

    public void Configure(EntityTypeBuilder<ConnectionSecretReferenceRecord> builder)
    {
        builder.ToTable(ConnectionsEfModule.ReferenceTableName);
        builder.HasKey(x => new { x.TenantId, x.NormalizedName, x.SecretLookupHash });
        builder.Property(x => x.TenantId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SecretLookupHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.SecretLookupHash });
        builder.HasOne<ConnectionRecord>().WithMany().HasForeignKey(x => new { x.TenantId, x.NormalizedName }).OnDelete(DeleteBehavior.Cascade);
    }
}
