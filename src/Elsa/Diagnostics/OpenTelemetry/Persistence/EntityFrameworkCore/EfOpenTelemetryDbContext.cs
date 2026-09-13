using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore;

public abstract class OpenTelemetryDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<OpenTelemetryResourceEntity> Resources => Set<OpenTelemetryResourceEntity>();
    public DbSet<OpenTelemetryTraceEntity> Traces => Set<OpenTelemetryTraceEntity>();
    public DbSet<OpenTelemetrySpanEntity> Spans => Set<OpenTelemetrySpanEntity>();
    public DbSet<OpenTelemetryMetricInstrumentEntity> Instruments => Set<OpenTelemetryMetricInstrumentEntity>();
    public DbSet<OpenTelemetryMetricPointEntity> MetricPoints => Set<OpenTelemetryMetricPointEntity>();
    public DbSet<OpenTelemetryLogEntity> Logs => Set<OpenTelemetryLogEntity>();
    public DbSet<OpenTelemetryCaptureLedgerEntity> CaptureLedger => Set<OpenTelemetryCaptureLedgerEntity>();
    public DbSet<OpenTelemetryTraceSummaryEntity> TraceSummaries => Set<OpenTelemetryTraceSummaryEntity>();
    public DbSet<OpenTelemetryTraceSummaryMembershipEntity> TraceSummaryMemberships => Set<OpenTelemetryTraceSummaryMembershipEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OpenTelemetryResourceEntity>(entity =>
        {
            entity.ToTable(EfOpenTelemetryModule.ResourceTable);
            entity.HasKey(x => new { x.ScopeKey, x.IdOrderKey });
            entity.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Id).HasMaxLength(512).IsRequired();
            entity.Property(x => x.IdOrderKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.IdSearchKey).HasMaxLength(3584).IsRequired();
            entity.Property(x => x.ServiceName).HasMaxLength(512).IsRequired();
            entity.Property(x => x.ServiceNameSearchKey).HasMaxLength(3584).IsRequired();
            entity.Property(x => x.ServiceNameKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.HasIndex(x => new { x.ScopeKey, x.ServiceNameKey, x.LastSeenTicks });
            entity.HasIndex(x => new { x.ScopeKey, x.LastSeenTicks, x.IdOrderKey });
        });
        modelBuilder.Entity<OpenTelemetryTraceEntity>(entity =>
        {
            entity.ToTable(EfOpenTelemetryModule.TraceTable);
            entity.HasKey(x => new { x.ScopeKey, x.Sequence });
            entity.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Id).HasMaxLength(256).IsRequired();
            entity.Property(x => x.IdOrderKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TraceId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.TraceIdSearchKey).HasMaxLength(1792).IsRequired();
            entity.Property(x => x.TraceKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(571);
            entity.Property(x => x.NameSearchKey).HasMaxLength(3997);
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.HasIndex(x => new { x.ScopeKey, x.TraceKey, x.Sequence });
            entity.HasIndex(x => new { x.ScopeKey, x.StartTimeTicks, x.TraceKey });
        });
        modelBuilder.Entity<OpenTelemetrySpanEntity>(entity =>
        {
            entity.ToTable(EfOpenTelemetryModule.SpanTable);
            entity.HasKey(x => new { x.ScopeKey, x.Sequence });
            entity.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Id).HasMaxLength(128).IsRequired();
            entity.Property(x => x.IdSearchKey).HasMaxLength(896).IsRequired();
            entity.Property(x => x.IdOrderKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TraceId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.TraceIdSearchKey).HasMaxLength(1792).IsRequired();
            entity.Property(x => x.TraceKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.SpanId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.SpanIdSearchKey).HasMaxLength(896).IsRequired();
            entity.Property(x => x.SpanIdOrderKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ResourceId).HasMaxLength(512).IsRequired();
            entity.Property(x => x.ResourceIdSearchKey).HasMaxLength(3584).IsRequired();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.NameSearchKey).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.HasIndex(x => new { x.ScopeKey, x.TraceKey, x.StartTimeTicks, x.SpanIdOrderKey, x.Sequence });
        });
        modelBuilder.Entity<OpenTelemetryMetricInstrumentEntity>(entity =>
        {
            entity.ToTable(EfOpenTelemetryModule.InstrumentTable);
            entity.HasKey(x => new { x.ScopeKey, x.IdOrderKey });
            entity.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Id).HasMaxLength(512).IsRequired();
            entity.Property(x => x.IdOrderKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.IdSearchKey).HasMaxLength(3584).IsRequired();
            entity.Property(x => x.ResourceId).HasMaxLength(512).IsRequired();
            entity.Property(x => x.ResourceIdSearchKey).HasMaxLength(3584).IsRequired();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.NameSearchKey).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.HasIndex(x => new { x.ScopeKey, x.IdOrderKey });
        });
        modelBuilder.Entity<OpenTelemetryMetricPointEntity>(entity =>
        {
            entity.ToTable(EfOpenTelemetryModule.MetricPointTable);
            entity.HasKey(x => new { x.ScopeKey, x.Sequence });
            entity.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Id).HasMaxLength(128).IsRequired();
            entity.Property(x => x.IdSearchKey).HasMaxLength(896).IsRequired();
            entity.Property(x => x.IdOrderKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.InstrumentId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.InstrumentIdSearchKey).HasMaxLength(1792).IsRequired();
            entity.Property(x => x.InstrumentName).IsRequired();
            entity.Property(x => x.InstrumentNameSearchKey).IsRequired();
            entity.Property(x => x.ResourceId).HasMaxLength(512).IsRequired();
            entity.Property(x => x.ResourceIdSearchKey).HasMaxLength(3584).IsRequired();
            entity.Property(x => x.ServiceNameKey).HasMaxLength(64);
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.HasIndex(x => new { x.ScopeKey, x.TimestampTicks, x.IdOrderKey });
        });
        modelBuilder.Entity<OpenTelemetryLogEntity>(entity =>
        {
            entity.ToTable(EfOpenTelemetryModule.LogTable);
            entity.HasKey(x => new { x.ScopeKey, x.Sequence });
            entity.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Id).HasMaxLength(128).IsRequired();
            entity.Property(x => x.IdSearchKey).HasMaxLength(896).IsRequired();
            entity.Property(x => x.ResourceId).HasMaxLength(512).IsRequired();
            entity.Property(x => x.ResourceIdSearchKey).HasMaxLength(3584).IsRequired();
            entity.Property(x => x.ServiceNameKey).HasMaxLength(64);
            entity.Property(x => x.TraceIdSearchKey).HasMaxLength(1792);
            entity.Property(x => x.SpanIdSearchKey).HasMaxLength(1792);
            entity.Property(x => x.SeverityText).IsRequired();
            entity.Property(x => x.SeveritySearchKey).IsRequired();
            entity.Property(x => x.Body).IsRequired();
            entity.Property(x => x.BodySearchKey).IsRequired();
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.HasIndex(x => new { x.ScopeKey, x.TimestampTicks, x.IdOrderKey });
        });
        modelBuilder.Entity<OpenTelemetryCaptureLedgerEntity>(entity =>
        {
            entity.ToTable(EfOpenTelemetryModule.LedgerTable);
            entity.HasKey(x => new { x.ScopeKey, x.BatchId });
            entity.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.ScopeKey, x.IssuedAtTicks });
        });
        modelBuilder.Entity<OpenTelemetryTraceSummaryEntity>(entity =>
        {
            entity.ToTable(EfOpenTelemetryModule.SummaryTable);
            entity.HasKey(x => new { x.ScopeKey, x.TraceKey });
            entity.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TraceKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TraceId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.TraceIdSearchKey).HasMaxLength(1792).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(571);
            entity.Property(x => x.NameSearchKey).HasMaxLength(3997);
            entity.Property(x => x.PayloadJson).IsRequired();
            entity.Property(x => x.ServiceMembershipJson).IsRequired();
            entity.Property(x => x.WorkflowMembershipJson).IsRequired();
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => new { x.ScopeKey, x.StartTimeTicks, x.TraceKey });
        });
        modelBuilder.Entity<OpenTelemetryTraceSummaryMembershipEntity>(entity =>
        {
            entity.ToTable(EfOpenTelemetryModule.MembershipTable);
            entity.HasKey(x => new { x.ScopeKey, x.TraceKey, x.Kind, x.ValueKey });
            entity.Property(x => x.ScopeKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TraceKey).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Value).HasMaxLength(512).IsRequired();
            entity.Property(x => x.ValueSearchKey).HasMaxLength(3584).IsRequired();
            entity.Property(x => x.ValueKey).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => new { x.ScopeKey, x.Kind, x.ValueKey, x.TraceKey });
        });
        ConfigureProvider(modelBuilder);
    }

    protected abstract void ConfigureProvider(ModelBuilder modelBuilder);
}

/// <summary>Compatibility base retained for provider-host code that used the early EF adapter name.</summary>
public abstract class EfOpenTelemetryDbContext(DbContextOptions options) : OpenTelemetryDbContext(options)
{
}
