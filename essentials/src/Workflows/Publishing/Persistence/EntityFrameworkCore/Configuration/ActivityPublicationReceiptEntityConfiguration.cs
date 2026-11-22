using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Configuration;

public sealed class ActivityPublicationReceiptEntityConfiguration : IEntityTypeConfiguration<ActivityPublicationReceiptEntity>
{
    public void Configure(EntityTypeBuilder<ActivityPublicationReceiptEntity> builder)
    {
        builder.ToTable(PublishingLedgerEfModule.ActivityPublicationReceiptTableName);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.ReceiptKeyHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();
        builder.Property(row => row.IdempotencyKey).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength).IsRequired();
        builder.Property(row => row.ReceiptTenantId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength);
        builder.Property(row => row.Status).HasMaxLength(PublishingPolicyProjectionEfModule.EnumMaximumLength).IsRequired();
        builder.Property(row => row.SchemaVersion).HasMaxLength(PublishingPolicyProjectionEfModule.SchemaVersionMaximumLength).IsRequired();
        builder.Property(row => row.Content).IsRequired();
        builder.Property(row => row.TenantId).HasMaxLength(PublishingPolicyProjectionEfModule.EncodedIdentityMaximumLength);
        builder.Property(row => row.TenantIdHash).HasMaxLength(PublishingPolicyProjectionEfModule.HashMaximumLength).IsRequired();

        // Receipts are create-only: the unique scope/receipt-key pair is what makes a racing second create fail.
        builder.HasIndex(row => new { row.TenantIdHash, row.ReceiptKeyHash })
            .HasDatabaseName(PublishingLedgerEfModule.ActivityPublicationReceiptByIdentityIndexName)
            .IsUnique();
    }
}
