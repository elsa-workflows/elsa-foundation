using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal sealed class RuntimeKernelEventPublicationEntityConfiguration : IEntityTypeConfiguration<RuntimeKernelEventPublicationEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeKernelEventPublicationEntity> builder)
    {
        builder.ToTable(RuntimeKernelEventPublicationEfModule.TableName);
        builder.HasKey(row => new { row.ScopeHash, row.SlotHash, row.Revision });
        builder.Property(row => row.ScopeHash).HasMaxLength(64);
        builder.Property(row => row.TenantHash).HasMaxLength(64).IsRequired();
        builder.Property(row => row.SlotHash).HasMaxLength(64);
        builder.Property(row => row.ArtifactId).HasMaxLength(512);
        builder.Property(row => row.ContentJson).IsRequired();
        builder.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(row => row.Current).IsConcurrencyToken();
        builder.HasIndex(row => new { row.ScopeHash, row.SlotHash, row.Current });
        builder.HasIndex(row => new { row.ScopeHash, row.TenantHash, row.Current, row.Applied, row.SlotHash, row.Revision })
            .HasDatabaseName("IX_KernelEventPublication_Apply");
        builder.HasIndex(row => new { row.ScopeHash, row.TenantHash, row.Current, row.SlotHash, row.Revision })
            .HasDatabaseName("IX_KernelEventPublication_Retain");
        builder.HasIndex(row => new { row.ScopeHash, row.TenantHash, row.InsertionOrdinal }).IsUnique()
            .HasDatabaseName("UX_KernelEventPublication_InsertionOrdinal");
        builder.HasIndex(row => new { row.ScopeHash, row.TenantHash, row.Current, row.Applied, row.InsertionOrdinal })
            .HasDatabaseName("IX_KernelEventPublication_ApplyScan");
        builder.HasIndex(row => new { row.ScopeHash, row.TenantHash, row.Current, row.InsertionOrdinal })
            .HasDatabaseName("IX_KernelEventPublication_RetainScan");
        builder.HasIndex(row => new { row.ScopeHash, row.TenantHash, row.InsertionOrdinal, row.Current, row.Applied, row.SlotHash, row.Revision })
            .HasDatabaseName("IX_KernelEventPublication_ApplyLegacy");
        builder.HasIndex(row => new { row.ScopeHash, row.TenantHash, row.InsertionOrdinal, row.Current, row.SlotHash, row.Revision })
            .HasDatabaseName("IX_KernelEventPublication_RetainLegacy");
    }
}
