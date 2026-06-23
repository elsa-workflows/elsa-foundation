using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Configurations;

public sealed class PersistedTelemetrySpanConfiguration : IEntityTypeConfiguration<PersistedTelemetrySpan>
{
    public void Configure(EntityTypeBuilder<PersistedTelemetrySpan> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.SpanRecordId).HasMaxLength(128);
        builder.Property(x => x.TraceId).HasMaxLength(128);
        builder.Property(x => x.SpanId).HasMaxLength(128);
        builder.Property(x => x.ParentSpanId).HasMaxLength(128);
        builder.Property(x => x.ResourceId).HasMaxLength(512);
        builder.Property(x => x.Name).HasMaxLength(1024);
        builder.Property(x => x.Kind).HasMaxLength(128);
        builder.Property(x => x.StatusDescription).HasMaxLength(-1);
        builder.Property(x => x.AttributesJson).HasMaxLength(-1);
        builder.Property(x => x.EventsJson).HasMaxLength(-1);
        builder.Property(x => x.LinksJson).HasMaxLength(-1);
        builder.HasIndex(x => x.TraceId).HasDatabaseName($"IX_{nameof(PersistedTelemetrySpan)}_{nameof(PersistedTelemetrySpan.TraceId)}");
        builder.HasIndex(x => x.ResourceId).HasDatabaseName($"IX_{nameof(PersistedTelemetrySpan)}_{nameof(PersistedTelemetrySpan.ResourceId)}");
        builder.HasIndex(x => x.StartTime).HasDatabaseName($"IX_{nameof(PersistedTelemetrySpan)}_{nameof(PersistedTelemetrySpan.StartTime)}");
    }
}
