using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Configuration;

public sealed class PublicationRecordEntityConfiguration : IEntityTypeConfiguration<PublicationRecordEntity>
{
    public void Configure(EntityTypeBuilder<PublicationRecordEntity> builder)
    {
        builder.ToTable(PublishingLedgerEfModule.PublicationRecordTableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.PublicationId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.PublicationIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.SlotId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.SlotIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.SlotName).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.WorkflowDefinitionId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.WorkflowDefinitionVersionId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.ArtifactId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.SourceReferenceId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength);
        builder.Property(row => row.ExpectedSlotRevision).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(PublishingPolicyProjectionEfModule.EnumMaximumLength).IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).IsRequired();
        builder.Property(row => row.CreatedAtOffsetMinutes).IsRequired();
        // Failure details come from activation diagnostics, which no contract bounds, so they are kept whole
        // in unindexed text rather than truncated to fit a column.
        builder.Property(row => row.FailureCode);
        builder.Property(row => row.FailureMessage);
        builder.Property(row => row.TenantId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength);
        builder.Property(row => row.TenantIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.Revision).IsConcurrencyToken().IsRequired();

        // Raw residuals are intentionally excluded: MySQL's composite index budget is provider dependent. The
        // adapter reads a bounded hash candidate set and checks the exact encoded residuals.
        builder.HasIndex(row => new { row.TenantIdHash, row.PublicationIdHash })
            .HasDatabaseName(PublishingLedgerEfModule.PublicationRecordByIdentityIndexName)
            .IsUnique();
        // Slot listing pages by the persisted, unique publication hash; the result is ordered in memory.
        builder.HasIndex(row => new { row.TenantIdHash, row.SlotIdHash, row.PublicationIdHash })
            .HasDatabaseName(PublishingLedgerEfModule.PublicationRecordBySlotIndexName);
    }
}
