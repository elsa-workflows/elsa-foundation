using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Entities;
using Elsa.Diagnostics.Persistence.Draining;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Tests;

public sealed class EfOpenTelemetryRetentionTests
{
    // Batch issuance has to sit inside the one-hour replay window around the store's clock, so a test that pins
    // issuance times pins the clock with them.
    private static readonly DateTimeOffset ReplayWindowNow = TelemetryTestData.Now.AddMinutes(30);

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

    [Fact]
    public async Task Signal_retention_deletes_every_row_below_the_boundary_and_keeps_the_boundary_row()
    {
        await using var fixture = await CreateFixtureAsync(new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 3,
            SpanCapacity = 3,
            MetricPointCapacity = 3,
            LogRecordCapacity = 3,
            ResourceCapacity = 10,
            MetricInstrumentCapacity = 10,
            MaxQuerySize = 20
        }, new FixedTimeProvider(ReplayWindowNow));
        var resource = TelemetryTestData.Resource("resource-boundary", "orders");
        var instrument = TelemetryTestData.Instrument("instrument-boundary", resource.Id, "requests");

        // Five rows against a capacity of three, so the retained set has a boundary row (the third newest, the
        // oldest survivor) with two rows strictly below it. Three rows would only prove the count, not the edge.
        for (var index = 1; index <= 5; index++)
            await WriteAllSignalsAsync(fixture, index, resource, instrument);

        await fixture.EfStore.ApplyPendingRetentionAsync();

        Assert.Equal(["trace-3", "trace-4", "trace-5"], await fixture.WithDbAsync(db => db.Traces.OrderBy(x => x.Sequence).Select(x => x.TraceId).ToArrayAsync()));
        Assert.Equal(["span-record-3", "span-record-4", "span-record-5"], await fixture.WithDbAsync(db => db.Spans.OrderBy(x => x.Sequence).Select(x => x.Id).ToArrayAsync()));
        Assert.Equal(["point-3", "point-4", "point-5"], await fixture.WithDbAsync(db => db.MetricPoints.OrderBy(x => x.Sequence).Select(x => x.Id).ToArrayAsync()));
        Assert.Equal(["log-3", "log-4", "log-5"], await fixture.WithDbAsync(db => db.Logs.OrderBy(x => x.Sequence).Select(x => x.Id).ToArrayAsync()));
    }

    [Fact]
    public async Task Catalog_retention_deletes_every_row_below_the_boundary_and_keeps_the_boundary_row()
    {
        await using var fixture = await CreateFixtureAsync(new OpenTelemetryDiagnosticsOptions
        {
            ResourceCapacity = 3,
            MetricInstrumentCapacity = 3,
            MaxQuerySize = 20
        }, new FixedTimeProvider(ReplayWindowNow));

        // Instrument recency is the batch issuance time, resource recency is the observation time, so both
        // orderings are pinned by giving each of the five writes its own second.
        for (var index = 1; index <= 5; index++)
        {
            var time = TelemetryTestData.Now.AddSeconds(index);
            await fixture.EfStore.WriteAsync(
                new DiagnosticsDrainBatchId(Guid.NewGuid(), time),
                new([TelemetryTestData.Resource($"resource-{index}", "orders", time)], [], [],
                    [TelemetryTestData.Instrument($"instrument-{index}", $"resource-{index}", "requests")], [], []));
        }

        await fixture.EfStore.ApplyPendingRetentionAsync();

        Assert.Equal(["resource-3", "resource-4", "resource-5"], await fixture.WithDbAsync(db => db.Resources.OrderBy(x => x.LastSeenTicks).Select(x => x.Id).ToArrayAsync()));
        Assert.Equal(["instrument-3", "instrument-4", "instrument-5"], await fixture.WithDbAsync(db => db.Instruments.OrderBy(x => x.LastSeenTicks).Select(x => x.Id).ToArrayAsync()));
    }

    [Fact]
    public async Task Retention_leaves_a_second_scopes_rows_untouched()
    {
        await using var fixture = await CreateFixtureAsync(new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 1,
            SpanCapacity = 1,
            MetricPointCapacity = 1,
            LogRecordCapacity = 1,
            ResourceCapacity = 1,
            MetricInstrumentCapacity = 1,
            MaxQuerySize = 20
        }, new FixedTimeProvider(ReplayWindowNow));
        var other = new EfOpenTelemetryBinding("other-tenant", "other-scope", "opentelemetry").ScopeKey;
        Assert.NotEqual(EfOpenTelemetryBinding.Default.ScopeKey, other);
        await fixture.WithDbAsync(async db =>
        {
            SeedScope(db, other, rows: 3);
            await db.SaveChangesAsync();
        });

        // A distinct resource and instrument per write, so the catalog trims have something to delete too.
        for (var index = 1; index <= 3; index++)
        {
            var resource = TelemetryTestData.Resource($"resource-{index}", "orders", TelemetryTestData.Now.AddSeconds(index));
            await WriteAllSignalsAsync(fixture, index, resource, TelemetryTestData.Instrument($"instrument-{index}", resource.Id, "requests"));
        }

        await fixture.EfStore.ApplyPendingRetentionAsync();

        // The bound scope is trimmed to its capacity of one in every signal and catalog table. Its own ledger
        // rows are inside the append-idempotency window and stay; the seeded ones are outside it, so only the
        // ScopeKey clause in the ledger delete keeps them.
        Assert.Equal((1, 1, 1, 1, 1, 1, 3), await CountsAsync(fixture, EfOpenTelemetryBinding.Default.ScopeKey));
        Assert.Equal((3, 3, 3, 3, 3, 3, 2), await CountsAsync(fixture, other));
    }

    /// <summary>
    /// One capture, six raw traces over three keys, so a single retention pass overflows by three rows and
    /// affects two keys at once. The recompute reads and deletes per chunk of keys rather than per key, so this
    /// pins what a chunk has to leave behind: the key whose every raw row went is gone summary and all, the
    /// partially retained key is rebuilt from what survived, and the key retention never touched keeps the
    /// memberships the append path gave it.
    /// </summary>
    /// <param name="keyBatchSize">
    /// Null runs at the provider-safe default, where the two affected keys share one chunk. 1 forces the
    /// recompute loop (and the resource search-key lookup inside it) to iterate once per key instead, so the
    /// same assertions also cover state that has to survive across chunk iterations, not just within one.
    /// </param>
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public async Task Trace_retention_recomputes_every_affected_key_and_leaves_an_unaffected_one_alone(int? keyBatchSize)
    {
        await using var fixture = await CreateFixtureAsync(new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 3,
            MaxQuerySize = 20
        }, keyBatchSize: keyBatchSize);
        var resources = SixResources();

        await fixture.Store.WriteAsync(new(resources,
        [
            TraceAt(resources, 1, "trace-a", "workflow-a1"),
            TraceAt(resources, 2, "trace-b", "workflow-b1"),
            TraceAt(resources, 3, "trace-a", "workflow-a2"),
            TraceAt(resources, 4, "trace-c", "workflow-c1"),
            TraceAt(resources, 5, "trace-b", "workflow-b2"),
            TraceAt(resources, 6, "trace-c", "workflow-c2")
        ], [], [], [], []));

        Assert.Equal(3, await fixture.WithDbAsync(db => db.Traces.CountAsync()));
        Assert.Equal(["trace-b", "trace-c"], await SummaryTraceIdsAsync(fixture));
        Assert.Equal(
        [
            "trace-b|Resource|resource-5",
            "trace-b|Service|service-5",
            "trace-b|WorkflowInstance|workflow-b2",
            "trace-c|Resource|resource-4",
            "trace-c|Resource|resource-6",
            "trace-c|Service|service-4",
            "trace-c|Service|service-6",
            "trace-c|WorkflowInstance|workflow-c1",
            "trace-c|WorkflowInstance|workflow-c2"
        ], await MembershipsAsync(fixture));
    }

    /// <summary>
    /// The membership deletes on both the append and the recompute path are set-based and keyed by a chunk of
    /// trace keys, so the foreign rows here are seeded under the very trace keys the second capture rewrites.
    /// A delete that lost its ScopeKey clause would take them with it.
    /// </summary>
    /// <param name="keyBatchSize">
    /// Null runs at the provider-safe default, where all three keys share one chunk in both the merge (append)
    /// loop and the recompute (retention) loop. 1 forces both loops, and the resource search-key lookups inside
    /// them, to iterate once per key, so the second-scope isolation this test pins has to hold across chunks.
    /// </param>
    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public async Task Summary_recompute_and_merge_leave_a_second_scopes_rows_untouched(int? keyBatchSize)
    {
        await using var fixture = await CreateFixtureAsync(new OpenTelemetryDiagnosticsOptions
        {
            TraceCapacity = 3,
            MaxQuerySize = 20
        }, keyBatchSize: keyBatchSize);
        var resources = SixResources();
        var other = new EfOpenTelemetryBinding("other-tenant", "other-scope", "opentelemetry").ScopeKey;
        Assert.NotEqual(EfOpenTelemetryBinding.Default.ScopeKey, other);

        await fixture.Store.WriteAsync(new(resources.Take(3).ToArray(),
        [
            TraceAt(resources, 1, "trace-a", "workflow-a1"),
            TraceAt(resources, 2, "trace-b", "workflow-b1"),
            TraceAt(resources, 3, "trace-c", "workflow-c1")
        ], [], [], [], []));

        // The trace key is a hash the store owns, so the foreign rows borrow the keys the first capture created
        // rather than recomputing them here.
        var keys = await fixture.WithDbAsync(db => db.TraceSummaries.Select(x => x.TraceKey).ToArrayAsync());
        Assert.Equal(3, keys.Length);
        await fixture.WithDbAsync(async db =>
        {
            foreach (var key in keys)
                SeedForeignSummary(db, other, key);
            await db.SaveChangesAsync();
        });

        await fixture.Store.WriteAsync(new(resources.Skip(3).ToArray(),
        [
            TraceAt(resources, 4, "trace-a", "workflow-a2"),
            TraceAt(resources, 5, "trace-b", "workflow-b2"),
            TraceAt(resources, 6, "trace-c", "workflow-c2")
        ], [], [], [], []));

        // Every key went through both converted deletes: the append path merging the second capture in, and the
        // recompute once retention had dropped all three first-capture rows.
        Assert.Equal(["trace-a", "trace-b", "trace-c"], await SummaryTraceIdsAsync(fixture));
        Assert.Equal(
        [
            "trace-a|Resource|resource-4",
            "trace-a|Service|service-4",
            "trace-a|WorkflowInstance|workflow-a2",
            "trace-b|Resource|resource-5",
            "trace-b|Service|service-5",
            "trace-b|WorkflowInstance|workflow-b2",
            "trace-c|Resource|resource-6",
            "trace-c|Service|service-6",
            "trace-c|WorkflowInstance|workflow-c2"
        ], await MembershipsAsync(fixture));
        Assert.Equal((3, 3), await fixture.WithDbAsync(async db => (
            await db.TraceSummaries.CountAsync(x => x.ScopeKey == other),
            await db.TraceSummaryMemberships.CountAsync(x => x.ScopeKey == other))));
    }

    private static TelemetryResource[] SixResources() => Enumerable.Range(1, 6)
        .Select(index => TelemetryTestData.Resource($"resource-{index}", $"service-{index}", TelemetryTestData.Now))
        .ToArray();

    /// <summary>One raw trace record for <paramref name="traceId"/>, carrying resource and second <paramref name="index"/>.</summary>
    private static TelemetryTrace TraceAt(TelemetryResource[] resources, int index, string traceId, string workflowInstanceId) =>
        TelemetryTestData.Trace(traceId, resources[index - 1].Id, TelemetryTestData.Now.AddSeconds(index), SpanStatus.Ok, 1, workflowInstanceId);

    private static Task<string[]> SummaryTraceIdsAsync(OpenTelemetryEntityFrameworkCoreFixture fixture) =>
        fixture.WithDbAsync(db => db.TraceSummaries
            .Where(x => x.ScopeKey == EfOpenTelemetryBinding.Default.ScopeKey)
            .OrderBy(x => x.TraceId)
            .Select(x => x.TraceId)
            .ToArrayAsync());

    /// <summary>
    /// Every membership row of the bound scope, named by the trace id of the summary that owns it. A membership
    /// left behind by a removed summary names itself an orphan instead, so it fails the comparison rather than
    /// disappearing from it.
    /// </summary>
    private static async Task<string[]> MembershipsAsync(OpenTelemetryEntityFrameworkCoreFixture fixture)
    {
        var scopeKey = EfOpenTelemetryBinding.Default.ScopeKey;
        var (summaries, memberships) = await fixture.WithDbAsync(async db => (
            await db.TraceSummaries.Where(x => x.ScopeKey == scopeKey).Select(x => new { x.TraceKey, x.TraceId }).ToArrayAsync(),
            await db.TraceSummaryMemberships.Where(x => x.ScopeKey == scopeKey).Select(x => new { x.TraceKey, x.Kind, x.Value }).ToArrayAsync()));
        var traceIds = summaries.ToDictionary(x => x.TraceKey, x => x.TraceId, StringComparer.Ordinal);
        return memberships
            .Select(x => $"{traceIds.GetValueOrDefault(x.TraceKey, "<orphan>")}|{x.Kind}|{x.Value}")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Seeds a foreign-scope summary and membership under a trace key the bound scope also uses.</summary>
    private static void SeedForeignSummary(OpenTelemetryDbContext db, string scopeKey, string traceKey)
    {
        var ticks = TelemetryTestData.Now.UtcTicks;
        db.TraceSummaries.Add(new OpenTelemetryTraceSummaryEntity
        {
            ScopeKey = scopeKey, TraceKey = traceKey, TraceId = "foreign-trace", TraceIdSearchKey = "foreign-trace",
            StartTimeTicks = ticks, EndTimeTicks = ticks, SpanCount = 1,
            PayloadJson = "{}", ServiceMembershipJson = "[]", WorkflowMembershipJson = "[]", Version = Guid.NewGuid()
        });
        db.TraceSummaryMemberships.Add(new OpenTelemetryTraceSummaryMembershipEntity
        {
            ScopeKey = scopeKey, TraceKey = traceKey, Kind = OpenTelemetryTraceSummaryMembershipKind.Resource,
            Value = "foreign-resource", ValueSearchKey = "foreign-resource", ValueKey = "foreign-resource"
        });
    }

    /// <summary>Writes one capture carrying every signal kind, issued and timestamped at second <paramref name="index"/>.</summary>
    private static ValueTask WriteAllSignalsAsync(
        OpenTelemetryEntityFrameworkCoreFixture fixture,
        int index,
        TelemetryResource resource,
        MetricInstrument instrument)
    {
        var time = TelemetryTestData.Now.AddSeconds(index);
        var trace = TelemetryTestData.Trace($"trace-{index}", resource.Id, time, spanCount: 1);
        return fixture.EfStore.WriteAsync(
            new DiagnosticsDrainBatchId(Guid.NewGuid(), time),
            new([resource], [trace],
                [TelemetryTestData.Span($"span-record-{index}", trace.TraceId, $"span-{index}", resource.Id, time)],
                [instrument],
                [TelemetryTestData.Point($"point-{index}", instrument.Id, resource.Id, time)],
                [TelemetryTestData.Log($"log-{index}", resource.Id, trace.TraceId, index.ToString()) with { Timestamp = time }]));
    }

    private static Task<(int Traces, int Spans, int Points, int Logs, int Resources, int Instruments, int Ledger)> CountsAsync(
        OpenTelemetryEntityFrameworkCoreFixture fixture,
        string scopeKey) =>
        fixture.WithDbAsync(async db => (
            await db.Traces.CountAsync(x => x.ScopeKey == scopeKey),
            await db.Spans.CountAsync(x => x.ScopeKey == scopeKey),
            await db.MetricPoints.CountAsync(x => x.ScopeKey == scopeKey),
            await db.Logs.CountAsync(x => x.ScopeKey == scopeKey),
            await db.Resources.CountAsync(x => x.ScopeKey == scopeKey),
            await db.Instruments.CountAsync(x => x.ScopeKey == scopeKey),
            await db.CaptureLedger.CountAsync(x => x.ScopeKey == scopeKey)));

    /// <summary>
    /// Seeds rows the bound store never writes, so any delete that drops its ScopeKey clause shows up as a loss here.
    /// The search and order keys are opaque to retention, which only ever orders and filters on them, so the
    /// projections are literal rather than recomputed through the (internal) key algorithm.
    /// </summary>
    private static void SeedScope(OpenTelemetryDbContext db, string scopeKey, int rows)
    {
        // Two hours back puts the ledger rows outside the one-hour append-idempotency window, so they are inside
        // the cutoff predicate and only the scope clause keeps them.
        var issuedAt = TelemetryTestData.Now.AddHours(-2);
        for (var index = 0; index < rows; index++)
        {
            var name = $"foreign-{index}";
            var ticks = TelemetryTestData.Now.AddSeconds(index).UtcTicks;
            db.Traces.Add(new OpenTelemetryTraceEntity
            {
                ScopeKey = scopeKey, Sequence = index, Id = name, IdSearchKey = name, IdOrderKey = name,
                TraceId = name, TraceIdSearchKey = name, TraceKey = name, PayloadJson = "{}",
                StartTimeTicks = ticks, EndTimeTicks = ticks, SpanCount = 1
            });
            db.Spans.Add(new OpenTelemetrySpanEntity
            {
                ScopeKey = scopeKey, Sequence = index, Id = name, IdSearchKey = name, IdOrderKey = name,
                TraceId = name, TraceIdSearchKey = name, TraceKey = name,
                SpanId = name, SpanIdSearchKey = name, SpanIdOrderKey = name,
                ResourceId = name, ResourceIdSearchKey = name, Name = name, NameSearchKey = name,
                PayloadJson = "{}", StartTimeTicks = ticks, EndTimeTicks = ticks
            });
            db.MetricPoints.Add(new OpenTelemetryMetricPointEntity
            {
                ScopeKey = scopeKey, Sequence = index, Id = name, IdSearchKey = name, IdOrderKey = name,
                InstrumentId = name, InstrumentIdSearchKey = name, InstrumentName = name, InstrumentNameSearchKey = name,
                ResourceId = name, ResourceIdSearchKey = name, PayloadJson = "{}", TimestampTicks = ticks
            });
            db.Logs.Add(new OpenTelemetryLogEntity
            {
                ScopeKey = scopeKey, Sequence = index, Id = name, IdSearchKey = name, IdOrderKey = name,
                ResourceId = name, ResourceIdSearchKey = name,
                SeverityText = "Information", SeveritySearchKey = "INFORMATION", Body = name, BodySearchKey = name,
                PayloadJson = "{}", TimestampTicks = ticks
            });
            db.Resources.Add(new OpenTelemetryResourceEntity
            {
                ScopeKey = scopeKey, Id = name, IdSearchKey = name, IdOrderKey = name,
                ServiceName = name, ServiceNameSearchKey = name, ServiceNameKey = name,
                LastSeenTicks = ticks, PayloadJson = "{}"
            });
            db.Instruments.Add(new OpenTelemetryMetricInstrumentEntity
            {
                ScopeKey = scopeKey, Id = name, IdSearchKey = name, IdOrderKey = name,
                ResourceId = name, ResourceIdSearchKey = name, Name = name, NameSearchKey = name,
                LastSeenTicks = ticks, PayloadJson = "{}"
            });
            if (index < 2)
                db.CaptureLedger.Add(new OpenTelemetryCaptureLedgerEntity
                {
                    ScopeKey = scopeKey, BatchId = Guid.NewGuid(), Fingerprint = name,
                    IssuedAtTicks = issuedAt.UtcTicks, Status = 1
                });
        }
    }

    private static async Task<OpenTelemetryEntityFrameworkCoreFixture> CreateFixtureAsync(
        OpenTelemetryDiagnosticsOptions options,
        TimeProvider? timeProvider = null,
        int? keyBatchSize = null)
    {
        var fixture = new OpenTelemetryEntityFrameworkCoreFixture();
        await fixture.InitializeAsync(options, timeProvider, keyBatchSize);
        return fixture;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
