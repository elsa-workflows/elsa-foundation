using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

[Collection(DiagnosticsProviderCollection.Name)]
public sealed class DiagnosticsGroundworkProviderConformanceTests(DiagnosticsProviderFixture providers)
{
    public static TheoryData<DiagnosticsProviderKind> ProviderKinds =>
        new()
        {
            DiagnosticsProviderKind.Sqlite,
            DiagnosticsProviderKind.SqlServer,
            DiagnosticsProviderKind.PostgreSql,
            DiagnosticsProviderKind.MongoDb
        };

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task OpenTelemetry_catalogs_records_and_counts_survive_restart(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var binding = GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-a", "collector-a");
        var batch = OpenTelemetryBatch();

        await using (var first = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding))
        {
            var store = new GroundworkOpenTelemetryStore(
                first.Stores,
                Options.Create(new OpenTelemetryDiagnosticsOptions()),
                binding);
            await store.WriteAsync(DiagnosticsDrainBatchId.New(), batch);
        }

        await using var restarted = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding);
        var restartedStore = new GroundworkOpenTelemetryStore(
            restarted.Stores,
            Options.Create(new OpenTelemetryDiagnosticsOptions()),
            binding);
        var diagnostics = await restartedStore.GetDiagnosticsAsync();
        var resources = await restartedStore.QueryResourcesAsync(new() { Take = 10 });
        var metrics = await restartedStore.QueryMetricsAsync(new() { Take = 10 });
        var logs = await restartedStore.QueryLogsAsync(new() { Take = 10 });

        Assert.Equal((1, 1, 1, 1, 1, 1),
            (diagnostics.ResourceCount, diagnostics.TraceCount, diagnostics.SpanCount,
                diagnostics.MetricInstrumentCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
        Assert.Equal("resource-1", Assert.Single(resources.Items).Id);
        Assert.Equal("instrument-1", Assert.Single(metrics.Instruments).Id);
        Assert.Equal("point-1", Assert.Single(metrics.Points).Id);
        Assert.Equal("log-1", Assert.Single(logs.Items).Id);
    }

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task Structured_log_commit_cursor_and_high_water_survive_restart(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var binding = new StructuredLogStoreBinding("tenant-a", "shell-a", "structured-logs");
        StructuredLogEntry committed;

        await using (var firstProvider = await DiagnosticsGroundworkProviderHarness.CreateStructuredLogsAsync(provider, binding))
        await using (var firstStore = new GroundworkStructuredLogStore(
                         firstProvider.Store,
                         Options.Create(new StructuredLogsOptions()),
                         binding))
        {
            firstStore.Start();
            committed = await firstStore.AppendAsync(StructuredLogEntry());
            Assert.NotNull(committed.ReplayCursor);
        }

        await using var restartedProvider = await DiagnosticsGroundworkProviderHarness.CreateStructuredLogsAsync(provider, binding);
        await using var restartedStore = new GroundworkStructuredLogStore(
            restartedProvider.Store,
            Options.Create(new StructuredLogsOptions()),
            binding);
        restartedStore.Start();
        var recent = await restartedStore.GetRecentAsync(StructuredLogFilter.None);
        var page = await restartedStore.ReadAfterAsync(null, StructuredLogFilter.None, 10);

        Assert.Equal(1, await restartedStore.GetHighWaterMarkAsync());
        Assert.Equal("persisted", Assert.Single(recent).Message);
        Assert.Equal(committed.ReplayCursor, Assert.Single(page.Entries).ReplayCursor);
    }

    private static OpenTelemetryBatch OpenTelemetryBatch()
    {
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-1);
        var resource = new TelemetryResource(
            "resource-1", "api", "api-1", "dotnet", new Dictionary<string, string?>(), timestamp,
            TelemetryResourceStatus.Active);
        var trace = new TelemetryTrace(
            "trace-1", "span-root", "request", timestamp, timestamp.AddMilliseconds(10),
            TimeSpan.FromMilliseconds(10), SpanStatus.Ok, [resource.Id], [], 1);
        var span = new TelemetrySpan(
            "span-record-1", trace.TraceId, "span-1", null, resource.Id, "request", "internal",
            timestamp, timestamp.AddMilliseconds(10), SpanStatus.Ok, null,
            new Dictionary<string, string?>(), [], []);
        var instrument = new MetricInstrument(
            "instrument-1", resource.Id, "request.duration", "ms", null, MetricKind.Gauge,
            new Dictionary<string, string?>());
        var point = new MetricPoint(
            "point-1", instrument.Id, instrument.Name, resource.Id, timestamp, 10, null, null,
            new Dictionary<string, string?>(), trace.TraceId, span.SpanId);
        var log = new OtlpLogRecord(
            "log-1", resource.Id, timestamp, "Information", null, "request completed", trace.TraceId,
            span.SpanId, new Dictionary<string, string?>());
        return new([resource], [trace], [span], [instrument], [point], [log]);
    }

    private static StructuredLogEntry StructuredLogEntry() => new()
    {
        Sequence = 1,
        Timestamp = DateTimeOffset.UtcNow.AddMinutes(-1),
        Level = LogLevel.Information,
        Category = "Provider.Conformance",
        Message = "persisted",
        SourceId = "provider-matrix"
    };
}
