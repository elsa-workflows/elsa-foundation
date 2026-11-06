using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal sealed class RuntimeIngressSourceEntityConfiguration : IEntityTypeConfiguration<RuntimeIngressSourceEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeIngressSourceEntity> b)
    {
        RuntimeDeliverySourceEntityConfiguration.Common(b, RuntimeDeliverySourceEfModule.SourceTableName);
        b.Property(row => row.HeaderFingerprint).IsRequired();
        b.Property(row => row.AcceptanceOperationId).IsRequired();
        b.Property(row => row.AcceptanceOperationFingerprint).IsRequired();
        b.Property(row => row.OperationProofJson).IsRequired();
        b.HasIndex(row => new { row.ScopeKeyHash, row.SourceFamilyHash, row.SourceIdHash });
        b.HasIndex(row => new { row.FullDiscoveryIdentityHash, row.SourceFamilyHash, row.Status, row.RecordedAtUtcTicks });
    }
}

internal sealed class RuntimeDeliveryWorkEntityConfiguration : IEntityTypeConfiguration<RuntimeDeliveryWorkEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeDeliveryWorkEntity> b)
    {
        RuntimeDeliverySourceEntityConfiguration.Common(b, RuntimeDeliverySourceEfModule.WorkTableName);
        b.Property(row => row.SourceHeaderFingerprint).IsRequired();
        b.Property(row => row.WorkId).IsRequired();
        b.Property(row => row.WorkIdHash).HasMaxLength(64).IsRequired();
        b.Property(row => row.WorkKind).IsRequired();
        b.Property(row => row.WorkKindHash).HasMaxLength(64).IsRequired();
        b.Property(row => row.ContentFingerprint).IsRequired();
        // A family adapter resolves the one canonical source row. Existing C2 ingress sources are not copied
        // into RuntimeIngressSources, so this cross-family address deliberately has no generic-source FK.
        b.HasIndex(row => new { row.ScopeKeyHash, row.SourceFamilyHash, row.SourceIdHash, row.WorkIdHash });
        // Only bounded hash/discriminator/time fields enter the discovery key on every provider. Full encoded
        // identities and canonical source joins still filter before Take; hashes never establish ownership.
        b.HasIndex(row => new { row.FullDiscoveryIdentityHash, row.SourceFamilyHash, row.WorkKindHash,
            row.Role, row.ClaimableIsEligible, row.ClaimableAtUtcTicks });
    }
}

file static class RuntimeDeliverySourceEntityConfiguration
{
    internal static void Common<T>(EntityTypeBuilder<T> b, string tableName) where T : class
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, tableName);
        b.HasKey("Id");
        b.Property("Id").HasMaxLength(64).IsRequired();
        b.Property("SourceFamily").IsRequired();
        b.Property("SourceFamilyHash").HasMaxLength(64).IsRequired();
        b.Property("SourceId").IsRequired();
        b.Property("SourceIdHash").HasMaxLength(64).IsRequired();
        b.Property("TenantIdHash").HasMaxLength(64);
        b.Property("TestScopeIdHash").HasMaxLength(64);
        b.Property("AuthorityPartitionKey").HasMaxLength(64).IsRequired();
        b.Property("FullDiscoveryIdentity").IsRequired();
        b.Property("FullDiscoveryIdentityHash").HasMaxLength(64).IsRequired();
    }
}
