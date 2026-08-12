using System.Globalization;
using System.Text;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

/// <summary>
/// Drives the <c>diagnostics-durable-history</c> operation sequence against real Groundwork diagnostics
/// stores over file-backed SQLite, to establish which of the frozen runner's eleven asserted observations
/// actually hold.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a probe and not the frozen workload.</b> The frozen scenario cannot be executed on this
/// stack at all: it requires <c>retainedRecordsPerStream = 100,000</c> retained traces, and
/// <see cref="GroundworkOpenTelemetryStore"/> refuses any trace capacity above
/// <see cref="OpenTelemetryRecordStreamDefinitions.MaxTraceRecordCapacity"/> (5,000) at construction — see
/// <see cref="Frozen_trace_capacity_is_refused_by_the_groundwork_open_telemetry_store"/>, which pins that
/// refusal. Everything except the retained volume is therefore run at its frozen value, and the volume is
/// run at the largest scale Groundwork admits, so a failure here is a real property of the stores rather
/// than an artefact of the reduction.
/// </para>
/// <para>
/// These assertions are copies of the runner's, not relaxations of them. Where the observed behaviour
/// diverges, the divergence is recorded in the assertion message rather than absorbed into a looser bound.
/// </para>
/// <para>
/// <b>If this starts failing on exact counts, suspect the drain budget before the stores.</b> Both stores
/// implement flush as <c>DiagnosticsDrain.StopAsync</c>, which fails every still-queued item once
/// <c>ShutdownDrainTimeout</c> elapses. At the ten-second default this probe failed on retained stream
/// counts, on the resource catalog, and on the bounded log window across three consecutive runs, and none
/// of those failures named the cause. <c>DiagnosticsStoreComposition</c> sizes the budget deliberately.
/// </para>
/// </remarks>
public sealed class DiagnosticsDurableHistoryProbeTests : IAsyncLifetime
{
    /// <summary>The largest per-stream retention Groundwork's trace stream admits, minus the overflow.</summary>
    private const int RetainedRecordsPerStream = 4_000;

    private const int RetentionOverflowRecords = 1_000;
    private const int AppendedPerStream = RetainedRecordsPerStream + RetentionOverflowRecords;

    // Every remaining parameter is the frozen value.
    private static readonly int ConcurrentWriters = DiagnosticsDurableHistoryWorkload.ConcurrentWriters;
    private static readonly int InstrumentCount = DiagnosticsDurableHistoryWorkload.InstrumentCount;
    private static readonly int RecordsPerOtlpBatch = DiagnosticsDurableHistoryWorkload.NormalizedRecordsPerOtlpBatch;
    private static readonly int PayloadBytes = DiagnosticsDurableHistoryWorkload.PayloadBytes;
    private static readonly int QueryLimit = DiagnosticsDurableHistoryWorkload.QueryLimit;
    private static readonly int ResourceCount = DiagnosticsDurableHistoryWorkload.ResourceCount;
    private static readonly int StructuredLogBatchSize = DiagnosticsDurableHistoryWorkload.StructuredLogBatchSize;
    private static readonly DateTimeOffset FixedNowUtc = DiagnosticsDurableHistoryWorkload.FixedNowUtc;

    private const string PrimaryCategory = "Elsa.Benchmarks.Diagnostics.Primary";
    private const string PrimarySourceId = "spec094-primary";
    private const string CrossScopeCategory = "Elsa.Benchmarks.Diagnostics.OtherScope";
    private const string CrossScopeSourceId = "spec094-other-scope";
    private const string CrossScopeServiceName = "spec094-other-scope-service";

    private readonly string _directory = Directory.CreateTempSubdirectory("elsa-diagnostics-probe").FullName;
    private readonly List<DiagnosticsClientLease> _leases = [];

    /// <summary>
    /// A bounded budget rather than <see cref="CancellationToken.None"/>: the runner's own retained-count
    /// traversal exists because a provider can advertise more pages without advancing its cursor, and a
    /// probe that hangs on that reports nothing at all.
    /// </summary>
    private readonly CancellationTokenSource _cancellation = new(TimeSpan.FromMinutes(20));

    private string ConnectionString => $"Data Source={Path.Combine(_directory, "diagnostics.db")}";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var lease in _leases)
            await lease.DisposeAsync();
        _leases.Clear();
        _cancellation.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    /// The blocker. The frozen contract obliges the adapter to size every OpenTelemetry capacity to
    /// <c>retainedRecordsPerStream</c>, and the runner then asserts the inspected trace count equals it. The
    /// Groundwork store refuses the configuration outright, so no adapter can satisfy both.
    /// </summary>
    [Fact]
    public async Task Frozen_trace_capacity_is_refused_by_the_groundwork_open_telemetry_store()
    {
        Assert.True(
            DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream > OpenTelemetryRecordStreamDefinitions.MaxTraceRecordCapacity,
            "This test only means something while the frozen retention exceeds the Groundwork trace ceiling.");

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => DiagnosticsStoreComposition.OpenAsync(
            ConnectionString,
            "tenant-diagnostics-primary",
            "spec094",
            "spec094-diagnostics",
            "structured-logs",
            DiagnosticsStoreComposition.FrozenStructuredLogOptions(),
            DiagnosticsStoreComposition.FrozenOpenTelemetryOptions(),
            _cancellation.Token));

        Assert.Contains("trace retention cannot exceed", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The eleven observations, in the frozen operation order, at the largest admitted scale.
    /// </summary>
    [Fact]
    public async Task Frozen_operation_sequence_holds_at_the_largest_admitted_scale()
    {
        var primary = await OpenAsync("tenant-diagnostics-primary");
        var secondary = await OpenAsync("tenant-diagnostics-secondary");
        var cancellationToken = _cancellation.Token;

        // 1. seed-cross-scope-diagnostic-history
        for (var index = 0; index < StructuredLogBatchSize; index++)
            await secondary.Client.StructuredLogs.AppendAsync(EntryFor(index, CrossScopeCategory, CrossScopeSourceId), cancellationToken);
        await secondary.Client.OpenTelemetry.WriteAsync(
            new OpenTelemetryBatch(
                [ResourceFor(0, CrossScopeServiceName)],
                [TraceFor(0)],
                [SpanFor(0, CrossScopeServiceName)],
                [InstrumentFor(0, CrossScopeServiceName)],
                [MetricPointFor(0, CrossScopeServiceName)],
                [LogRecordFor(0, CrossScopeServiceName)]),
            cancellationToken);
        await FlushAsync(cancellationToken);

        // 2. append-structured-log-batches
        await AppendStructuredLogsAsync(primary.Client.StructuredLogs, cancellationToken);
        await FlushAsync(cancellationToken);
        var highWaterAfterAppend = await primary.Client.StructuredLogs.GetHighWaterMarkAsync(cancellationToken);
        Assert.Equal(AppendedPerStream, highWaterAfterAppend);

        // 3. read-structured-log-recent
        var recent = await primary.Client.StructuredLogs.GetRecentAsync(new() { MaxCount = QueryLimit }, cancellationToken);
        Assert.Equal(QueryLimit, recent.Count);

        // 4. resume-structured-log-history
        var replay = await primary.Client.StructuredLogs.ReadAfterAsync(null, StructuredLogFilter.None, QueryLimit, cancellationToken);
        Assert.Equal(QueryLimit, replay.Entries.Count);
        Assert.NotNull(replay.NextCursor);
        Assert.True(replay.HasMore, "The first resume page reported no continuation despite the stream holding more entries.");

        // 5. reopen-and-read-structured-log-high-water
        var reopenedForHighWater = await OpenAsync("tenant-diagnostics-primary");
        Assert.Equal(highWaterAfterAppend, await reopenedForHighWater.Client.StructuredLogs.GetHighWaterMarkAsync(cancellationToken));

        // 6. append-open-telemetry-batches
        await AppendOpenTelemetryAsync(primary.Client.OpenTelemetry, cancellationToken);
        await FlushAsync(cancellationToken);

        // 7-11. every declared read shape, clamped by the frozen query limit
        var resources = await primary.Client.OpenTelemetry.QueryResourcesAsync(new() { Take = QueryLimit }, cancellationToken);
        Assert.Equal(ResourceCount, resources.Items.Count);

        var traces = await primary.Client.OpenTelemetry.QueryTracesAsync(new() { Take = QueryLimit }, cancellationToken);
        Assert.InRange(traces.Items.Count, 1, QueryLimit);

        var newestTraceId = traces.Items
            .OrderByDescending(item => item.StartTime)
            .ThenBy(item => item.TraceId, StringComparer.Ordinal)
            .First().TraceId;
        var detail = await primary.Client.OpenTelemetry.GetTraceAsync(newestTraceId, cancellationToken);
        Assert.NotNull(detail);
        Assert.Equal(newestTraceId, detail.Trace.TraceId, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(detail.Spans);

        var metrics = await primary.Client.OpenTelemetry.QueryMetricsAsync(new() { Take = QueryLimit }, cancellationToken);
        Assert.Equal(QueryLimit, metrics.Points.Count);
        Assert.NotEmpty(metrics.Instruments);

        var logs = await primary.Client.OpenTelemetry.QueryLogsAsync(new() { Take = QueryLimit }, cancellationToken);
        Assert.Equal(QueryLimit, logs.Items.Count);

        // 12. inspect-exact-stream-counts
        var diagnostics = await primary.Client.OpenTelemetry.GetDiagnosticsAsync(cancellationToken);
        Assert.Equal(
            new[] { RetainedRecordsPerStream, RetainedRecordsPerStream, RetainedRecordsPerStream, RetainedRecordsPerStream },
            new[] { diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount });
        Assert.Equal(0L,
            diagnostics.DroppedTraceCount + diagnostics.DroppedSpanCount +
            diagnostics.DroppedMetricPointCount + diagnostics.DroppedLogRecordCount);
        Assert.Equal(ResourceCount, diagnostics.ResourceCount);
        Assert.Equal(InstrumentCount, diagnostics.MetricInstrumentCount);

        // 13. trim-diagnostic-streams
        await primary.Client.StructuredLogs.TrimAsync(RetainedRecordsPerStream, cancellationToken);
        var retainedAfterTrim = await CountRetainedAsync(primary.Client.StructuredLogs, cancellationToken);
        Assert.Equal(RetainedRecordsPerStream, retainedAfterTrim);
        Assert.Equal(RetentionOverflowRecords, AppendedPerStream - retainedAfterTrim);
        Assert.Equal(highWaterAfterAppend, await primary.Client.StructuredLogs.GetHighWaterMarkAsync(cancellationToken));

        // 14. reopen-and-verify-durable-history
        var reopened = await OpenAsync("tenant-diagnostics-primary");
        Assert.Equal(highWaterAfterAppend, await reopened.Client.StructuredLogs.GetHighWaterMarkAsync(cancellationToken));
        Assert.Equal(RetainedRecordsPerStream, await CountRetainedAsync(reopened.Client.StructuredLogs, cancellationToken));
        var restartDiagnostics = await reopened.Client.OpenTelemetry.GetDiagnosticsAsync(cancellationToken);
        Assert.Equal(
            new[] { diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount, diagnostics.ResourceCount, diagnostics.MetricInstrumentCount },
            new[] { restartDiagnostics.TraceCount, restartDiagnostics.SpanCount, restartDiagnostics.MetricPointCount, restartDiagnostics.LogRecordCount, restartDiagnostics.ResourceCount, restartDiagnostics.MetricInstrumentCount });

        // 15. verify-cross-scope-isolation
        var leakedEntries = await primary.Client.StructuredLogs.GetRecentAsync(
            new() { Category = CrossScopeCategory, MaxCount = QueryLimit }, cancellationToken);
        var leakedResources = await primary.Client.OpenTelemetry.QueryResourcesAsync(
            new() { ServiceName = CrossScopeServiceName, Take = QueryLimit }, cancellationToken);
        var leakedLogs = await primary.Client.OpenTelemetry.QueryLogsAsync(
            new() { ServiceName = CrossScopeServiceName, Take = QueryLimit }, cancellationToken);
        Assert.Equal(0, leakedEntries.Count + leakedResources.Items.Count + leakedLogs.Items.Count);
    }

    // ---- composition ---------------------------------------------------------------------------------

    private async Task<DiagnosticsClientLease> OpenAsync(string tenantId)
    {
        var lease = await DiagnosticsStoreComposition.OpenAsync(
            ConnectionString,
            tenantId,
            "spec094",
            "spec094-diagnostics",
            "structured-logs",
            DiagnosticsStoreComposition.FrozenStructuredLogOptions(),
            DiagnosticsStoreComposition.OpenTelemetryOptionsFor(
                RetainedRecordsPerStream,
                ResourceCount,
                InstrumentCount,
                QueryLimit,
                DiagnosticsStoreComposition.BatchesFor(AppendedPerStream, RecordsPerOtlpBatch)));
        _leases.Add(lease);
        return lease;
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        foreach (var lease in _leases)
            await lease.OpenTelemetry.FlushAsync(cancellationToken);
    }

    // ---- the frozen operation bodies -----------------------------------------------------------------

    private static async Task AppendStructuredLogsAsync(IStructuredLogStore store, CancellationToken cancellationToken)
    {
        var batches = AppendedPerStream / StructuredLogBatchSize;
        await Task.WhenAll(Enumerable.Range(0, ConcurrentWriters).Select(async writer =>
        {
            for (var batch = writer; batch < batches; batch += ConcurrentWriters)
            {
                for (var offset = 0; offset < StructuredLogBatchSize; offset++)
                {
                    var committed = await store.AppendAsync(
                        EntryFor((batch * StructuredLogBatchSize) + offset, PrimaryCategory, PrimarySourceId),
                        cancellationToken);
                    Assert.True(committed.ReplayCursor is { IsValid: true }, "A committed entry carried no valid replay cursor.");
                }
            }
        }));
    }

    private static async Task AppendOpenTelemetryAsync(
        Elsa.Diagnostics.OpenTelemetry.Core.Contracts.IOpenTelemetryStore store,
        CancellationToken cancellationToken)
    {
        var remaining = AppendedPerStream;
        var index = 0;
        var resourcesWritten = 0;
        var instrumentsWritten = 0;
        while (remaining > 0)
        {
            var size = Math.Min(RecordsPerOtlpBatch, remaining);

            // The catalogs are keyed upserts, not record streams: emit each exactly once so their inspected
            // counts are the frozen catalog sizes rather than the record volume.
            var resources = Enumerable.Range(resourcesWritten, Math.Max(0, Math.Min(size, ResourceCount - resourcesWritten)))
                .Select(ordinal => ResourceFor(ordinal, ServiceNameFor(ordinal))).ToArray();
            var instruments = Enumerable.Range(instrumentsWritten, Math.Max(0, Math.Min(size, InstrumentCount - instrumentsWritten)))
                .Select(ordinal => InstrumentFor(ordinal, ServiceNameFor(ordinal))).ToArray();
            resourcesWritten += resources.Length;
            instrumentsWritten += instruments.Length;

            var traces = new List<TelemetryTrace>(size);
            var spans = new List<TelemetrySpan>(size);
            var points = new List<MetricPoint>(size);
            var records = new List<OtlpLogRecord>(size);
            for (var offset = 0; offset < size; offset++, index++)
            {
                var service = ServiceNameFor(index % ResourceCount);
                traces.Add(TraceFor(index));
                spans.Add(SpanFor(index, service));
                points.Add(MetricPointFor(index, service));
                records.Add(LogRecordFor(index, service));
            }

            await store.WriteAsync(new OpenTelemetryBatch(resources, traces, spans, instruments, points, records), cancellationToken);
            remaining -= size;
        }

        Assert.Equal(AppendedPerStream, index);
        Assert.Equal(ResourceCount, resourcesWritten);
        Assert.Equal(InstrumentCount, instrumentsWritten);
    }

    /// <summary>
    /// Counts retained entries by draining the durable tail. <see cref="IStructuredLogStore"/> exposes no
    /// count operation and <c>GetRecentAsync</c> clamps to its own maximum, so the gap-free
    /// <c>ReadAfterAsync</c> traversal is the only contract-legal exact count. Bounded rather than
    /// <c>while(true)</c> so a provider advertising pages without advancing its cursor fails instead of
    /// hanging.
    /// </summary>
    private static async Task<int> CountRetainedAsync(IStructuredLogStore store, CancellationToken cancellationToken)
    {
        var count = 0;
        StructuredLogReplayCursor? cursor = null;
        var maximumPages = (AppendedPerStream / StructuredLogBatchSize) + 2;
        for (var page = 0; page < maximumPages; page++)
        {
            var read = await store.ReadAfterAsync(cursor, StructuredLogFilter.None, StructuredLogBatchSize, cancellationToken);
            count += read.Entries.Count;
            cursor = read.NextCursor;
            if (!read.HasMore || read.NextCursor is null)
                return count;
        }

        throw new InvalidOperationException("The durable structured-log tail did not terminate within the bounded page budget.");
    }

    // ---- frozen record shapes ------------------------------------------------------------------------

    private static string ServiceNameFor(int ordinal) => $"spec094-service-{ordinal % ResourceCount:D4}";

    private static StructuredLogEntry EntryFor(int index, string category, string sourceId) => new()
    {
        Sequence = index + 1,
        Timestamp = FixedNowUtc.AddMilliseconds(index),
        Level = LogLevel.Information,
        Category = category,
        EventId = index % 128,
        EventName = "spec094-diagnostics",
        Message = PayloadFor(index),
        MessageTemplate = "spec094 diagnostics {Index}",
        Properties = [new LogProperty("index", index.ToString(CultureInfo.InvariantCulture))],
        SourceId = sourceId
    };

    private static string PayloadFor(int index)
    {
        var prefix = $"spec094-diagnostics-{index:D8}-";
        return prefix + new string('p', PayloadBytes - Encoding.UTF8.GetByteCount(prefix));
    }

    private static TelemetryResource ResourceFor(int ordinal, string serviceName) => new(
        $"resource-{ordinal:D4}",
        serviceName,
        $"instance-{ordinal:D4}",
        "dotnet",
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["spec094.ordinal"] = ordinal.ToString(CultureInfo.InvariantCulture) },
        FixedNowUtc.AddMilliseconds(ordinal),
        TelemetryResourceStatus.Active);

    private static TelemetryTrace TraceFor(int index) => new(
        TraceIdFor(index),
        SpanIdFor(index),
        $"spec094-trace-{index:D8}",
        FixedNowUtc.AddMilliseconds(index),
        FixedNowUtc.AddMilliseconds(index + 1),
        TimeSpan.FromMilliseconds(1),
        SpanStatus.Ok,
        [$"resource-{index % ResourceCount:D4}"],
        [$"workflow-{index % ResourceCount:D4}"],
        1);

    private static TelemetrySpan SpanFor(int index, string serviceName) => new(
        $"span-row-{index:D8}",
        TraceIdFor(index),
        SpanIdFor(index),
        null,
        $"resource-{index % ResourceCount:D4}",
        $"spec094-span-{index:D8}",
        "Internal",
        FixedNowUtc.AddMilliseconds(index),
        FixedNowUtc.AddMilliseconds(index + 1),
        SpanStatus.Ok,
        null,
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["spec094.service"] = serviceName },
        [],
        []);

    private static MetricInstrument InstrumentFor(int ordinal, string serviceName) => new(
        $"instrument-{ordinal:D4}",
        $"resource-{ordinal % ResourceCount:D4}",
        $"spec094.instrument.{ordinal:D4}",
        "ms",
        "spec094 frozen instrument",
        MetricKind.Gauge,
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["spec094.service"] = serviceName });

    private static MetricPoint MetricPointFor(int index, string serviceName) => new(
        $"point-{index:D8}",
        $"instrument-{index % InstrumentCount:D4}",
        $"spec094.instrument.{index % InstrumentCount:D4}",
        $"resource-{index % ResourceCount:D4}",
        FixedNowUtc.AddMilliseconds(index),
        index % 1_000,
        null,
        null,
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["spec094.service"] = serviceName },
        TraceIdFor(index),
        SpanIdFor(index));

    private static OtlpLogRecord LogRecordFor(int index, string serviceName) => new(
        $"otlp-log-{index:D8}",
        $"resource-{index % ResourceCount:D4}",
        FixedNowUtc.AddMilliseconds(index),
        "Information",
        9,
        PayloadFor(index),
        TraceIdFor(index),
        SpanIdFor(index),
        new Dictionary<string, string?>(StringComparer.Ordinal) { ["spec094.service"] = serviceName });

    private static string TraceIdFor(int index) => $"{index:x32}"[^32..];

    private static string SpanIdFor(int index) => $"{index:x16}"[^16..];
}
