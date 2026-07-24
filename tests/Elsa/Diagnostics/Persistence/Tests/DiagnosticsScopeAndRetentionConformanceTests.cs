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

/// <summary>
/// Shared real-provider proof that the explicit diagnostics scope owns both retention and visibility.
/// These scenarios deliberately use identical logical identifiers in separate scopes.
/// </summary>
[Collection(DiagnosticsProviderCollection.Name)]
public sealed class DiagnosticsScopeAndRetentionConformanceTests(DiagnosticsProviderFixture providers)
{
    public static TheoryData<DiagnosticsProviderKind> ProviderKinds =>
        DiagnosticsGroundworkProviderConformanceTests.ProviderKinds;

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task Structured_log_retention_never_observes_or_mutates_another_scope(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var firstBinding = new StructuredLogStoreBinding("tenant-a", "scope-a", "structured-logs");
        var secondBinding = new StructuredLogStoreBinding("tenant-b", "scope-b", "structured-logs");

        await using var firstProvider = await DiagnosticsGroundworkProviderHarness.CreateStructuredLogsAsync(provider, firstBinding);
        await using var secondProvider = await DiagnosticsGroundworkProviderHarness.CreateStructuredLogsAsync(provider, secondBinding);
        await using var first = StartStructuredLogs(firstProvider, firstBinding);
        await using var second = StartStructuredLogs(secondProvider, secondBinding);
        var timestamp = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

        await first.AppendAsync(StructuredEntry(1, "first-old", timestamp));
        await first.AppendAsync(StructuredEntry(2, "first-new", timestamp));
        await second.AppendAsync(StructuredEntry(1, "second-only", timestamp));

        await first.TrimAsync(1);

        Assert.Equal(["first-new"], (await first.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
        Assert.Equal(["second-only"], (await second.GetRecentAsync(StructuredLogFilter.None)).Select(entry => entry.Message));
        Assert.Equal(2, await first.GetHighWaterMarkAsync());
        Assert.Equal(1, await second.GetHighWaterMarkAsync());
    }

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task Telemetry_retention_never_observes_or_mutates_another_scope(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var firstBinding = GroundworkOpenTelemetryBinding.Create("tenant-a", "scope-a", "collector-a");
        var secondBinding = GroundworkOpenTelemetryBinding.Create("tenant-b", "scope-b", "collector-b");
        var options = Options.Create(new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 1,
            SpanCapacity = 1,
            MetricPointCapacity = 1,
            LogRecordCapacity = 1,
            ResourceCapacity = 1,
            MetricInstrumentCapacity = 1,
            MaxQuerySize = 20
        });

        await using var firstProvider = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, firstBinding);
        await using var secondProvider = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, secondBinding);
        await using var first = new GroundworkOpenTelemetryStore(firstProvider.Stores, options, firstBinding);
        await using var second = new GroundworkOpenTelemetryStore(secondProvider.Stores, options, secondBinding);
        var timestamp = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

        await first.WriteAsync(DiagnosticsDrainBatchId.New(), Batch("first-old", "orders-a", timestamp));
        await first.WriteAsync(DiagnosticsDrainBatchId.New(), Batch("first-new", "orders-a", timestamp.AddSeconds(1)));
        await second.WriteAsync(DiagnosticsDrainBatchId.New(), Batch("second-only", "orders-b", timestamp));

        var firstDiagnostics = await first.GetDiagnosticsAsync();
        var secondDiagnostics = await second.GetDiagnosticsAsync();

        Assert.Equal((1, 1, 1, 1, 1, 1), Counts(firstDiagnostics));
        Assert.Equal((1, 1, 1, 1, 1, 1), Counts(secondDiagnostics));
        Assert.Equal(["first-new-log"], (await first.QueryLogsAsync(new() { Take = 20 })).Items.Select(log => log.Id));
        Assert.Equal(["second-only-log"], (await second.QueryLogsAsync(new() { Take = 20 })).Items.Select(log => log.Id));
        Assert.Equal("orders-a", Assert.Single((await first.QueryResourcesAsync(new() { Take = 20 })).Items).ServiceName);
        Assert.Equal("orders-b", Assert.Single((await second.QueryResourcesAsync(new() { Take = 20 })).Items).ServiceName);
    }

    private static GroundworkStructuredLogStore StartStructuredLogs(
        GroundworkStructuredLogProviderLease provider,
        StructuredLogStoreBinding binding)
    {
        var store = new GroundworkStructuredLogStore(provider.Store, Options.Create(new StructuredLogsOptions()), binding);
        store.Start();
        return store;
    }

    private static StructuredLogEntry StructuredEntry(long sequence, string message, DateTimeOffset timestamp) => new()
    {
        Sequence = sequence,
        Timestamp = timestamp,
        Level = LogLevel.Information,
        Category = "Scope.Conformance",
        Message = message,
        SourceId = "scope-conformance"
    };

    private static OpenTelemetryBatch Batch(string suffix, string serviceName, DateTimeOffset timestamp)
    {
        var resource = new TelemetryResource("resource", serviceName, null, "dotnet", new Dictionary<string, string?>(), timestamp, TelemetryResourceStatus.Active);
        var trace = new TelemetryTrace($"{suffix}-trace", "span", "request", timestamp, timestamp, TimeSpan.Zero, SpanStatus.Ok, [resource.Id], [], 1);
        var span = new TelemetrySpan($"{suffix}-span-record", trace.TraceId, "span", null, resource.Id, "request", "internal", timestamp, timestamp, SpanStatus.Ok, null, new Dictionary<string, string?>(), [], []);
        var instrument = new MetricInstrument("instrument", resource.Id, "request.duration", "ms", null, MetricKind.Gauge, new Dictionary<string, string?>());
        var point = new MetricPoint($"{suffix}-point", instrument.Id, instrument.Name, resource.Id, timestamp, 1, null, null, new Dictionary<string, string?>(), trace.TraceId, span.SpanId);
        var log = new OtlpLogRecord($"{suffix}-log", resource.Id, timestamp, "Information", null, suffix, trace.TraceId, span.SpanId, new Dictionary<string, string?>());
        return new([resource], [trace], [span], [instrument], [point], [log]);
    }

    private static (int Resources, int Traces, int Spans, int Instruments, int Points, int Logs) Counts(
        OpenTelemetryStorageDiagnostics diagnostics) =>
        (diagnostics.ResourceCount, diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricInstrumentCount,
            diagnostics.MetricPointCount, diagnostics.LogRecordCount);
}
