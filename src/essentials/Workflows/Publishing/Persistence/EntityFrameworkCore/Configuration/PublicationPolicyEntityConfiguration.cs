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
        builder.Property(row => row.PolicyKey).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedPolicyKeyMaximumLength).IsRequired();
        builder.Property(row => row.PolicyKeyHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.WorkflowDefinitionId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength);
        builder.Property(row => row.WorkflowDefinitionIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength);
        builder.Property(row => row.TenantId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength);
        builder.Property(row => row.TenantIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.DefaultAction).HasMaxLength(PublishingPolicyProjectionEfModule.EnumMaximumLength).IsRequired();
        builder.Property(row => row.DefaultSlotName).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.Revision).IsConcurrencyToken().IsRequired();
        builder.Property(row => row.UpdatedAtUtcTicks).IsRequired();
        builder.Property(row => row.UpdatedAtOffsetMinutes).IsRequired();

        // Raw residuals are intentionally excluded: MySQL's composite index budget is provider
        // dependent. The adapter performs a bounded hash candidate lookup and exact residual check.
        builder.HasIndex(row => new { row.TenantIdHash, row.PolicyKeyHash })
            .HasDatabaseName(PublishingPolicyProjectionEfModule.PolicyByIdentityIndexName)
            .IsUnique();
    }
}
