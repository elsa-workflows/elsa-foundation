using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Configuration;

public sealed class ActivityDraftTestRunEntityConfiguration : IEntityTypeConfiguration<ActivityDraftTestRunEntity>
{
    public void Configure(EntityTypeBuilder<ActivityDraftTestRunEntity> builder)
    {
        builder.ToTable(PublishingLedgerEfModule.ActivityDraftTestRunTableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.TestRunId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.TestRunIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.TestRunIdOrderKey).HasMaxLength(PublishingLedgerEfModule.TestRunIdOrderKeyMaximumLength).IsRequired();
        builder.Property(row => row.ReceiptExpiresAtUtcTicks).IsRequired();
        builder.Property(row => row.ReceiptExpiresAtOffsetMinutes).IsRequired();
        builder.Property(row => row.Status).HasMaxLength(PublishingPolicyProjectionEfModule.EnumMaximumLength).IsRequired();
        builder.Property(row => row.SchemaVersion).HasMaxLength(PublishingPolicyProjectionEfModule.SchemaVersionMaximumLength).IsRequired();
        builder.Property(row => row.Content).IsRequired();
        builder.Property(row => row.TenantId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength);
        builder.Property(row => row.TenantIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.Revision).IsConcurrencyToken().IsRequired();

        builder.HasIndex(row => new { row.TenantIdHash, row.TestRunIdHash })
            .HasDatabaseName(PublishingLedgerEfModule.ActivityDraftTestRunByIdentityIndexName)
            .IsUnique();
        // Cleanup is ordered by expiry and then by the ordinal test-run identity, within one scope.
        builder.HasIndex(row => new { row.TenantIdHash, row.ReceiptExpiresAtUtcTicks, row.TestRunIdOrderKey })
            .HasDatabaseName(PublishingLedgerEfModule.ActivityDraftTestRunByExpiryIndexName);
    }
}
