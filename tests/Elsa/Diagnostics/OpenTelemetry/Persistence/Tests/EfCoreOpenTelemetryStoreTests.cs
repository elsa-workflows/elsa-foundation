using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Tests;

public sealed class EfCoreOpenTelemetryStoreTests
{
    [Fact]
    public async Task WriteAsync_PopulatesSourceRegistrySynchronouslyAndPersistsAllSignalTypes()
    {
        using var context = new OpenTelemetryPersistenceTestContext(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 10 });
        var resource = context.Resource("resource-api", "api");
        var trace = context.Trace("trace-1", resource.Id, workflowInstanceIds: ["workflow-1"]);
        var span = context.Span("span-record-1", trace.TraceId, "span-1", resource.Id);
        var instrument = context.Instrument("instrument-1", resource.Id, "request.duration");
        var point = context.Point("point-1", instrument.Id, resource.Id, traceId: trace.TraceId, spanId: span.SpanId);
        var log = context.Log("log-1", resource.Id, trace.TraceId, body: "hello from otel");

        await context.Store.WriteAsync(new OpenTelemetryBatch([resource], [trace], [span], [instrument], [point], [log]));

        Assert.Equal(resource.Id, Assert.Single(context.SourceRegistry.List()).Id);

        await context.Store.CompleteDrainingAsync();

        var diagnostics = await context.Store.GetDiagnosticsAsync();
        Assert.Equal(1, diagnostics.TraceCount);
        Assert.Equal(1, diagnostics.SpanCount);
        Assert.Equal(1, diagnostics.MetricPointCount);
        Assert.Equal(1, diagnostics.LogRecordCount);

        var resources = await context.Store.QueryResourcesAsync(new OpenTelemetryResourceFilter { ServiceName = "api", Take = 10 });
        Assert.Equal(resource.Id, Assert.Single(resources.Items).Id);

        var traces = await context.Store.QueryTracesAsync(new OpenTelemetryTraceFilter { WorkflowInstanceId = "workflow-1", Take = 10 });
        Assert.Equal(trace.TraceId, Assert.Single(traces.Items).TraceId);

        var detail = await context.Store.GetTraceAsync(trace.TraceId);
        Assert.NotNull(detail);
        Assert.Equal("GET", Assert.Single(Assert.Single(detail!.Spans).Attributes).Value);
        Assert.Equal("event-a", Assert.Single(Assert.Single(detail.Spans).Events).Name);
        Assert.Equal("linked-trace", Assert.Single(Assert.Single(detail.Spans).Links).TraceId);
        Assert.Equal("hello from otel", Assert.Single(detail.Logs).Body);

        var metrics = await context.Store.QueryMetricsAsync(new OpenTelemetryMetricFilter { InstrumentName = "duration", Take = 10 });
        Assert.Equal(instrument.Id, Assert.Single(metrics.Instruments).Id);
        Assert.Equal(point.Id, Assert.Single(metrics.Points).Id);

        var logs = await context.Store.QueryLogsAsync(new OpenTelemetryLogFilter { Search = "hello", Severity = "Information", Take = 10 });
        Assert.Equal(log.Id, Assert.Single(logs.Items).Id);
    }

    [Fact]
    public async Task QueryTracesAsync_WhenTraceIdAppearsInMultipleBatches_ReturnsLatestTrace()
    {
        using var context = new OpenTelemetryPersistenceTestContext(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 10 });
        var resource = context.Resource("resource-api", "api");

        await context.Store.WriteAsync(new OpenTelemetryBatch([resource], [context.Trace("trace-1", resource.Id, context.Now.AddSeconds(1))], [], [], [], []));
        await context.Store.WriteAsync(new OpenTelemetryBatch([], [context.Trace("trace-1", resource.Id, context.Now.AddSeconds(2))], [], [], [], []));

        await context.Store.CompleteDrainingAsync();

        var result = await context.Store.QueryTracesAsync(new OpenTelemetryTraceFilter { Take = 10 });

        var trace = Assert.Single(result.Items);
        Assert.Equal(context.Now.AddSeconds(2), trace.StartTime);
    }

    [Fact]
    public async Task QueryMetricsAndLogs_FilterByServiceNameThroughDurableResources()
    {
        using var context = new OpenTelemetryPersistenceTestContext(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 10 });
        var api = context.Resource("resource-api", "api");
        var worker = context.Resource("resource-worker", "worker");
        var apiInstrument = context.Instrument("instrument-api", api.Id, "queue.depth");
        var workerInstrument = context.Instrument("instrument-worker", worker.Id, "queue.depth");

        await context.Store.WriteAsync(new OpenTelemetryBatch(
            [api, worker],
            [],
            [],
            [apiInstrument, workerInstrument],
            [context.Point("point-api", apiInstrument.Id, api.Id), context.Point("point-worker", workerInstrument.Id, worker.Id)],
            [context.Log("log-api", api.Id, "trace-api"), context.Log("log-worker", worker.Id, "trace-worker")]));

        await context.Store.CompleteDrainingAsync();

        var metrics = await context.Store.QueryMetricsAsync(new OpenTelemetryMetricFilter { ServiceName = "api", Take = 10 });
        var logs = await context.Store.QueryLogsAsync(new OpenTelemetryLogFilter { ServiceName = "worker", Take = 10 });

        Assert.Equal("point-api", Assert.Single(metrics.Points).Id);
        Assert.Equal("log-worker", Assert.Single(logs.Items).Id);
    }

    [Fact]
    public async Task QueryMetricsAsync_WhenInstrumentIdsDifferOnlyByCase_CollapsesLikeInMemoryStore()
    {
        using var context = new OpenTelemetryPersistenceTestContext(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 10 });
        var resource = context.Resource("resource-api", "api");
        var lower = context.Instrument("resource-api:request.count:gauge", resource.Id, "request.count");
        var upper = context.Instrument("RESOURCE-API:REQUEST.COUNT:GAUGE", resource.Id, "REQUEST.COUNT");

        await context.Store.WriteAsync(new OpenTelemetryBatch(
            [resource],
            [],
            [],
            [lower],
            [context.Point("point-1", lower.Id, resource.Id)],
            []));
        await context.Store.WriteAsync(new OpenTelemetryBatch(
            [],
            [],
            [],
            [upper],
            [context.Point("point-2", upper.Id, resource.Id)],
            []));

        await context.Store.CompleteDrainingAsync();

        var result = await context.Store.QueryMetricsAsync(new OpenTelemetryMetricFilter { InstrumentName = "request.count", Take = 10 });

        Assert.Single(result.Instruments);
        Assert.Equal(["point-1", "point-2"], result.Points.Select(x => x.Id));
    }

    [Fact]
    public async Task DrainPrunesHighVolumeSignalsToConfiguredCapacities()
    {
        using var context = new OpenTelemetryPersistenceTestContext(new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 2,
            SpanCapacity = 2,
            MetricPointCapacity = 2,
            LogRecordCapacity = 2,
            ResourceCapacity = 2,
            MaxQuerySize = 10
        }, pruneInterval: 1);

        for (var i = 1; i <= 6; i++)
        {
            var resource = context.Resource($"resource-{i}", "api", context.Now.AddSeconds(i));
            var trace = context.Trace($"trace-{i}", resource.Id, context.Now.AddSeconds(i));
            await context.Store.WriteAsync(new OpenTelemetryBatch(
                [resource],
                [trace],
                [context.Span($"span-record-{i}", trace.TraceId, $"span-{i}", resource.Id)],
                [context.Instrument($"instrument-{i}", resource.Id, "duration")],
                [context.Point($"point-{i}", $"instrument-{i}", resource.Id, context.Now.AddSeconds(i))],
                [context.Log($"log-{i}", resource.Id, trace.TraceId)]));
        }

        await context.Store.CompleteDrainingAsync();

        var diagnostics = await context.Store.GetDiagnosticsAsync();
        Assert.Equal(2, diagnostics.ResourceCount);
        Assert.Equal(2, diagnostics.TraceCount);
        Assert.Equal(2, diagnostics.SpanCount);
        Assert.Equal(2, diagnostics.MetricPointCount);
        Assert.Equal(2, diagnostics.LogRecordCount);

        var traces = await context.Store.QueryTracesAsync(new OpenTelemetryTraceFilter { Take = 10 });
        Assert.Equal(["trace-5", "trace-6"], traces.Items.Select(x => x.TraceId));
    }
}
