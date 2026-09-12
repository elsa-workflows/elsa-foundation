using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;

public abstract class StructuredLogsDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<StructuredLogRecord> Records => Set<StructuredLogRecord>();
    public DbSet<StructuredLogStreamState> StreamStates => Set<StructuredLogStreamState>();
    public DbSet<StructuredLogAppendOperation> AppendOperations => Set<StructuredLogAppendOperation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new StructuredLogRecordConfiguration());
        modelBuilder.ApplyConfiguration(new StructuredLogStreamStateConfiguration());
        modelBuilder.ApplyConfiguration(new StructuredLogAppendOperationConfiguration());
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}
