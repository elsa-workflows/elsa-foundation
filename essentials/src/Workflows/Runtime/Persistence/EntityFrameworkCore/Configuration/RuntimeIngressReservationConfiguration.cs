using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;

internal sealed class RuntimeIngressReservationConfiguration : IEntityTypeConfiguration<RuntimeIngressReservationEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeIngressReservationEntity> b)
    {
        b.ToTable("elsa_runtime_ingress_reservations");
        b.HasKey(row => row.Id);
        b.Property(row => row.Id).HasMaxLength(128);
        b.Property(row => row.ScopeKey).IsRequired(); b.Property(row => row.ScopeHash).HasMaxLength(64);
        b.Property(row => row.TenantKey).IsRequired();
        b.Property(row => row.StimulusType).IsRequired(); b.Property(row => row.StimulusTypeHash).HasMaxLength(64);
        b.Property(row => row.StimulusHash).IsRequired(); b.Property(row => row.StimulusHashHash).HasMaxLength(64);
        b.Property(row => row.OwnerKind).HasMaxLength(32); b.Property(row => row.OwnerId).IsRequired(); b.Property(row => row.OwnerHash).HasMaxLength(64);
        b.Property(row => row.Revision).IsConcurrencyToken(); b.Property(row => row.ContentJson).IsRequired();
        b.Property(row => row.SchemaVersion).HasMaxLength(32);
        b.HasIndex(row => new { row.ScopeHash, row.StimulusTypeHash, row.StimulusHashHash }).IsUnique();
        b.HasIndex(row => new { row.ScopeHash, row.OwnerKind, row.OwnerHash });
    }
}
