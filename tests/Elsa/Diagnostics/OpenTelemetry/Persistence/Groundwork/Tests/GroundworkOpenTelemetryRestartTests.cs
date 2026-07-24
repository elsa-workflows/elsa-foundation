using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Exceptions;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

public sealed class GroundworkOpenTelemetryRestartTests : OpenTelemetryRestartContractTests, IAsyncLifetime
{
    private readonly OpenTelemetryGroundworkSqliteFixture _fixture = new();

    protected override async ValueTask<IOpenTelemetryStore> CreateStoreAsync() =>
        await _fixture.CreateStoreAsync();

    protected override ValueTask WriteBeforeRestartAsync(IOpenTelemetryStore store, OpenTelemetryBatch batch) =>
        ((GroundworkOpenTelemetryStore)store).WriteDurablyAsync(batch);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _fixture.DisposeAsync();
}

public abstract class OpenTelemetryRestartContractTests
{
    [Fact]
    public async Task Exact_catalog_and_immutable_counts_survive_store_restart()
    {
        var expected = RestartScenario.Create();
        var beforeRestart = await CreateStoreAsync();

        try
        {
            await WriteBeforeRestartAsync(beforeRestart, expected.Batch);
        }
        finally
        {
            await DisposeStoreAsync(beforeRestart);
        }

        var restarted = await CreateStoreAsync();

        try
        {
            var diagnostics = await restarted.GetDiagnosticsAsync();
            Assert.Equal((1, 1, 1, 1, 1, 1),
                (diagnostics.ResourceCount, diagnostics.TraceCount, diagnostics.SpanCount,
                    diagnostics.MetricInstrumentCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));

            var logs = await restarted.QueryLogsAsync(new OpenTelemetryLogFilter { Take = 10 });
            var log = Assert.Single(logs.Items);
            Assert.Equal(expected.Log.Id, log.Id);
            Assert.Equal(expected.Log.Body, log.Body);
            Assert.Equal(expected.Log.Timestamp, log.Timestamp);
            Assert.Equal(expected.Log.SeverityText, log.SeverityText);
            AssertAttributes(expected.Log.Attributes, log.Attributes);

            var resources = await restarted.QueryResourcesAsync(new OpenTelemetryResourceFilter { Take = 10 });
            var resource = Assert.Single(resources.Items);
            Assert.Equal(expected.Resource, resource with { Attributes = expected.Resource.Attributes });
            AssertAttributes(expected.Resource.Attributes, resource.Attributes);

            var metrics = await restarted.QueryMetricsAsync(new OpenTelemetryMetricFilter { Take = 10 });
            var instrument = Assert.Single(metrics.Instruments);
            Assert.Equal(expected.Instrument, instrument with { Attributes = expected.Instrument.Attributes });
            AssertAttributes(expected.Instrument.Attributes, instrument.Attributes);
            var point = Assert.Single(metrics.Points);
            Assert.Equal(expected.Point, point with { Attributes = expected.Point.Attributes });
            AssertAttributes(expected.Point.Attributes, point.Attributes);

            var traces = await restarted.QueryTracesAsync(new OpenTelemetryTraceFilter { Take = 10 });
            Assert.Collection(traces.Items, item => Assert.Equal(expected.Trace.TraceId, item.TraceId));
            var detail = await restarted.GetTraceAsync(expected.Trace.TraceId);
            Assert.NotNull(detail);
            Assert.Collection(detail!.Spans, item => Assert.Equal(expected.Span.Id, item.Id));
        }
        finally
        {
            await DisposeStoreAsync(restarted);
        }
    }

    protected abstract ValueTask<IOpenTelemetryStore> CreateStoreAsync();

    protected virtual ValueTask WriteBeforeRestartAsync(IOpenTelemetryStore store, OpenTelemetryBatch batch) =>
        store.WriteAsync(batch);

    private static async ValueTask DisposeStoreAsync(IOpenTelemetryStore store)
    {
        if (store is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (store is IDisposable disposable)
            disposable.Dispose();
    }

    private static void AssertAttributes(
        IDictionary<string, string?> expected,
        IDictionary<string, string?> actual) =>
        Assert.Equal(
            expected.OrderBy(x => x.Key, StringComparer.Ordinal),
            actual.OrderBy(x => x.Key, StringComparer.Ordinal));

    private sealed record RestartScenario(
        TelemetryResource Resource,
        TelemetryTrace Trace,
        TelemetrySpan Span,
        MetricInstrument Instrument,
        MetricPoint Point,
        OtlpLogRecord Log)
    {
        private static DateTimeOffset Timestamp { get; } =
            new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

        public OpenTelemetryBatch Batch =>
            new([Resource], [Trace], [Span], [Instrument], [Point], [Log]);

        public static RestartScenario Create()
        {
            var resource = new TelemetryResource(
                "resource-api",
                "api",
                "api-instance",
                "dotnet",
                new Dictionary<string, string?> { ["deployment.environment"] = "test" },
                Timestamp,
                TelemetryResourceStatus.Active);
            var trace = new TelemetryTrace(
                "trace-1",
                "span-root",
                "trace-orders",
                Timestamp,
                Timestamp.AddMilliseconds(25),
                TimeSpan.FromMilliseconds(25),
                SpanStatus.Ok,
                [resource.Id],
                ["workflow-1"],
                2);
            var span = new TelemetrySpan(
                "span-record-1",
                trace.TraceId,
                "span-1",
                trace.RootSpanId,
                resource.Id,
                "process-order",
                "internal",
                Timestamp,
                Timestamp.AddMilliseconds(10),
                SpanStatus.Ok,
                null,
                new Dictionary<string, string?> { ["http.method"] = "GET" },
                [new TelemetrySpanEvent(
                    "order-accepted",
                    Timestamp.AddMilliseconds(1),
                    new Dictionary<string, string?> { ["event.attr"] = "value" })],
                [new TelemetrySpanLink(
                    "linked-trace",
                    "linked-span",
                    new Dictionary<string, string?> { ["link.attr"] = "value" })]);
            var instrument = new MetricInstrument(
                "instrument-1",
                resource.Id,
                "request.duration",
                "ms",
                "duration",
                MetricKind.Gauge,
                new Dictionary<string, string?> { ["instrument.attr"] = "value" });
            var point = new MetricPoint(
                "point-1",
                instrument.Id,
                instrument.Name,
                resource.Id,
                Timestamp,
                42,
                null,
                null,
                new Dictionary<string, string?> { ["point.attr"] = "value" },
                trace.TraceId,
                span.SpanId);
            var log = new OtlpLogRecord(
                "log-1",
                resource.Id,
                Timestamp,
                "Information",
                null,
                "hello after restart",
                trace.TraceId,
                span.SpanId,
                new Dictionary<string, string?> { ["log.attr"] = "value" });

            return new(resource, trace, span, instrument, point, log);
        }
    }
}
