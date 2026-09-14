using Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Diagnostics.StructuredLogs.Persistence.EntityFrameworkCore;

public sealed class StructuredLogsPostgreSqlDbContext(DbContextOptions<StructuredLogsPostgreSqlDbContext> options)
    : StructuredLogsDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;

    protected override void ConfigureProvider(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StructuredLogRecord>(entity => entity.Property(record => record.PayloadJson).HasColumnType("text"));
        modelBuilder.Entity<StructuredLogAppendOperation>(entity => entity.Property(operation => operation.OutcomeJson).HasColumnType("text"));
    }
}
