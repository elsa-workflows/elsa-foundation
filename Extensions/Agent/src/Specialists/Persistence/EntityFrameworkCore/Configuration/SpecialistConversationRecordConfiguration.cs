using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Configuration;

internal sealed class SpecialistConversationRecordConfiguration : IEntityTypeConfiguration<SpecialistConversationRecord>
{
    public void Configure(EntityTypeBuilder<SpecialistConversationRecord> builder)
    {
        builder.ToTable("elsa_specialist_conversations");
        builder.HasKey(x => new { x.ScopeKey, x.Id });
        builder.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Id).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}
