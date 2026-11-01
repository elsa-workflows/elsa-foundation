using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Configuration;

internal sealed class SpecialistOperationRecordConfiguration : IEntityTypeConfiguration<SpecialistOperationRecord>
{
    public void Configure(EntityTypeBuilder<SpecialistOperationRecord> builder)
    {
        builder.ToTable("elsa_specialist_operations");
        builder.HasKey(x => new { x.ScopeKey, x.OperationKey });
        builder.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OperationKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}
