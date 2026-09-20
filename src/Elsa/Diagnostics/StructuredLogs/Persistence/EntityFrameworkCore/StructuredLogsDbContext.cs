using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;

public abstract class StructuredLogsDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<StructuredLogRecord> Records => Set<StructuredLogRecord>();
    public DbSet<StructuredLogStreamState> StreamStates => Set<StructuredLogStreamState>();
    public DbSet<StructuredLogAppendOperation> AppendOperations => Set<StructuredLogAppendOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The host's optional schema; nothing changes when none is configured.
        modelBuilder.HasElsaDefaultSchema(this);
        modelBuilder.ApplyConfiguration(new StructuredLogRecordConfiguration());
        modelBuilder.ApplyConfiguration(new StructuredLogStreamStateConfiguration());
        modelBuilder.ApplyConfiguration(new StructuredLogAppendOperationConfiguration());
        ConfigureProvider(modelBuilder);
        // Installed unconditionally, so this context reads a frame whatever wrote it. Nothing here enables an
        // encoder: with no codec configured these columns are written exactly as they were before.
        modelBuilder.UseElsaPayloadColumns(
            this,
            "PayloadJson",
            "OutcomeJson");
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
