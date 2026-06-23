using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Configurations;

public sealed class PersistedTelemetryTraceConfiguration : IEntityTypeConfiguration<PersistedTelemetryTrace>
{
    public void Configure(EntityTypeBuilder<PersistedTelemetryTrace> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.TraceId).HasMaxLength(128);
        builder.Property(x => x.RootSpanId).HasMaxLength(128);
        builder.Property(x => x.Name).HasMaxLength(1024);
        builder.Property(x => x.ResourceIdsJson).HasMaxLength(-1);
        builder.Property(x => x.WorkflowInstanceIdsJson).HasMaxLength(-1);
        builder.HasIndex(x => x.TraceId).HasDatabaseName($"IX_{nameof(PersistedTelemetryTrace)}_{nameof(PersistedTelemetryTrace.TraceId)}");
        builder.HasIndex(x => x.StartTime).HasDatabaseName($"IX_{nameof(PersistedTelemetryTrace)}_{nameof(PersistedTelemetryTrace.StartTime)}");
        builder.HasIndex(x => x.Status).HasDatabaseName($"IX_{nameof(PersistedTelemetryTrace)}_{nameof(PersistedTelemetryTrace.Status)}");
    }
}
