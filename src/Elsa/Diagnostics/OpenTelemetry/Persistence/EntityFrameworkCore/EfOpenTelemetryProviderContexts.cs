using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;

public sealed class OpenTelemetrySqliteDbContext(DbContextOptions<OpenTelemetrySqliteDbContext> options)
    : EfOpenTelemetryDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.Sqlite;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => EfOpenTelemetryProviderModel.Configure(modelBuilder, "TEXT", "TEXT");
}

public sealed class OpenTelemetrySqlServerDbContext(DbContextOptions<OpenTelemetrySqlServerDbContext> options)
    : EfOpenTelemetryDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.SqlServer;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => EfOpenTelemetryProviderModel.Configure(modelBuilder, "nvarchar(max)", "nvarchar(max)");
}

public sealed class OpenTelemetryPostgreSqlDbContext(DbContextOptions<OpenTelemetryPostgreSqlDbContext> options)
    : EfOpenTelemetryDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.PostgreSql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => EfOpenTelemetryProviderModel.Configure(modelBuilder, "text", "text");
}

public sealed class OpenTelemetryMySqlDbContext(DbContextOptions<OpenTelemetryMySqlDbContext> options)
    : EfOpenTelemetryDbContext(options)
{
    public const string ExpectedProviderName = Elsa.Persistence.EntityFramework.EfProviderNames.MySql;
    protected override void ConfigureProvider(ModelBuilder modelBuilder) => EfOpenTelemetryProviderModel.Configure(modelBuilder, "longtext", "text");
}

internal static class EfOpenTelemetryProviderModel
{
    public static void Configure(ModelBuilder modelBuilder, string payloadType, string canonicalType)
    {
        foreach (var type in new Type[]
        {
            typeof(OpenTelemetryResourceEntity),
            typeof(OpenTelemetryTraceEntity),
            typeof(OpenTelemetrySpanEntity),
            typeof(OpenTelemetryMetricInstrumentEntity),
            typeof(OpenTelemetryMetricPointEntity),
            typeof(OpenTelemetryLogEntity),
            typeof(OpenTelemetryTraceSummaryEntity)
        })
        {
            modelBuilder.Entity(type).Property("PayloadJson").HasColumnType(payloadType);
            foreach (var property in modelBuilder.Entity(type).Metadata.GetProperties().Where(property => property.Name.EndsWith("SearchKey", StringComparison.Ordinal)))
            {
                // Search keys are ASCII projections. Oversized keys are deliberately unindexed text so
                // MySQL's utf8mb4 row/index limits cannot reject an otherwise valid telemetry record.
                modelBuilder.Entity(type).Property(property.Name).HasColumnType(canonicalType).IsUnicode(false);
            }
        }
    }
}
