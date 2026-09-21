using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Configuration;

public sealed class PublicationProjectionIntentEntityConfiguration : IEntityTypeConfiguration<PublicationProjectionIntentEntity>
{
    public void Configure(EntityTypeBuilder<PublicationProjectionIntentEntity> builder)
    {
        builder.ToTable(PublishingPolicyProjectionEfModule.ProjectionIntentTableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.IntentId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.IntentIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.IntentIdOrderKey).HasMaxLength(PublishingPolicyProjectionEfModule.IntentIdOrderKeyMaximumLength).IsRequired();
        builder.Property(row => row.PublicationId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.PublicationIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.ProjectionKind).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.ProjectionKindHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.Operation).HasMaxLength(PublishingPolicyProjectionEfModule.EnumMaximumLength).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(PublishingPolicyProjectionEfModule.EnumMaximumLength).IsRequired();
        builder.Property(row => row.AttemptCount).IsRequired();
        builder.Property(row => row.LastFailureCode).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedFailureCodeMaximumLength);
        builder.Property(row => row.LastFailureMessage).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedFailureMessageMaximumLength);
        builder.Property(row => row.TenantId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength);
        builder.Property(row => row.TenantIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.Revision).IsConcurrencyToken().IsRequired();

        // Raw residuals are intentionally excluded: MySQL's composite index budget is provider
        // dependent. The adapter performs a bounded hash candidate lookup and exact residual check.
        builder.HasIndex(row => new { row.TenantIdHash, row.IntentIdHash })
            .HasDatabaseName(PublishingPolicyProjectionEfModule.ProjectionIntentByIdentityIndexName)
            .IsUnique();
        builder.HasIndex(row => new { row.TenantIdHash, row.PublicationIdHash, row.IntentIdOrderKey, row.IntentIdHash })
            .HasDatabaseName(PublishingPolicyProjectionEfModule.ProjectionIntentByPublicationIndexName);
    }
}
