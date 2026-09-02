using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Draining;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkV2OpenTelemetryTests
{
    [Fact]
    public void Public_declarations_are_ordinary_scoped_units_with_pre_source_aggregation()
    {
        var units = V2OpenTelemetryStorageSchema.CreateUnits();

        Assert.Equal(8, units.Count);
        Assert.All(units, unit => Assert.Equal(ScopePolicy.Scoped, unit.Scope));
        var traces = Assert.Single(units, unit => unit.Id.Value == V2OpenTelemetryStorageSchema.TraceUnitId);
        Assert.Equal(ColumnGeneration.ProviderSequence, Assert.Single(traces.Columns, column => column.Name == V2OpenTelemetryStorageSchema.Sequence).Generation);
        Assert.Contains(traces.AggregationProfiles, profile => profile.Name == V2OpenTelemetryStorageSchema.TraceProfile);
        Assert.Contains(traces.Columns, column =>
            column.Name == V2OpenTelemetryStorageSchema.TraceKey && column.MaxLength == 64);
        Assert.Contains(traces.Indexes, index =>
            index.Name == "elsa_otel_traces_trace_key" &&
            index.Columns.SequenceEqual([new IndexColumn(V2OpenTelemetryStorageSchema.TraceKey)]));
        Assert.NotNull(traces.RetentionIdempotency);

        var summaries = Assert.Single(units, unit => unit.Id.Value == V2OpenTelemetryStorageSchema.TraceSummaryUnitId);
        Assert.Equal("elsa_otel_trace_summaries_v3", summaries.Name);
        Assert.True(summaries.Concurrency.IsOptimistic);
        Assert.Equal([V2OpenTelemetryStorageSchema.TraceKey], summaries.Key.Columns);
        var workflowIds = Assert.Single(summaries.Columns,
            column => column.Name == V2OpenTelemetryStorageSchema.WorkflowInstanceIds);
        Assert.Equal(PortableCollation.UnicodeOrdinalIgnoreCase, workflowIds.ElementSearchKey!.Collation);
        Assert.Equal(512, workflowIds.ElementSearchKey.MaximumElementCodeUnits);
        var order = Assert.Single(summaries.Indexes,
            index => index.Name == "elsa_otel_trace_summaries_start");
        Assert.Equal(
            [
                new IndexColumn(V2OpenTelemetryStorageSchema.StartTime, SortDirection.Descending),
                new IndexColumn(V2OpenTelemetryStorageSchema.TraceKey)
            ],
            order.Columns);
    }

    [Fact]
    public async Task SQLite_round_trip_uses_ordinary_units_and_declared_trace_source_filter()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database, start: true);
        var store = fixture.Store;

        var resource = new TelemetryResource("resource-1", "orders", null, "dotnet", new Dictionary<string, string?>(), DateTimeOffset.UtcNow, TelemetryResourceStatus.Active);
        var trace = new TelemetryTrace("trace-1", "root-1", "checkout", DateTimeOffset.UtcNow.AddSeconds(-2), DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2), SpanStatus.Error, [resource.Id], ["workflow-1"], 1);
        var span = new TelemetrySpan("span-1", trace.TraceId, "span-1", null, resource.Id, "checkout", "server", trace.StartTime, trace.EndTime, SpanStatus.Error, null, new Dictionary<string, string?>(), [], []);
        await store.WriteAsync(DiagnosticsDrainBatchId.New(), new OpenTelemetryBatch([resource], [trace], [span], [], [], []));

        var result = await store.QueryTracesAsync(new OpenTelemetryTraceFilter { ServiceName = "orders", Status = SpanStatus.Error, Take = 10 });
        var detail = await store.GetTraceAsync(trace.TraceId);

        Assert.Equal([trace.TraceId], result.Items.Select(item => item.TraceId));
        Assert.Single(detail!.Spans);
        Assert.Equal(resource.Id, detail.Resources.Single().Id);
    }

    [Fact]
    public async Task Accepted_capture_marks_the_source_synchronously_and_persists_every_signal_kind()
    {
        using var database = new TemporarySqliteDatabase();
        var registry = new RecordingSourceRegistry();
        await using var fixture = await OpenStoreAsync(database, start: true, sourceRegistry: registry);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-capture", "orders", now);
        var trace = new TelemetryTrace("trace-capture", null, "checkout", now, now, TimeSpan.Zero, SpanStatus.Ok, [resource.Id], [], 1);
        var span = new TelemetrySpan("span-capture-record", trace.TraceId, "span-capture", null, resource.Id, "checkout", "internal", now, now, SpanStatus.Ok, null, new Dictionary<string, string?>(), [], []);
        var instrument = Instrument("instrument-capture", resource.Id, "requests");

        await fixture.Store.WriteAsync(new(
            [resource], [trace], [span], [instrument], [Point("point-capture", instrument, resource.Id, now)], [Log("log-capture", resource.Id, now, "captured")]));

        Assert.Equal([resource.Id], registry.List().Select(item => item.Id));
        await fixture.Store.CompleteDrainingAsync();
        Assert.Single((await fixture.Store.QueryResourcesAsync(new() { Take = 10 })).Items);
        Assert.Single((await fixture.Store.QueryTracesAsync(new() { Take = 10 })).Items);
        Assert.Single((await fixture.Store.GetTraceAsync(trace.TraceId))!.Spans);
        Assert.Single((await fixture.Store.QueryMetricsAsync(new() { Take = 10 })).Points);
        Assert.Single((await fixture.Store.QueryLogsAsync(new() { Take = 10 })).Items);
    }

    [Fact]
    public async Task SQLite_trace_detail_reads_every_span_and_log_beyond_the_query_page_size()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(
            database,
            new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 2 });
        var store = fixture.Store;
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var resource = new TelemetryResource("resource-page", "orders", null, "dotnet", new Dictionary<string, string?>(), now, TelemetryResourceStatus.Active);
        var trace = new TelemetryTrace("trace-page", "root-page", "checkout", now, now.AddSeconds(1), TimeSpan.FromSeconds(1), SpanStatus.Ok, [resource.Id], [], 3);
        var spans = Enumerable.Range(1, 3)
            .Select(index => new TelemetrySpan($"span-record-{index}", trace.TraceId, $"span-{index}", null, resource.Id, "checkout", "server", now.AddTicks(index), now.AddTicks(index + 1), SpanStatus.Ok, null, new Dictionary<string, string?>(), [], []))
            .ToArray();
        var logs = Enumerable.Range(1, 3)
            .Select(index => new OtlpLogRecord($"log-{index}", resource.Id, now.AddTicks(index), "Information", 9, $"log {index}", trace.TraceId, $"span-{index}", new Dictionary<string, string?>()))
            .ToArray();
        await store.WriteAsync(DiagnosticsDrainBatchId.New(), new OpenTelemetryBatch([resource], [trace], spans, [], [], logs));

        var detail = await store.GetTraceAsync(trace.TraceId);

        Assert.Equal(["span-1", "span-2", "span-3"], detail!.Spans.Select(span => span.SpanId));
        Assert.Equal(["log-1", "log-2", "log-3"], detail.Logs.Select(log => log.Id));
    }

    [Fact]
    public async Task SQLite_source_filter_is_applied_before_trace_reduction_and_exact_append_replays()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database);
        var store = fixture.Store;
        var now = DateTimeOffset.UtcNow;
        var firstResource = Resource("resource-a", "api");
        var secondResource = Resource("resource-b", "worker");
        var first = Trace("trace-shared", firstResource, now.AddSeconds(-2));
        var second = Trace("trace-shared", secondResource, now.AddSeconds(-1));
        var batch = new OpenTelemetryBatch([firstResource, secondResource], [first, second], [], [], [], []);
        var batchId = DiagnosticsDrainBatchId.New();

        await store.WriteAsync(batchId, batch);
        await store.WriteAsync(batchId, batch);

        var api = await store.QueryTracesAsync(new OpenTelemetryTraceFilter { ServiceName = "api", Take = 10 });
        var all = await store.QueryTracesAsync(new OpenTelemetryTraceFilter { Take = 10 });

        Assert.Equal(1, api.Items.Single().SpanCount);
        Assert.Equal(2, all.Items.Single().SpanCount);

        static TelemetryResource Resource(string id, string service) =>
            new(id, service, null, "dotnet", new Dictionary<string, string?>(), DateTimeOffset.UtcNow, TelemetryResourceStatus.Active);

        static TelemetryTrace Trace(string id, TelemetryResource resource, DateTimeOffset start) =>
            new(id, null, "operation", start, start.AddSeconds(1), TimeSpan.FromSeconds(1), SpanStatus.Ok, [resource.Id], [], 1);
    }

    [Fact]
    public async Task SQLite_repeated_trace_records_merge_earliest_latest_worst_count_and_workflows()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database);
        var start = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-merge", "orders", start);
        var first = new TelemetryTrace(
            "trace-merge", null, "checkout", start, start.AddMilliseconds(10), TimeSpan.FromMilliseconds(10),
            SpanStatus.Error, [resource.Id], [], 2);
        var second = new TelemetryTrace(
            "trace-merge", null, "checkout", start.AddSeconds(1), start.AddSeconds(1).AddMilliseconds(25),
            TimeSpan.FromMilliseconds(25), SpanStatus.Ok, [resource.Id], ["workflow-a"], 3);

        await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), new([resource], [first], [], [], [], []));
        await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), new([], [second], [], [], [], []));

        var summary = Assert.Single((await fixture.Store.QueryTracesAsync(new() { Take = 10 })).Items);
        Assert.Equal(start, summary.StartTime);
        Assert.Equal(second.EndTime, summary.EndTime);
        Assert.Equal(second.EndTime - start, summary.Duration);
        Assert.Equal(SpanStatus.Error, summary.Status);
        Assert.Equal(5, summary.SpanCount);
        Assert.Equal(["workflow-a"], summary.WorkflowInstanceIds);
    }

    [Fact]
    public async Task SQLite_metric_and_log_service_filters_follow_durable_resource_values()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database);
        var time = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var api = Resource("resource-api", "api", time);
        var worker = Resource("resource-worker", "worker", time);
        var apiInstrument = Instrument("instrument-api", api.Id, "queue.depth");
        var workerInstrument = Instrument("instrument-worker", worker.Id, "queue.depth");
        var batch = new OpenTelemetryBatch(
            [api, worker],
            [],
            [],
            [apiInstrument, workerInstrument],
            [Point("point-api", apiInstrument, api.Id, time), Point("point-worker", workerInstrument, worker.Id, time)],
            [Log("log-api", api.Id, time, "api"), Log("log-worker", worker.Id, time, "worker")]);

        await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), batch);

        Assert.Equal(["point-api"], (await fixture.Store.QueryMetricsAsync(new() { ServiceName = "api", Take = 10 })).Points.Select(point => point.Id));
        Assert.Equal(["log-worker"], (await fixture.Store.QueryLogsAsync(new() { ServiceName = "worker", Take = 10 })).Items.Select(log => log.Id));
    }

    [Fact]
    public async Task SQLite_case_equivalent_instrument_ids_collapse_without_losing_points()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database);
        var time = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-api", "api", time);
        var lower = Instrument("resource-api:request.count:gauge", resource.Id, "request.count");
        var upper = Instrument("RESOURCE-API:REQUEST.COUNT:GAUGE", resource.Id, "REQUEST.COUNT");

        await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), new(
            [resource], [], [], [lower], [Point("point-1", lower, resource.Id, time)], []));
        await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), new(
            [], [], [], [upper], [Point("point-2", upper, resource.Id, time.AddSeconds(1))], []));

        var result = await fixture.Store.QueryMetricsAsync(new() { Take = 10 });
        Assert.Single(result.Instruments);
        Assert.Equal(["point-1", "point-2"], result.Points.Select(point => point.Id));
    }

    [Fact]
    public async Task SQLite_final_retention_keeps_exact_newest_signal_and_catalog_windows()
    {
        using var database = new TemporarySqliteDatabase();
        var options = new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 2,
            SpanCapacity = 2,
            MetricPointCapacity = 2,
            LogRecordCapacity = 2,
            ResourceCapacity = 2,
            MetricInstrumentCapacity = 2,
            MaxQuerySize = 20
        };
        await using var fixture = await OpenStoreAsync(database, options, start: true);
        var first = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        for (var index = 1; index <= 3; index++)
        {
            var time = first.AddSeconds(index);
            var resource = Resource($"resource-{index}", "orders", time);
            var trace = new TelemetryTrace($"trace-{index}", null, "operation", time, time, TimeSpan.Zero, SpanStatus.Ok, [resource.Id], [], 1);
            var span = new TelemetrySpan($"span-record-{index}", trace.TraceId, $"span-{index}", null, resource.Id, "operation", "internal", time, time, SpanStatus.Ok, null, new Dictionary<string, string?>(), [], []);
            var instrument = Instrument($"instrument-{index}", resource.Id, "requests");
            await fixture.Store.WriteAsync(new(
                [resource], [trace], [span], [instrument], [Point($"point-{index}", instrument, resource.Id, time)], [Log($"log-{index}", resource.Id, time, index.ToString())]));
        }

        await fixture.Store.CompleteDrainingAsync();

        var diagnostics = await fixture.Store.GetDiagnosticsAsync();
        Assert.Equal((2, 2, 2, 2, 2, 2),
            (diagnostics.ResourceCount, diagnostics.TraceCount, diagnostics.SpanCount,
                diagnostics.MetricInstrumentCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
        var traces = await fixture.Store.QueryTracesAsync(new() { Take = 20 });
        var metrics = await fixture.Store.QueryMetricsAsync(new() { Take = 20 });
        Assert.Equal(["trace-2", "trace-3"], traces.Items.Select(trace => trace.TraceId));
        Assert.Null(await fixture.Store.GetTraceAsync("trace-1"));
        Assert.Single((await fixture.Store.GetTraceAsync("trace-2"))!.Spans);
        Assert.Single((await fixture.Store.GetTraceAsync("trace-3"))!.Spans);
        Assert.Equal(["point-2", "point-3"], metrics.Points.Select(point => point.Id));
        Assert.Equal(["log-2", "log-3"], (await fixture.Store.QueryLogsAsync(new() { Take = 20 })).Items.Select(log => log.Id));
        Assert.Equal(["resource-3", "resource-2"], (await fixture.Store.QueryResourcesAsync(new() { Take = 20 })).Items.Select(resource => resource.Id));
    }

    [Fact]
    public async Task SQLite_concurrent_identical_batch_writers_converge()
    {
        using var database = new TemporarySqliteDatabase();
        using var connection = new SqliteProviderFactory().Create(database.ConnectionString);
        var options = Options.Create(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 100 });
        await using var first = new GroundworkOpenTelemetryStore(connection, options, V2OpenTelemetryBinding.Default);
        await using var second = new GroundworkOpenTelemetryStore(connection, options, V2OpenTelemetryBinding.Default);
        await using var firstLease = await ((IDiagnosticsPersistenceStartupResource)first).AcquireAsync();
        await using var secondLease = await ((IDiagnosticsPersistenceStartupResource)second).AcquireAsync();
        var now = new DateTimeOffset(2026, 8, 16, 13, 0, 0, TimeSpan.Zero);
        var resource = new TelemetryResource("concurrent-resource", "orders", null, "dotnet", new Dictionary<string, string?>(), now, TelemetryResourceStatus.Active);
        var trace = new TelemetryTrace("concurrent-trace", "concurrent-root", "checkout", now, now.AddSeconds(1), TimeSpan.FromSeconds(1), SpanStatus.Ok, [resource.Id], [], 1);
        var span = new TelemetrySpan("concurrent-span-record", trace.TraceId, "concurrent-span", null, resource.Id, "checkout", "server", now, now.AddSeconds(1), SpanStatus.Ok, null, new Dictionary<string, string?>(), [], []);
        var batch = new OpenTelemetryBatch([resource], [trace], [span], [], [], []);
        var batchId = DiagnosticsDrainBatchId.New();
        using var start = new ManualResetEventSlim();

        var writes = new[] { first, second }.Select(store => Task.Run(async () =>
        {
            start.Wait();
            await store.WriteAsync(batchId, batch);
        })).ToArray();
        start.Set();
        await Task.WhenAll(writes);

        Assert.Equal([trace.TraceId], (await first.QueryTracesAsync(new OpenTelemetryTraceFilter())).Items.Select(item => item.TraceId));
    }

    [Fact]
    public async Task SQLite_queued_capture_survives_restart_and_scope_isolation()
    {
        using var database = new TemporarySqliteDatabase();
        var options = new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 100 };
        var binding = new V2OpenTelemetryBinding("tenant", "scope", "collector");
        var batch = CaptureBatch("restart-trace");
        await using (var first = await OpenStoreAsync(database, options, binding, start: true))
        {
            await first.Store.WriteAsync(batch);
            await first.Store.CompleteDrainingAsync();
        }

        await using (var restarted = await OpenStoreAsync(database, options, binding))
        {
            Assert.Equal(["restart-trace"], (await restarted.Store.QueryTracesAsync(new OpenTelemetryTraceFilter())).Items.Select(item => item.TraceId));
        }

        await using (var foreign = await OpenStoreAsync(
                         database,
                         options,
                         new V2OpenTelemetryBinding("tenant", "other-scope", "collector")))
        {
            Assert.Empty((await foreign.Store.QueryTracesAsync(new OpenTelemetryTraceFilter())).Items);
        }

        static OpenTelemetryBatch CaptureBatch(string traceId)
        {
            var now = DateTimeOffset.UtcNow;
            var resource = new TelemetryResource("restart-resource", "restart-service", null, "dotnet", new Dictionary<string, string?>(), now, TelemetryResourceStatus.Active);
            var trace = new TelemetryTrace(traceId, "restart-root", "restart", now.AddSeconds(-1), now, TimeSpan.FromSeconds(1), SpanStatus.Ok, [resource.Id], [], 1);
            var span = new TelemetrySpan("restart-span", traceId, "restart-span", null, resource.Id, "restart", "server", trace.StartTime, trace.EndTime, SpanStatus.Ok, null, new Dictionary<string, string?>(), [], []);
            return new OpenTelemetryBatch([resource], [trace], [span], [], [], []);
        }
    }

    private static async Task<OpenTelemetryStoreFixture> OpenStoreAsync(
        TemporarySqliteDatabase database,
        OpenTelemetryDiagnosticsOptions? options = null,
        V2OpenTelemetryBinding? binding = null,
        bool start = false,
        IOpenTelemetrySourceRegistry? sourceRegistry = null)
    {
        var connection = new SqliteProviderFactory().Create(database.ConnectionString);
        var store = new GroundworkOpenTelemetryStore(
            connection,
            Options.Create(options ?? new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 100 }),
            binding ?? V2OpenTelemetryBinding.Default,
            sourceRegistry: sourceRegistry);
        IDiagnosticsPersistenceResourceLease? lease = null;
        try
        {
            lease = await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync();
            if (start)
                store.Start();
            return new OpenTelemetryStoreFixture(connection, store, lease);
        }
        catch
        {
            if (lease is not null)
                await lease.DisposeAsync();
            await store.DisposeAsync();
            connection.Dispose();
            throw;
        }
    }

    private static TelemetryResource Resource(string id, string service, DateTimeOffset time) =>
        new(id, service, null, "dotnet", new Dictionary<string, string?>(), time, TelemetryResourceStatus.Active);

    private static MetricInstrument Instrument(string id, string resourceId, string name) =>
        new(id, resourceId, name, null, null, MetricKind.Gauge, new Dictionary<string, string?>());

    private static MetricPoint Point(string id, MetricInstrument instrument, string resourceId, DateTimeOffset time) =>
        new(id, instrument.Id, instrument.Name, resourceId, time, 1, null, null, new Dictionary<string, string?>(), null, null);

    private static OtlpLogRecord Log(string id, string resourceId, DateTimeOffset time, string body) =>
        new(id, resourceId, time, "Information", 9, body, null, null, new Dictionary<string, string?>());

    private sealed class OpenTelemetryStoreFixture(
        IStorageProviderConnection connection,
        GroundworkOpenTelemetryStore store,
        IDiagnosticsPersistenceResourceLease lease) : IAsyncDisposable
    {
        public GroundworkOpenTelemetryStore Store => store;

        public async ValueTask DisposeAsync()
        {
            await lease.DisposeAsync();
            await store.DisposeAsync();
            connection.Dispose();
        }
    }

    private sealed class TemporarySqliteDatabase(string prefix = "elsa-otel-v2") : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={path}";

        public void Dispose()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".schema.lock" })
            {
                var candidate = path + suffix;
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
        }
    }

    private sealed class RecordingSourceRegistry : IOpenTelemetrySourceRegistry
    {
        private readonly Dictionary<string, TelemetryResource> resources = new(StringComparer.OrdinalIgnoreCase);

        public long DroppedCount => 0;

        public void MarkSeen(TelemetryResource resource) => resources[resource.Id] = resource;

        public IReadOnlyCollection<TelemetryResource> List() => resources.Values.ToArray();
    }
}
