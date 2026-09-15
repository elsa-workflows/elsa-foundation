using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Configuration;

public sealed class PublicationSnapshotReviewEntityConfiguration : IEntityTypeConfiguration<PublicationSnapshotReviewEntity>
{
    public void Configure(EntityTypeBuilder<PublicationSnapshotReviewEntity> builder)
    {
        builder.ToTable(PublishingSnapshotReviewEfModule.TableName);
        builder.HasKey(row => row.PreflightToken);
        builder.Property(row => row.PreflightToken).HasMaxLength(PublishingSnapshotReviewEfModule.IdentityMaximumLength).IsRequired();
        builder.Property(row => row.Incarnation).HasMaxLength(PublishingSnapshotReviewEfModule.IncarnationMaximumLength).IsRequired();
        builder.Property(row => row.CandidateHash).HasMaxLength(PublishingSnapshotReviewEfModule.CandidateHashMaximumLength).IsRequired();
        builder.Property(row => row.DefinitionId).HasMaxLength(PublishingSnapshotReviewEfModule.IdentityMaximumLength).IsRequired();
        builder.Property(row => row.Action).HasMaxLength(32).IsRequired();
        builder.Property(row => row.SlotName).HasMaxLength(PublishingSnapshotReviewEfModule.IdentityMaximumLength).IsRequired();
        builder.Property(row => row.PolicySource).HasMaxLength(32).IsRequired();
        builder.Property(row => row.RequestedAction).HasMaxLength(32);
        builder.Property(row => row.RequestedSlotName).HasMaxLength(PublishingSnapshotReviewEfModule.IdentityMaximumLength);
        builder.Property(row => row.RequestedExpectedPublicationId).HasMaxLength(PublishingSnapshotReviewEfModule.IdentityMaximumLength);
        builder.Property(row => row.ActivePublicationId).HasMaxLength(PublishingSnapshotReviewEfModule.IdentityMaximumLength);
        builder.Property(row => row.TenantId).HasMaxLength(PublishingSnapshotReviewEfModule.IdentityMaximumLength);
        builder.Property(row => row.ExpiresAt)
            .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero))
            .IsRequired();
        builder.HasIndex(row => new { row.ExpiresAt, row.PreflightToken })
            .HasDatabaseName(PublishingSnapshotReviewEfModule.ExpiryIndexName);
    }
}
