using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal sealed class MessagePublicationInterestEntityConfiguration : IEntityTypeConfiguration<MessagePublicationInterestEntity>
{
    public void Configure(EntityTypeBuilder<MessagePublicationInterestEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeMessageInterestEfModule.RootTable);
        b.Property(row => row.ScopeKey).HasMaxLength(null);
        b.HasKey(row => row.Id); b.Property(row => row.Id).HasMaxLength(64);
        b.Property(row => row.TenantIdHash).HasMaxLength(64);
        b.Property(row => row.ActivationId).IsRequired(); b.Property(row => row.ActivationIdHash).HasMaxLength(64).IsRequired();
        b.Property(row => row.SlotId).IsRequired(); b.Property(row => row.SlotIdHash).HasMaxLength(64).IsRequired();
        b.Property(row => row.DescriptorFingerprint).IsRequired();
        b.HasIndex(row => new { row.ScopeKeyHash, row.SlotIdHash, row.State, row.ActivationIdHash });
    }
}

internal sealed class MessageInterestScopeControlEntityConfiguration : IEntityTypeConfiguration<MessageInterestScopeControlEntity>
{
    public void Configure(EntityTypeBuilder<MessageInterestScopeControlEntity> b)
    {
        RuntimeOperationalStateEntityConfigurationHelpers.ConfigureCommon(b, RuntimeMessageInterestEfModule.ControlTable);
        b.Property(row => row.ScopeKey).HasMaxLength(null);
        b.HasKey(row => row.Id); b.Property(row => row.Id).HasMaxLength(64);
        b.Property(row => row.TenantIdHash).HasMaxLength(64); b.Property(row => row.FullIdentity).IsRequired();
        b.HasIndex(row => new { row.ScopeKeyHash, row.TenantIdHash });
    }
}
