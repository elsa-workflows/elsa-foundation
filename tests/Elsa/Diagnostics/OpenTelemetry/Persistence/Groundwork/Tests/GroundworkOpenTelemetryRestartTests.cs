using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Tests;

public sealed class GroundworkOpenTelemetryRestartTests : OpenTelemetryRestartContractTests
{
    protected override ValueTask<IOpenTelemetryStore> CreateStoreAsync() =>
        ValueTask.FromException<IOpenTelemetryStore>(new XunitException(
            "Expected T013 red: T020 has not implemented or wired GroundworkOpenTelemetryStore."));
}

public abstract class OpenTelemetryRestartContractTests
{
    [Fact]
    public async Task Durable_state_survives_store_restart_for_every_signal_kind()
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
            var resources = await restarted.QueryResourcesAsync(new OpenTelemetryResourceFilter
            {
                ServiceName = expected.Resource.ServiceName,
                Take = 10
            });
            var resource = Assert.Single(resources.Items);
            Assert.Equal(expected.Resource.Id, resource.Id);
            Assert.Equal(expected.Resource.ServiceName, resource.ServiceName);
            Assert.Equal(expected.Resource.ServiceInstanceId, resource.ServiceInstanceId);
            Assert.Equal(expected.Resource.LastSeen, resource.LastSeen);
            AssertAttributes(expected.Resource.Attributes, resource.Attributes);

            var traces = await restarted.QueryTracesAsync(new OpenTelemetryTraceFilter
            {
                TraceId = expected.Trace.TraceId,
                Take = 10
            });
            var trace = Assert.Single(traces.Items);
            Assert.Equal(expected.Trace.TraceId, trace.TraceId);
            Assert.Equal(expected.Trace.RootSpanId, trace.RootSpanId);
            Assert.Equal(expected.Trace.StartTime, trace.StartTime);
            Assert.Equal(expected.Trace.ResourceIds, trace.ResourceIds);
            Assert.Equal(expected.Trace.WorkflowInstanceIds, trace.WorkflowInstanceIds);

            var detail = await restarted.GetTraceAsync(expected.Trace.TraceId);
            Assert.NotNull(detail);
            Assert.Equal(expected.Trace.TraceId, detail.Trace.TraceId);
            Assert.Equal(expected.Resource.Id, Assert.Single(detail.Resources).Id);

            var span = Assert.Single(detail.Spans);
            Assert.Equal(expected.Span.Id, span.Id);
            Assert.Equal(expected.Span.TraceId, span.TraceId);
            Assert.Equal(expected.Span.SpanId, span.SpanId);
            AssertAttributes(expected.Span.Attributes, span.Attributes);

            var expectedEvent = Assert.Single(expected.Span.Events);
            var actualEvent = Assert.Single(span.Events);
            Assert.Equal(expectedEvent.Name, actualEvent.Name);
            Assert.Equal(expectedEvent.Timestamp, actualEvent.Timestamp);
            AssertAttributes(expectedEvent.Attributes, actualEvent.Attributes);

            var expectedLink = Assert.Single(expected.Span.Links);
            var actualLink = Assert.Single(span.Links);
            Assert.Equal(expectedLink.TraceId, actualLink.TraceId);
            Assert.Equal(expectedLink.SpanId, actualLink.SpanId);
            AssertAttributes(expectedLink.Attributes, actualLink.Attributes);

            var metrics = await restarted.QueryMetricsAsync(new OpenTelemetryMetricFilter
            {
                InstrumentName = expected.Instrument.Name,
                Take = 10
            });
            var instrument = Assert.Single(metrics.Instruments);
            Assert.Equal(expected.Instrument.Id, instrument.Id);
            Assert.Equal(expected.Instrument.Name, instrument.Name);
            Assert.Equal(expected.Instrument.Kind, instrument.Kind);
            AssertAttributes(expected.Instrument.Attributes, instrument.Attributes);

            var point = Assert.Single(metrics.Points);
            Assert.Equal(expected.Point.Id, point.Id);
            Assert.Equal(expected.Point.Value, point.Value);
            Assert.Equal(expected.Point.InstrumentId, point.InstrumentId);
            Assert.Equal(expected.Point.Timestamp, point.Timestamp);
            AssertAttributes(expected.Point.Attributes, point.Attributes);
            Assert.Equal(expected.Trace.TraceId, point.TraceId);
            Assert.Equal(expected.Span.SpanId, point.SpanId);

            var logs = await restarted.QueryLogsAsync(new OpenTelemetryLogFilter
            {
                TraceId = expected.Trace.TraceId,
                Severity = expected.Log.SeverityText,
                Take = 10
            });
            var log = Assert.Single(logs.Items);
            Assert.Equal(expected.Log.Id, log.Id);
            Assert.Equal(expected.Log.Body, log.Body);
            Assert.Equal(expected.Log.Timestamp, log.Timestamp);
            Assert.Equal(expected.Log.SeverityText, log.SeverityText);
            AssertAttributes(expected.Log.Attributes, log.Attributes);
            Assert.Equal(expected.Log.Id, Assert.Single(detail.Logs).Id);
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
