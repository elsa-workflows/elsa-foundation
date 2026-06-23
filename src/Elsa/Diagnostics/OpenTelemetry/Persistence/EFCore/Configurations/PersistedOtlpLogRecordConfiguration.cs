using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Configurations;

public sealed class PersistedOtlpLogRecordConfiguration : IEntityTypeConfiguration<PersistedOtlpLogRecord>
{
    public void Configure(EntityTypeBuilder<PersistedOtlpLogRecord> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.LogRecordId).HasMaxLength(128);
        builder.Property(x => x.ResourceId).HasMaxLength(512);
        builder.Property(x => x.SeverityText).HasMaxLength(128);
        builder.Property(x => x.Body).HasMaxLength(-1);
        builder.Property(x => x.TraceId).HasMaxLength(128);
        builder.Property(x => x.SpanId).HasMaxLength(128);
        builder.Property(x => x.AttributesJson).HasMaxLength(-1);
        builder.HasIndex(x => x.ResourceId).HasDatabaseName($"IX_{nameof(PersistedOtlpLogRecord)}_{nameof(PersistedOtlpLogRecord.ResourceId)}");
        builder.HasIndex(x => x.TraceId).HasDatabaseName($"IX_{nameof(PersistedOtlpLogRecord)}_{nameof(PersistedOtlpLogRecord.TraceId)}");
        builder.HasIndex(x => x.Timestamp).HasDatabaseName($"IX_{nameof(PersistedOtlpLogRecord)}_{nameof(PersistedOtlpLogRecord.Timestamp)}");
    }
}
