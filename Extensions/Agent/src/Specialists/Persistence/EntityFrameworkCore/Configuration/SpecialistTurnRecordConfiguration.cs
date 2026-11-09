using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Configuration;

internal sealed class SpecialistTurnRecordConfiguration : IEntityTypeConfiguration<SpecialistTurnRecord>
{
    public void Configure(EntityTypeBuilder<SpecialistTurnRecord> builder)
    {
        builder.ToTable(SpecialistsEfModule.TurnTableName);
        builder.HasKey(x => new { x.ScopeKey, x.Handle });
        builder.HasIndex(x => new { x.ScopeKey, x.OperationKey }).IsUnique();
        builder.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Handle).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OperationKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Fence).HasMaxLength(64);
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}
