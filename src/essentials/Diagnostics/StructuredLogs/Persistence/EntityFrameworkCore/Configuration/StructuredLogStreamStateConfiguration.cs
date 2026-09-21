using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Configuration;

public sealed class StructuredLogStreamStateConfiguration : IEntityTypeConfiguration<StructuredLogStreamState>
{
    public void Configure(EntityTypeBuilder<StructuredLogStreamState> builder)
    {
        builder.ToTable(StructuredLogsEfModule.StreamStatesTableName);
        builder.HasKey(state => state.ScopeKey);
        builder.Property(state => state.ScopeKey).HasMaxLength(64).IsRequired();
        builder.Property(state => state.TenantId).HasMaxLength(64).IsRequired();
        builder.Property(state => state.ScopeId).HasMaxLength(64).IsRequired();
        builder.Property(state => state.StreamId).HasMaxLength(64).IsRequired();
        builder.Property(state => state.HighWater).IsRequired();
        builder.Property(state => state.AppendOperationCutoffTicks).IsRequired();
        builder.Property(state => state.Version).HasMaxLength(32).IsRequired().IsConcurrencyToken();
        builder.Property(state => state.UpdatedAtTicks).IsRequired();
    }
}
