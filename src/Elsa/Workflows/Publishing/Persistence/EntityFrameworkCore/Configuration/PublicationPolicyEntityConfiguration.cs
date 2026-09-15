using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Configuration;

public sealed class PublicationPolicyEntityConfiguration : IEntityTypeConfiguration<PublicationPolicyEntity>
{
    public void Configure(EntityTypeBuilder<PublicationPolicyEntity> builder)
    {
        builder.ToTable(PublishingPolicyProjectionEfModule.PolicyTableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.PolicyKey).HasMaxLength(PublishingPolicyProjectionEfModule.PolicyKeyMaximumLength).IsRequired();
        builder.Property(row => row.PolicyKeyHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.WorkflowDefinitionId).HasMaxLength(PublishingPolicyProjectionEfModule.IdentityMaximumLength);
        builder.Property(row => row.WorkflowDefinitionIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength);
        builder.Property(row => row.TenantId).HasMaxLength(PublishingPolicyProjectionEfModule.IdentityMaximumLength);
        builder.Property(row => row.TenantIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.DefaultAction).HasMaxLength(PublishingPolicyProjectionEfModule.EnumMaximumLength).IsRequired();
        builder.Property(row => row.DefaultSlotName).HasMaxLength(PublishingPolicyProjectionEfModule.IdentityMaximumLength).IsRequired();
        builder.Property(row => row.Revision).IsConcurrencyToken().IsRequired();
        builder.Property(row => row.UpdatedAtUtcTicks).IsRequired();
        builder.Property(row => row.UpdatedAtOffsetMinutes).IsRequired();

        // The raw key is the collision guard; hashes keep the composite index provider-safe.
        builder.HasIndex(row => new { row.TenantIdHash, row.PolicyKeyHash, row.PolicyKey })
            .HasDatabaseName(PublishingPolicyProjectionEfModule.PolicyByIdentityIndexName)
            .IsUnique();
    }
}
