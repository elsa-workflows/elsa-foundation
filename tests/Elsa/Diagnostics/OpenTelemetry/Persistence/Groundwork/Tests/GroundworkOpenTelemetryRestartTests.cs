using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

public sealed class GroundworkOpenTelemetryRestartTests : OpenTelemetryRestartContractTests, IAsyncLifetime
{
    private readonly OpenTelemetryGroundworkSqliteFixture _fixture = new();

    protected override async ValueTask<IOpenTelemetryStore> CreateStoreAsync() =>
        await _fixture.CreateStoreAsync();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _fixture.DisposeAsync();
}

public abstract class OpenTelemetryRestartContractTests
{
    [Fact]
    public async Task Exact_immutable_counts_and_unfiltered_logs_survive_store_restart()
    {
        var expected = RestartScenario.Create();
        var beforeRestart = await CreateStoreAsync();

        try
        {
            await beforeRestart.WriteAsync(expected.Batch);
        }
        finally
        {
            await DisposeStoreAsync(beforeRestart);
        }

        var restarted = await CreateStoreAsync();

        try
        {
            var diagnostics = await restarted.GetDiagnosticsAsync();
            Assert.Equal((0, 1, 1, 0, 1, 1),
                (diagnostics.ResourceCount, diagnostics.TraceCount, diagnostics.SpanCount,
                    diagnostics.MetricInstrumentCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));

            var logs = await restarted.QueryLogsAsync(new OpenTelemetryLogFilter { Take = 10 });
            var log = Assert.Single(logs.Items);
            Assert.Equal(expected.Log.Id, log.Id);
            Assert.Equal(expected.Log.Body, log.Body);
            Assert.Equal(expected.Log.Timestamp, log.Timestamp);
            Assert.Equal(expected.Log.SeverityText, log.SeverityText);
            AssertAttributes(expected.Log.Attributes, log.Attributes);

            await Assert.ThrowsAsync<NotSupportedException>(() =>
                restarted.QueryTracesAsync(new OpenTelemetryTraceFilter()).AsTask());
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                restarted.GetTraceAsync(expected.Trace.TraceId).AsTask());
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                restarted.QueryMetricsAsync(new OpenTelemetryMetricFilter()).AsTask());
        }
        finally
        {
            await DisposeStoreAsync(restarted);
        }
    }

    protected abstract ValueTask<IOpenTelemetryStore> CreateStoreAsync();

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
            new([], [Trace], [Span], [], [Point], [Log]);

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
