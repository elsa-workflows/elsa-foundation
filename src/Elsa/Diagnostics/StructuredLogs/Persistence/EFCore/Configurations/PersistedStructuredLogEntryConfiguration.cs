using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Configurations;

public sealed class PersistedStructuredLogEntryConfiguration : IEntityTypeConfiguration<PersistedStructuredLogEntry>
{
    public void Configure(EntityTypeBuilder<PersistedStructuredLogEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();

        // Non-unique: the in-process Sequence can repeat across host restarts. Durable ordering uses Id;
        // this index just accelerates the within-process "after sequence N" resume query.
        builder.HasIndex(x => x.Sequence).HasDatabaseName($"IX_{nameof(PersistedStructuredLogEntry)}_{nameof(PersistedStructuredLogEntry.Sequence)}");
        builder.HasIndex(x => x.Timestamp).HasDatabaseName($"IX_{nameof(PersistedStructuredLogEntry)}_{nameof(PersistedStructuredLogEntry.Timestamp)}");
        builder.HasIndex(x => x.Level).HasDatabaseName($"IX_{nameof(PersistedStructuredLogEntry)}_{nameof(PersistedStructuredLogEntry.Level)}");

        builder.Property(x => x.Category).HasMaxLength(512);
        builder.Property(x => x.EventName).HasMaxLength(512);
        builder.Property(x => x.SourceId).HasMaxLength(256);
        builder.Property(x => x.Message).HasMaxLength(-1);
        builder.Property(x => x.MessageTemplate).HasMaxLength(-1);
        builder.Property(x => x.PropertiesJson).HasMaxLength(-1);
        builder.Property(x => x.ScopesJson).HasMaxLength(-1);
        builder.Property(x => x.ExceptionJson).HasMaxLength(-1);
    }
}
