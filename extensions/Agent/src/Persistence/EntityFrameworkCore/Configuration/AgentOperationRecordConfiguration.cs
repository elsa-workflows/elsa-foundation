using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Agent.Persistence.EntityFrameworkCore.Configuration;

internal sealed class AgentOperationRecordConfiguration : IEntityTypeConfiguration<AgentOperationRecord>
{
    public void Configure(EntityTypeBuilder<AgentOperationRecord> builder)
    {
        builder.ToTable("elsa_agent_operations");
        builder.HasKey(x => new { x.ScopeKey, x.OperationKey });
        builder.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OperationKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
    }
}
