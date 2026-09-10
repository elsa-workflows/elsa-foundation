using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Persistence.Spike.VariantA;

public sealed class SecretRecordConfiguration : IEntityTypeConfiguration<SecretRecord>
{
    public void Configure(EntityTypeBuilder<SecretRecord> builder)
    {
        builder.ToTable(SchemaNames.Table);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasDatabaseName(SchemaNames.UniqueIndex);
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.RowVersion).IsRequired();
    }
}
