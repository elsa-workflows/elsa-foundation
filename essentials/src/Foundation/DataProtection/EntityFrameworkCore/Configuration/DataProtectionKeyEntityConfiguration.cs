using Elsa.Foundation.DataProtection.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.Configuration;

public sealed class DataProtectionKeyEntityConfiguration : IEntityTypeConfiguration<DataProtectionKeyEntity>
{
    /// <summary>A GUID's 32 hex digits.</summary>
    public const int IdLength = 32;

    public void Configure(EntityTypeBuilder<DataProtectionKeyEntity> builder)
    {
        builder.ToTable(DataProtectionKeysEfModule.TableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(IdLength).ValueGeneratedNever();
        builder.Property(row => row.Xml).IsRequired();
        builder.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
    }
}
