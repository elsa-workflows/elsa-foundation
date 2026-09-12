using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Configuration;

public sealed class StructuredLogRecordConfiguration : IEntityTypeConfiguration<StructuredLogRecord>
{
    public void Configure(EntityTypeBuilder<StructuredLogRecord> builder)
    {
        builder.ToTable(StructuredLogsEfModule.RecordsTableName);
        builder.HasKey(record => new { record.ScopeKey, record.Position });
        builder.Property(record => record.ScopeKey).HasMaxLength(64).IsRequired();
        builder.Property(record => record.Position).IsRequired();
        builder.Property(record => record.TenantId).HasMaxLength(64).IsRequired();
        builder.Property(record => record.ScopeId).HasMaxLength(64).IsRequired();
        builder.Property(record => record.StreamId).HasMaxLength(64).IsRequired();
        builder.Property(record => record.TimestampTicks).IsRequired();
        builder.Property(record => record.TimestampOffsetMinutes).IsRequired();
        builder.Property(record => record.Level).IsRequired();
        builder.Property(record => record.CategoryKey).HasMaxLength(64).IsRequired();
        builder.Property(record => record.SourceKey).HasMaxLength(64).IsRequired();
        builder.Property(record => record.ReplayToken).HasMaxLength(32).IsRequired();
        builder.Property(record => record.PayloadJson).IsRequired();

        builder.HasIndex(record => new { record.ScopeKey, record.Level, record.Position })
            .HasDatabaseName("IX_elsa_structured_log_records_scope_level_position");
        builder.HasIndex(record => new { record.ScopeKey, record.CategoryKey, record.Position })
            .HasDatabaseName("IX_elsa_structured_log_records_scope_category_position");
        builder.HasIndex(record => new { record.ScopeKey, record.SourceKey, record.Position })
            .HasDatabaseName("IX_elsa_structured_log_records_scope_source_position");
        builder.HasIndex(record => new { record.ScopeKey, record.ReplayToken })
            .HasDatabaseName("IX_elsa_structured_log_records_scope_replay")
            .IsUnique();
    }
}
