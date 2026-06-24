using Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.Entities;
using Elsa.Persistence.EFCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EFCore.DbContext;

public sealed class OpenTelemetryDbContext(DbContextOptions<OpenTelemetryDbContext> options, IServiceProvider serviceProvider)
    : ElsaDbContextBase(options, serviceProvider)
{
    public DbSet<PersistedTelemetryResource> TelemetryResources { get; set; } = null!;
    public DbSet<PersistedTelemetryTrace> TelemetryTraces { get; set; } = null!;
    public DbSet<PersistedTelemetrySpan> TelemetrySpans { get; set; } = null!;
    public DbSet<PersistedMetricInstrument> MetricInstruments { get; set; } = null!;
    public DbSet<PersistedMetricPoint> MetricPoints { get; set; } = null!;
    public DbSet<PersistedOtlpLogRecord> OtlpLogRecords { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
