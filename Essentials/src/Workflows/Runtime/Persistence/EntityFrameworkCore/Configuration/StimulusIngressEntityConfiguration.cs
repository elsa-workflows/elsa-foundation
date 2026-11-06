using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal sealed class StimulusIngressSourceEntityConfiguration : IEntityTypeConfiguration<StimulusIngressSourceEntity>
{
    public void Configure(EntityTypeBuilder<StimulusIngressSourceEntity> b)
    {
        b.ToTable(RuntimeStimulusIngressEfModule.SourceTableName);
        b.HasKey(row => row.Id);
        b.Property(row => row.Id).HasMaxLength(64);
        b.Property(row => row.ScopeKey).HasMaxLength(RuntimeOperationalStateEfModule.ScopeProjectionMaximumLength).IsRequired();
        b.Property(row => row.ScopeKeyHash).HasMaxLength(64).IsRequired();
        b.Property(row => row.SourceId).IsRequired();
        b.Property(row => row.SourceIdHash).HasMaxLength(64).IsRequired();
        b.Property(row => row.OriginFingerprint).HasMaxLength(64).IsRequired();
        b.Property(row => row.SourceFingerprint).HasMaxLength(64).IsRequired();
        b.Property(row => row.ContentJson).IsRequired();
        b.Property(row => row.SchemaVersion).HasMaxLength(32).IsRequired();
        b.Property(row => row.Revision).IsConcurrencyToken();
        b.HasIndex(row => new { row.ScopeKeyHash, row.SourceIdHash }).IsUnique();
        b.HasIndex(row => new { row.ScopeKeyHash, row.AcceptedAtUtcTicks, row.Id });
    }
}
