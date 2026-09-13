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
    public async Task Instrument_retention_uses_the_stable_batch_issuance_time()
    {
        var now = TelemetryTestData.Now.AddMinutes(30);
        await using var fixture = new OpenTelemetryEntityFrameworkCoreFixture();
        await fixture.InitializeAsync(new OpenTelemetryDiagnosticsOptions
        {
            MetricInstrumentCapacity = 1,
            MaxQuerySize = 10
        }, new FixedTimeProvider(now));
        var newer = TelemetryTestData.Instrument("instrument-z", "resource-z", "requests-z");
        var older = TelemetryTestData.Instrument("instrument-a", "resource-a", "requests-a");
        var newerIssuedAt = now.AddMinutes(-1);

        await fixture.EfStore.WriteAsync(
            new DiagnosticsDrainBatchId(Guid.NewGuid(), newerIssuedAt),
            new([], [], [], [newer], [], []));
        await fixture.EfStore.WriteAsync(
            new DiagnosticsDrainBatchId(Guid.NewGuid(), now.AddMinutes(-2)),
            new([], [], [], [older], [], []));
        await fixture.EfStore.ApplyPendingRetentionAsync();

        var retained = await fixture.WithDbAsync(db => db.Instruments.Select(x => new { x.Id, x.LastSeenTicks }).SingleAsync());
        Assert.Equal((newer.Id, newerIssuedAt.UtcTicks), (retained.Id, retained.LastSeenTicks));
    }

    [Fact]
    public async Task Catalog_upserts_do_not_regress_last_seen_for_out_of_order_observations()
    {
        var now = TelemetryTestData.Now.AddMinutes(30);
        await using var fixture = new OpenTelemetryEntityFrameworkCoreFixture();
        await fixture.InitializeAsync(new OpenTelemetryDiagnosticsOptions
        {
            ResourceCapacity = 10,
            MetricInstrumentCapacity = 10,
            MaxQuerySize = 10
        }, new FixedTimeProvider(now));
        var newestAt = now.AddMinutes(-1);
        var olderAt = now.AddMinutes(-2);
        var oldestAt = now.AddMinutes(-3);
        var newestResource = TelemetryTestData.Resource("resource-shared", "service-newest", newestAt);
        var olderResource = TelemetryTestData.Resource("RESOURCE-SHARED", "service-older", olderAt);
        var oldestResource = TelemetryTestData.Resource("Resource-Shared", "service-oldest", oldestAt);
        var newestInstrument = TelemetryTestData.Instrument("instrument-shared", newestResource.Id, "instrument-newest");
        var olderInstrument = TelemetryTestData.Instrument("INSTRUMENT-SHARED", olderResource.Id, "instrument-older");
        var oldestInstrument = TelemetryTestData.Instrument("Instrument-Shared", oldestResource.Id, "instrument-oldest");
        var oldestTrace = TelemetryTestData.Trace("trace-out-of-order-catalog", oldestResource.Id, oldestAt, spanCount: 1);

        await fixture.EfStore.WriteGroupAsync(
        [
            (new DiagnosticsDrainBatchId(Guid.NewGuid(), newestAt), new([newestResource], [], [], [newestInstrument], [], [])),
            (new DiagnosticsDrainBatchId(Guid.NewGuid(), olderAt), new([olderResource], [], [], [olderInstrument], [], []))
        ]);
        await fixture.EfStore.WriteAsync(
            new DiagnosticsDrainBatchId(Guid.NewGuid(), oldestAt),
            new([oldestResource], [oldestTrace], [], [oldestInstrument], [], []));

        var resource = Assert.Single((await fixture.Store.QueryResourcesAsync(new() { Take = 10 })).Items);
        var instrument = await fixture.WithDbAsync(db => db.Instruments
            .Select(x => new { x.Id, x.Name, x.LastSeenTicks })
            .SingleAsync());
        Assert.Equal((newestResource.Id, newestResource.ServiceName, newestAt), (resource.Id, resource.ServiceName, resource.LastSeen));
        Assert.Equal((newestInstrument.Id, newestInstrument.Name, newestAt.UtcTicks), (instrument.Id, instrument.Name, instrument.LastSeenTicks));
        Assert.Equal([oldestTrace.TraceId], (await fixture.Store.QueryTracesAsync(new() { ServiceName = newestResource.ServiceName, Take = 10 })).Items.Select(x => x.TraceId));
        Assert.Empty((await fixture.Store.QueryTracesAsync(new() { ServiceName = oldestResource.ServiceName, Take = 10 })).Items);
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
    public async Task Trace_summary_recomputation_preserves_service_membership_before_catalog_retention()
    {
        await using var fixture = await CreateFixtureAsync(new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 2,
            ResourceCapacity = 1,
            MaxQuerySize = 20
        });
        var start = TelemetryTestData.Now;
        var retainedResource = TelemetryTestData.Resource("resource-retained-reference", "orders-retained", start);
        var newerCatalogResource = TelemetryTestData.Resource("resource-newer-catalog", "orders-newer", start.AddSeconds(2));
        var first = TelemetryTestData.Trace("trace-shared-retention", retainedResource.Id, start);
        var unrelated = TelemetryTestData.Trace("trace-unrelated-retention", newerCatalogResource.Id, start.AddSeconds(1));
        var latest = TelemetryTestData.Trace("trace-shared-retention", retainedResource.Id, start.AddSeconds(2));

        await fixture.Store.WriteAsync(new([retainedResource], [first], [], [], [], []));
        await fixture.Store.WriteAsync(new([newerCatalogResource], [unrelated, latest], [], [], [], []));

        Assert.Equal([latest.TraceId],
            (await fixture.Store.QueryTracesAsync(new() { ServiceName = retainedResource.ServiceName, Take = 10 }))
            .Items.Select(item => item.TraceId));
        Assert.Equal([newerCatalogResource.Id],
            (await fixture.Store.QueryResourcesAsync(new() { Take = 10 })).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Retention_deletes_large_overflow_in_bounded_batches_and_recomputes_the_summary()
    {
        await using var fixture = await CreateFixtureAsync(new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 2,
            MaxQuerySize = 10
        });
        var resource = TelemetryTestData.Resource("resource-bounded-retention", "orders");
        var traces = Enumerable.Range(0, 602)
            .Select(index => TelemetryTestData.Trace(
                "trace-bounded-retention",
                resource.Id,
                TelemetryTestData.Now.AddTicks(index),
                spanCount: 1))
            .ToArray();

        await fixture.Store.WriteAsync(new([resource], traces, [], [], [], []));

        var summary = (await fixture.Store.GetTraceAsync("trace-bounded-retention"))!.Trace;
        Assert.Equal(2, summary.SpanCount);
        Assert.Equal(2, await fixture.WithDbAsync(db => db.Traces.CountAsync()));
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
