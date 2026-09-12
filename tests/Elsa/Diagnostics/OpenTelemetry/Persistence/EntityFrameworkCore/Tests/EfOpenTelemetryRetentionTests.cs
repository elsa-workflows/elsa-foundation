using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.Persistence.Draining;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Tests;

public sealed class EfOpenTelemetryRetentionTests
{
    [Fact]
    public async Task Retention_keeps_exact_newest_rows_for_each_signal_and_catalog()
    {
        await using var fixture = await CreateFixtureAsync(new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 2,
            SpanCapacity = 2,
            MetricPointCapacity = 2,
            LogRecordCapacity = 2,
            ResourceCapacity = 2,
            MetricInstrumentCapacity = 2,
            MaxQuerySize = 20
        });

        for (var index = 1; index <= 3; index++)
        {
            var time = TelemetryTestData.Now.AddSeconds(index);
            var resource = TelemetryTestData.Resource($"resource-{index}", "orders", time);
            var trace = TelemetryTestData.Trace($"trace-{index}", resource.Id, time, spanCount: 1);
            var span = TelemetryTestData.Span($"span-record-{index}", trace.TraceId, $"span-{index}", resource.Id, time);
            var instrument = TelemetryTestData.Instrument($"instrument-{index}", resource.Id, "requests");
            await fixture.Store.WriteAsync(new(
                [resource], [trace], [span], [instrument],
                [TelemetryTestData.Point($"point-{index}", instrument.Id, resource.Id, time)],
                [TelemetryTestData.Log($"log-{index}", resource.Id, trace.TraceId, index.ToString()) with { Timestamp = time }]));
        }

        var diagnostics = await fixture.Store.GetDiagnosticsAsync();
        Assert.Equal((2, 2, 2, 2, 2, 2),
            (diagnostics.ResourceCount, diagnostics.TraceCount, diagnostics.SpanCount,
                diagnostics.MetricInstrumentCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
        Assert.Equal(["trace-2", "trace-3"], (await fixture.Store.QueryTracesAsync(new() { Take = 20 })).Items.Select(item => item.TraceId));
        Assert.Null(await fixture.Store.GetTraceAsync("trace-1"));
        Assert.Equal(["point-2", "point-3"], (await fixture.Store.QueryMetricsAsync(new() { Take = 20 })).Points.Select(item => item.Id));
        Assert.Equal(["log-2", "log-3"], (await fixture.Store.QueryLogsAsync(new() { Take = 20 })).Items.Select(item => item.Id));
        Assert.Equal(["resource-3", "resource-2"], (await fixture.Store.QueryResourcesAsync(new() { Take = 20 })).Items.Select(item => item.Id));
        Assert.Equal(["point-3"], (await fixture.Store.QueryMetricsAsync(new() { Take = 1 })).Points.Select(item => item.Id));
        Assert.Equal(["log-3"], (await fixture.Store.QueryLogsAsync(new() { Take = 1 })).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Catalog_retention_uses_the_persisted_ordinal_identity_for_equal_time_ties()
    {
        await using var fixture = await CreateFixtureAsync(new OpenTelemetryDiagnosticsOptions
        {
            ResourceCapacity = 1,
            MetricInstrumentCapacity = 1,
            MaxQuerySize = 10
        });
        var resources = new[]
        {
            TelemetryTestData.Resource("resource-a", "orders"),
            TelemetryTestData.Resource("resource-z", "orders")
        };
        var instruments = new[]
        {
            TelemetryTestData.Instrument("instrument-a", resources[0].Id, "requests-a"),
            TelemetryTestData.Instrument("instrument-z", resources[1].Id, "requests-z")
        };

        await fixture.EfStore.WriteAsync(DiagnosticsDrainBatchId.New(), new(resources, [], [], instruments, [], []));
        await fixture.WithDbAsync(async db =>
        {
            var rows = await db.Instruments.ToListAsync();
            foreach (var row in rows)
                row.LastSeenTicks = TelemetryTestData.Now.UtcTicks;
            await db.SaveChangesAsync();
        });

        await fixture.EfStore.ApplyPendingRetentionAsync();

        Assert.Equal(["resource-z"], (await fixture.Store.QueryResourcesAsync(new() { Take = 10 })).Items.Select(x => x.Id));
        Assert.Equal(["instrument-a"], await fixture.WithDbAsync(db => db.Instruments.Select(x => x.Id).ToArrayAsync()));
    }

    [Fact]
    public async Task Trace_retention_recomputes_a_partially_retained_summary()
    {
        await using var fixture = await CreateFixtureAsync(new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 2,
            MaxQuerySize = 20
        });
        var start = TelemetryTestData.Now;
        var api = TelemetryTestData.Resource("resource-retention-api", "orders-api", start);
        var worker = TelemetryTestData.Resource("resource-retention-worker", "orders-worker", start);
        var unrelated = TelemetryTestData.Resource("resource-retention-other", "other", start);
        var first = TelemetryTestData.Trace("trace-shared", api.Id, start, SpanStatus.Error, 1, "workflow-old");
        var other = TelemetryTestData.Trace("trace-other", unrelated.Id, start.AddSeconds(1), SpanStatus.Ok, 4);
        var latest = new TelemetryTrace("trace-shared", null, "operation", start.AddSeconds(2), start.AddSeconds(2),
            TimeSpan.Zero, SpanStatus.Ok, [api.Id, worker.Id], ["workflow-new"], 2);

        await fixture.Store.WriteAsync(new([api, worker, unrelated], [first], [], [], [], []));
        await fixture.Store.WriteAsync(new([], [other], [], [], [], []));
        await fixture.Store.WriteAsync(new([], [latest], [], [], [], []));

        var shared = (await fixture.Store.GetTraceAsync("TRACE-SHARED"))!.Trace;
        Assert.Equal(latest.StartTime, shared.StartTime);
        Assert.Equal(SpanStatus.Ok, shared.Status);
        Assert.Equal(latest.SpanCount, shared.SpanCount);
        Assert.Equal(["workflow-new"], shared.WorkflowInstanceIds);
        Assert.Equal([latest.TraceId], (await fixture.Store.QueryTracesAsync(new() { ServiceName = "orders-api", Take = 10 })).Items.Select(item => item.TraceId));
        Assert.Equal([latest.TraceId], (await fixture.Store.QueryTracesAsync(new() { ServiceName = "orders-worker", Take = 10 })).Items.Select(item => item.TraceId));
    }

    [Fact]
    public async Task Zero_trace_retention_removes_raw_history_and_its_summary()
    {
        await using var fixture = await CreateFixtureAsync(new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 0,
            MaxQuerySize = 20
        });

        await fixture.Store.WriteAsync(TelemetryTestData.Batch("trace-zero-retention"));

        Assert.Null(await fixture.Store.GetTraceAsync("trace-zero-retention"));
        Assert.Equal(0, (await fixture.Store.GetDiagnosticsAsync()).TraceCount);
    }

    private static async Task<OpenTelemetryEntityFrameworkCoreFixture> CreateFixtureAsync(OpenTelemetryDiagnosticsOptions options)
    {
        var fixture = new OpenTelemetryEntityFrameworkCoreFixture();
        await fixture.InitializeAsync(options);
        return fixture;
    }
}
