using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Core.Exceptions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

/// <summary>Executable provider-side query contract for the immutable OpenTelemetry streams.</summary>
public sealed class GroundworkOpenTelemetryQueryConformanceTests : IAsyncLifetime
{
    private readonly OpenTelemetryGroundworkSqliteFixture _fixture = new();

    [Fact]
    public async Task Trace_metric_and_log_queries_apply_exact_filters_with_inclusive_ranges_and_stable_ties()
    {
        var store = await _fixture.CreateStoreAsync();
        var time = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-api", "Orders", time);
        var traceA = Trace("trace-a", resource.Id, time, "process order", SpanStatus.Ok);
        var traceB = Trace("trace-b", resource.Id, time, "process payment", SpanStatus.Error);
        var instrument = Instrument("instrument-a", resource.Id, "orders.duration");
        var pointA = Point("point-a", instrument, resource.Id, time, traceA.TraceId);
        var pointB = Point("point-b", instrument, resource.Id, time, traceB.TraceId);
        var logA = Log("log-a", resource.Id, time, traceA.TraceId, "span-a", "Information", "order accepted");
        var logB = Log("log-b", resource.Id, time, traceB.TraceId, "span-b", "Error", "payment failed");

        await store.WriteDurablyAsync(new([resource], [traceA, traceB],
            [Span("span-a-record", traceA.TraceId, "span-a", resource.Id, time), Span("span-b-record", traceB.TraceId, "span-b", resource.Id, time)],
            [instrument], [pointA, pointB], [logA, logB]));

        var traces = await store.QueryTracesAsync(new()
        {
            ResourceId = resource.Id,
            Status = SpanStatus.Ok,
            From = time,
            To = time,
            Search = "ORDER",
            Take = 10
        });
        Assert.Collection(traces.Items, trace => Assert.Equal(traceA.TraceId, trace.TraceId));

        var metrics = await store.QueryMetricsAsync(new()
        {
            ResourceId = resource.Id,
            InstrumentName = "DURATION",
            From = time,
            To = time,
            Take = 10
        });
        Assert.Equal([pointA.Id, pointB.Id], metrics.Points.Select(x => x.Id));
        Assert.Collection(metrics.Instruments, item => Assert.Equal(instrument.Id, item.Id));

        var logs = await store.QueryLogsAsync(new()
        {
            ResourceId = resource.Id,
            TraceId = "B",
            SpanId = "SPAN-B",
            Severity = "err",
            From = time,
            To = time,
            Search = "FAILED",
            Take = 10
        });
        Assert.Collection(logs.Items, item => Assert.Equal(logB.Id, item.Id));
    }

    [Fact]
    public async Task Tied_signal_times_use_the_domain_identity_tie_breakers()
    {
        var store = await _fixture.CreateStoreAsync();
        var time = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-api", "Orders", time);
        var trace = Trace("trace-a", resource.Id, time, "process order", SpanStatus.Ok);
        var instrument = Instrument("instrument-a", resource.Id, "orders.duration");
        var spanA = Span("span-record-a", trace.TraceId, "span-a", resource.Id, time);
        var spanB = Span("span-record-b", trace.TraceId, "span-b", resource.Id, time);
        var pointA = Point("point-a", instrument, resource.Id, time, trace.TraceId);
        var pointB = Point("point-b", instrument, resource.Id, time, trace.TraceId);
        var logA = Log("log-a", resource.Id, time, trace.TraceId, spanA.SpanId, "Information", "first");
        var logB = Log("log-b", resource.Id, time, trace.TraceId, spanB.SpanId, "Information", "second");

        await store.WriteDurablyAsync(new(
            [resource],
            [trace],
            [spanB, spanA],
            [instrument],
            [pointB, pointA],
            [logB, logA]));

        var detail = await store.GetTraceAsync(trace.TraceId);
        var metrics = await store.QueryMetricsAsync(new() { Take = 10 });
        var logs = await store.QueryLogsAsync(new() { Take = 10 });

        Assert.Equal(["span-a", "span-b"], detail!.Spans.Select(x => x.SpanId));
        Assert.Equal(["point-a", "point-b"], metrics.Points.Select(x => x.Id));
        Assert.Equal(["log-a", "log-b"], logs.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Trace_take_selects_the_newest_window_and_returns_it_in_ascending_order()
    {
        var store = await _fixture.CreateStoreAsync();
        var first = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-api", "Orders", first);
        var traces = Enumerable.Range(1, 4)
            .Select(index => Trace(
                $"trace-{index}",
                resource.Id,
                first.AddSeconds(index),
                $"trace {index}",
                SpanStatus.Ok))
            .ToArray();

        await store.WriteDurablyAsync(new([resource], traces, [], [], [], []));

        var result = await store.QueryTracesAsync(new() { Take = 2 });

        Assert.Equal(["trace-3", "trace-4"], result.Items.Select(x => x.TraceId));
    }

    [Fact(Skip = "Blocked by valence-works/Groundwork#130: grouped trace reduction must execute provider-side.")]
    public async Task Repeated_trace_records_merge_to_one_summary_across_durable_batches()
    {
        var store = await _fixture.CreateStoreAsync();
        var start = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-api", "Orders", start);
        var first = new TelemetryTrace(
            "trace-a", null, "process order", start, start.AddMilliseconds(10), TimeSpan.FromMilliseconds(10),
            SpanStatus.Error, [resource.Id], [], 2);
        var second = new TelemetryTrace(
            "trace-a", null, "process order", start.AddSeconds(1), start.AddSeconds(1).AddMilliseconds(25),
            TimeSpan.FromMilliseconds(25), SpanStatus.Ok, [resource.Id], ["workflow-a"], 2);

        await store.WriteDurablyAsync(new([resource], [first], [], [], [], []));
        await store.WriteDurablyAsync(new([], [second], [], [], [], []));

        var summary = Assert.Single((await store.QueryTracesAsync(new() { Take = 10 })).Items);
        Assert.Equal(start, summary.StartTime);
        Assert.Equal(second.EndTime, summary.EndTime);
        Assert.Equal(second.EndTime - start, summary.Duration);
        Assert.Equal(SpanStatus.Error, summary.Status);
        Assert.Equal(4, summary.SpanCount);
        Assert.Equal(["workflow-a"], summary.WorkflowInstanceIds);
    }

    [Fact]
    public async Task Metric_and_log_service_name_filters_follow_the_durable_resource_catalog()
    {
        var store = await _fixture.CreateStoreAsync();
        var time = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var api = Resource("resource-api", "api", time);
        var worker = Resource("resource-worker", "worker", time);
        var apiInstrument = Instrument("instrument-api", api.Id, "queue.depth");
        var workerInstrument = Instrument("instrument-worker", worker.Id, "queue.depth");

        await store.WriteDurablyAsync(new(
            [api, worker],
            [],
            [],
            [apiInstrument, workerInstrument],
            [Point("point-api", apiInstrument, api.Id, time, "trace-api"), Point("point-worker", workerInstrument, worker.Id, time, "trace-worker")],
            [Log("log-api", api.Id, time, "trace-api", "span-api", "Information", "api"), Log("log-worker", worker.Id, time, "trace-worker", "span-worker", "Information", "worker")]));

        var metrics = await store.QueryMetricsAsync(new() { ServiceName = "api", Take = 10 });
        var logs = await store.QueryLogsAsync(new() { ServiceName = "worker", Take = 10 });

        Assert.Equal(["point-api"], metrics.Points.Select(point => point.Id));
        Assert.Equal(["log-worker"], logs.Items.Select(log => log.Id));
    }

    [Fact]
    public async Task Trace_detail_uses_exact_trace_lookup_and_pages_every_related_record()
    {
        var store = await _fixture.CreateStoreAsync();
        var time = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-api", "Orders", time);
        var trace = Trace("trace-a", resource.Id, time, "process order", SpanStatus.Ok);
        var span = Span("span-a-record", trace.TraceId, "span-a", resource.Id, time);
        var log = Log("log-a", resource.Id, time, trace.TraceId, span.SpanId, "Information", "order accepted");

        await store.WriteDurablyAsync(new([resource], [trace], [span], [], [], [log]));

        var detail = await store.GetTraceAsync("TRACE-A");
        Assert.NotNull(detail);
        Assert.Equal(trace.TraceId, detail!.Trace.TraceId);
        Assert.Collection(detail.Spans, item => Assert.Equal(span.Id, item.Id));
        Assert.Collection(detail.Resources, item => Assert.Equal(resource.Id, item.Id));
        Assert.Collection(detail.Logs, item => Assert.Equal(log.Id, item.Id));
    }

    [Fact]
    public async Task Trace_detail_does_not_silently_truncate_at_the_per_query_limit()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(
            providers,
            options: new OpenTelemetryDiagnosticsOptions
            {
                MaxQuerySize = 2,
                ResourceCapacity = 20,
                TraceCapacity = 20,
                SpanCapacity = 20,
                LogRecordCapacity = 20
            });
        var time = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-api", "Orders", time);
        var trace = Trace("trace-a", resource.Id, time, "process order", SpanStatus.Ok);
        var spans = Enumerable.Range(1, 3)
            .Select(index => Span(
                $"span-record-{index}",
                trace.TraceId,
                $"span-{index}",
                resource.Id,
                time.AddTicks(index)))
            .ToArray();
        var logs = Enumerable.Range(1, 3)
            .Select(index => Log(
                $"log-{index}",
                resource.Id,
                time.AddTicks(index),
                trace.TraceId,
                $"span-{index}",
                "Information",
                $"log {index}"))
            .ToArray();

        await store.WriteDurablyAsync(new([resource], [trace], spans, [], [], logs));

        var detail = await store.GetTraceAsync(trace.TraceId);

        Assert.Equal(["span-1", "span-2", "span-3"], detail!.Spans.Select(x => x.SpanId));
        Assert.Equal(["log-1", "log-2", "log-3"], detail.Logs.Select(x => x.Id));
    }

    [Fact]
    public async Task Invalid_ranges_are_rejected_before_provider_io()
    {
        var store = await _fixture.CreateStoreAsync();
        var time = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Assert.ThrowsAsync<ArgumentException>(() => store.QueryTracesAsync(new OpenTelemetryTraceFilter { From = time.AddTicks(1), To = time }, canceled.Token).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.QueryMetricsAsync(new OpenTelemetryMetricFilter { From = time.AddTicks(1), To = time }, canceled.Token).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.QueryLogsAsync(new OpenTelemetryLogFilter { From = time.AddTicks(1), To = time }, canceled.Token).AsTask());
    }

    [Fact]
    public async Task Signal_retention_keeps_the_exact_newest_window_for_every_stream()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = new GroundworkOpenTelemetryStore(
            providers,
            Options.Create(new OpenTelemetryDiagnosticsOptions
            {
                TraceCapacity = 2,
                SpanCapacity = 2,
                MetricPointCapacity = 2,
                LogRecordCapacity = 2,
                ResourceCapacity = 10,
                MaxQuerySize = 20
            }),
            _fixture.Binding);
        var first = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        for (var index = 1; index <= 6; index++)
        {
            var time = first.AddSeconds(index);
            var resource = Resource($"resource-{index}", "Orders", time);
            var trace = Trace($"trace-{index}", resource.Id, time, $"trace {index}", SpanStatus.Ok);
            var instrument = Instrument($"instrument-{index}", resource.Id, "orders.duration");
            await store.WriteDurablyAsync(new(
                [resource],
                [trace],
                [Span($"span-record-{index}", trace.TraceId, $"span-{index}", resource.Id, time)],
                [instrument],
                [Point($"point-{index}", instrument, resource.Id, time, trace.TraceId)],
                [Log($"log-{index}", resource.Id, time, trace.TraceId, $"span-{index}", "Information", $"log {index}")]));
        }

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal((2, 2, 2, 2),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
        Assert.Equal(["trace-5", "trace-6"],
            (await store.QueryTracesAsync(new() { Take = 20 })).Items.Select(x => x.TraceId));
        Assert.Equal(["point-5", "point-6"],
            (await store.QueryMetricsAsync(new() { Take = 20 })).Points.Select(x => x.Id));
        Assert.Equal(["log-5", "log-6"],
            (await store.QueryLogsAsync(new() { Take = 20 })).Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Zero_signal_capacities_remove_every_current_record()
    {
        var providers = await _fixture.CreateProvidersAsync();
        var store = _fixture.CreateStore(
            providers,
            options: new OpenTelemetryDiagnosticsOptions
            {
                TraceCapacity = 0,
                SpanCapacity = 0,
                MetricPointCapacity = 0,
                LogRecordCapacity = 0,
                ResourceCapacity = 10,
                MetricInstrumentCapacity = 10,
                MaxQuerySize = 20
            });
        var time = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-api", "Orders", time);
        var trace = Trace("trace-a", resource.Id, time, "process order", SpanStatus.Ok);
        var instrument = Instrument("instrument-a", resource.Id, "orders.duration");

        await store.WriteDurablyAsync(new(
            [resource],
            [trace],
            [Span("span-record-a", trace.TraceId, "span-a", resource.Id, time)],
            [instrument],
            [Point("point-a", instrument, resource.Id, time, trace.TraceId)],
            [Log("log-a", resource.Id, time, trace.TraceId, "span-a", "Information", "accepted")]));

        var diagnostics = await store.GetDiagnosticsAsync();
        Assert.Equal(
            (0, 0, 0, 0),
            (diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Fact]
    public async Task Oversized_catalog_only_batch_is_rejected_without_mutation()
    {
        var store = await _fixture.CreateStoreAsync();
        var time = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var resources = Enumerable.Range(0, 1_001)
            .Select(index => Resource($"resource-{index}", "Orders", time))
            .ToArray();

        await Assert.ThrowsAsync<OpenTelemetryPersistenceValidationException>(
            () => store.WriteDurablyAsync(new(resources, [], [], [], [], [])).AsTask());

        Assert.Equal(0, (await store.GetDiagnosticsAsync()).ResourceCount);
    }

    [Fact]
    public async Task Resource_and_trace_filters_execute_through_declared_provider_routes()
    {
        var store = await _fixture.CreateStoreAsync();
        var time = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var orders = Resource("resource-orders", "Orders API", time);
        var billing = Resource("resource-billing", "Billing API", time.AddSeconds(1));
        await store.WriteDurablyAsync(new(
            [orders, billing],
            [
                Trace("trace-orders", orders.Id, time, "orders", SpanStatus.Ok),
                Trace("trace-billing", billing.Id, time.AddSeconds(1), "billing", SpanStatus.Ok)
            ],
            [],
            [],
            [],
            []));

        Assert.Equal(
            [orders.Id],
            (await store.QueryResourcesAsync(new() { Search = "ORDERS", Take = 10 })).Items.Select(x => x.Id));
        Assert.Equal(
            [billing.Id],
            (await store.QueryResourcesAsync(new() { Search = "BILLING", Take = 10 })).Items.Select(x => x.Id));
        Assert.Equal(
            [orders.Id],
            (await store.QueryResourcesAsync(new() { ServiceName = "orders api", Take = 10 })).Items.Select(x => x.Id));
        Assert.Equal(
            ["trace-billing"],
            (await store.QueryTracesAsync(new() { ServiceName = "BILLING API", Take = 10 })).Items.Select(x => x.TraceId));
    }

    [Fact]
    public async Task Instrument_name_filters_use_the_name_captured_with_each_historical_point()
    {
        var store = await _fixture.CreateStoreAsync();
        var first = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-api", "Orders", first);
        var oldInstrument = Instrument("instrument-a", resource.Id, "orders.duration");
        var renamedInstrument = oldInstrument with { Name = "orders.latency" };

        await store.WriteDurablyAsync(new(
            [resource],
            [],
            [],
            [oldInstrument],
            [Point("point-old", oldInstrument, resource.Id, first, "trace-a")],
            []));
        await store.WriteDurablyAsync(new(
            [],
            [],
            [],
            [renamedInstrument],
            [Point("point-new", renamedInstrument, resource.Id, first.AddSeconds(1), "trace-b")],
            []));

        var oldName = await store.QueryMetricsAsync(new() { InstrumentName = "DURATION", Take = 10 });
        var newName = await store.QueryMetricsAsync(new() { InstrumentName = "LATENCY", Take = 10 });

        Assert.Equal(["point-old"], oldName.Points.Select(x => x.Id));
        Assert.Equal(["point-new"], newName.Points.Select(x => x.Id));
        Assert.All(oldName.Instruments, instrument => Assert.Equal("orders.latency", instrument.Name));
    }

    [Fact]
    public async Task Case_equivalent_instrument_ids_collapse_while_retaining_both_points()
    {
        var store = await _fixture.CreateStoreAsync();
        var first = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-api", "Orders", first);
        var lower = Instrument("resource-api:request.count:gauge", resource.Id, "request.count");
        var upper = Instrument("RESOURCE-API:REQUEST.COUNT:GAUGE", resource.Id, "REQUEST.COUNT");

        await store.WriteDurablyAsync(new(
            [resource],
            [],
            [],
            [lower],
            [Point("point-1", lower, resource.Id, first, "trace-a")],
            []));
        await store.WriteDurablyAsync(new(
            [],
            [],
            [],
            [upper],
            [Point("point-2", upper, resource.Id, first.AddSeconds(1), "trace-b")],
            []));

        var result = await store.QueryMetricsAsync(new() { InstrumentName = "request.count", Take = 10 });

        Assert.Single(result.Instruments);
        Assert.Equal(["point-1", "point-2"], result.Points.Select(x => x.Id));
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private static TelemetryResource Resource(string id, string service, DateTimeOffset time) =>
        new(id, service, null, "dotnet", new Dictionary<string, string?>(), time, TelemetryResourceStatus.Active);

    private static TelemetryTrace Trace(string id, string resourceId, DateTimeOffset time, string name, SpanStatus status) =>
        new(id, null, name, time, time, TimeSpan.Zero, status, [resourceId], ["workflow-a"], 1);

    private static TelemetrySpan Span(string id, string traceId, string spanId, string resourceId, DateTimeOffset time) =>
        new(id, traceId, spanId, null, resourceId, "operation", "internal", time, time, SpanStatus.Ok, null,
            new Dictionary<string, string?>(), [], []);

    private static MetricInstrument Instrument(string id, string resourceId, string name) =>
        new(id, resourceId, name, "ms", null, MetricKind.Gauge, new Dictionary<string, string?>());

    private static MetricPoint Point(string id, MetricInstrument instrument, string resourceId, DateTimeOffset time, string traceId) =>
        new(id, instrument.Id, instrument.Name, resourceId, time, 1, null, null, new Dictionary<string, string?>(), traceId, null);

    private static OtlpLogRecord Log(string id, string resourceId, DateTimeOffset time, string traceId, string spanId, string severity, string body) =>
        new(id, resourceId, time, severity, null, body, traceId, spanId, new Dictionary<string, string?>());
}
