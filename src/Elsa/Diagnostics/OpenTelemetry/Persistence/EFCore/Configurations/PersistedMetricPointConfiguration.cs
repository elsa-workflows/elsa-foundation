using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Configurations;

public sealed class PersistedMetricPointConfiguration : IEntityTypeConfiguration<PersistedMetricPoint>
{
    public void Configure(EntityTypeBuilder<PersistedMetricPoint> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.PointId).HasMaxLength(128);
        builder.Property(x => x.InstrumentId).HasMaxLength(512);
        builder.Property(x => x.InstrumentName).HasMaxLength(512);
        builder.Property(x => x.ResourceId).HasMaxLength(512);
        builder.Property(x => x.TraceId).HasMaxLength(128);
        builder.Property(x => x.SpanId).HasMaxLength(128);
        builder.Property(x => x.AttributesJson).HasMaxLength(-1);
        builder.HasIndex(x => x.InstrumentId).HasDatabaseName($"IX_{nameof(PersistedMetricPoint)}_{nameof(PersistedMetricPoint.InstrumentId)}");
        builder.HasIndex(x => x.ResourceId).HasDatabaseName($"IX_{nameof(PersistedMetricPoint)}_{nameof(PersistedMetricPoint.ResourceId)}");
        builder.HasIndex(x => x.Timestamp).HasDatabaseName($"IX_{nameof(PersistedMetricPoint)}_{nameof(PersistedMetricPoint.Timestamp)}");
    }
}
