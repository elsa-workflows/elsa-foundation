using Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.Entities;
using Elsa.Persistence.EFCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EFCore.DbContext;

public sealed class StructuredLogsDbContext(DbContextOptions<StructuredLogsDbContext> options, IServiceProvider serviceProvider)
    : ElsaDbContextBase(options, serviceProvider)
{
    public DbSet<PersistedStructuredLogEntry> StructuredLogEntries { get; set; } = null!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
