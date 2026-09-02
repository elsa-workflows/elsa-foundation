using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Draining;
using Groundwork.Kernel;
using Groundwork.Query.Model;
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

        var spans = Assert.Single(units, unit => unit.Id.Value == V2OpenTelemetryStorageSchema.SpanUnitId);
        Assert.Contains(spans.Columns, column =>
            column.Name == V2OpenTelemetryStorageSchema.TraceKey && column.MaxLength == 64 && !column.IsNullable);
        var logs = Assert.Single(units, unit => unit.Id.Value == V2OpenTelemetryStorageSchema.LogUnitId);
        Assert.Contains(logs.Columns, column =>
            column.Name == V2OpenTelemetryStorageSchema.TraceKey && column.MaxLength == 64 && column.IsNullable);

        var summaries = Assert.Single(units, unit => unit.Id.Value == V2OpenTelemetryStorageSchema.TraceSummaryUnitId);
        Assert.Equal("elsa_otel_trace_summaries_v3", summaries.Name);
        Assert.True(summaries.Concurrency.IsOptimistic);
        Assert.Equal([V2OpenTelemetryStorageSchema.TraceKey], summaries.Key.Columns);
        var workflowIds = Assert.Single(summaries.Columns,
            column => column.Name == V2OpenTelemetryStorageSchema.WorkflowInstanceIds);
        Assert.Equal(PortableCollation.UnicodeOrdinalIgnoreCase, workflowIds.ElementSearchKey!.Collation);
        Assert.Equal(512, workflowIds.ElementSearchKey.MaximumElementCodeUnits);
        Assert.Equal(7, SearchKeyProjection.ExpansionFactor(PortableCollation.UnicodeOrdinalIgnoreCase));
        Assert.Contains(summaries.Columns, column =>
            column.Name == V2OpenTelemetryStorageSchema.TraceIdSearchKey && column.MaxLength == 1792 && !column.IsNullable);
        Assert.Contains(summaries.Columns, column =>
            column.Name == V2OpenTelemetryStorageSchema.Name &&
            column.MaxLength == 571 && column.IsNullable);
        Assert.Contains(summaries.Columns, column =>
            column.Name == V2OpenTelemetryStorageSchema.NameSearchKey &&
            column.MaxLength == 3997 && column.IsNullable);
        Assert.Contains(summaries.Columns, column =>
            column.Name == V2OpenTelemetryStorageSchema.ServiceNames && column.Type == PortableType.Json && !column.IsNullable);
        var order = Assert.Single(summaries.Indexes,
            index => index.Name == "elsa_otel_trace_summaries_start");
        Assert.Equal(
            [
                new IndexColumn(V2OpenTelemetryStorageSchema.StartTime, SortDirection.Descending),
                new IndexColumn(V2OpenTelemetryStorageSchema.TraceKey)
            ],
            order.Columns);

        var ledger = Assert.Single(units, unit => unit.Id.Value == V2OpenTelemetryStorageSchema.CaptureLedgerUnitId);
        Assert.Equal("elsa_otel_capture_ledger_v3", ledger.Name);
    }

    [Fact]
    public async Task Trace_summary_accepts_every_declared_cross_provider_search_key_bound()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database, start: true);
        var now = DateTimeOffset.UtcNow;
        var resourceId = new string('r', 512);
        var serviceName = new string('s', 512);
        var traceId = new string('t', 256);
        var traceName = new string('n', 571);
        var resource = Resource(resourceId, serviceName, now);
        var trace = new TelemetryTrace(
            traceId,
            null,
            traceName,
            now,
            now,
            TimeSpan.Zero,
            SpanStatus.Ok,
            [resourceId],
            [],
            1);

        await fixture.Store.WriteAsync(
            DiagnosticsDrainBatchId.New(),
            new OpenTelemetryBatch([resource], [trace], [], [], [], []));

        var result = await fixture.Store.QueryTracesAsync(new()
        {
            TraceId = traceId,
            ResourceId = resourceId,
            ServiceName = serviceName,
            Search = traceName,
            Take = 10
        });

        Assert.Equal(traceId, Assert.Single(result.Items).TraceId);
    }

    [Fact]
    public async Task Trace_summary_refuses_names_beyond_the_cross_provider_search_key_bound()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database, start: true);
        var now = DateTimeOffset.UtcNow;
        var trace = new TelemetryTrace(
            "trace-name-bound",
            null,
            new string('n', 572),
            now,
            now,
            TimeSpan.Zero,
            SpanStatus.Ok,
            ["resource-name-bound"],
            [],
            1);

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await fixture.Store.WriteAsync(
                DiagnosticsDrainBatchId.New(),
                new OpenTelemetryBatch([], [trace], [], [], [], [])));

        Assert.Contains("571-code-unit bound", exception.Message, StringComparison.Ordinal);
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
    public async Task SQLite_trace_summary_filters_after_reduction_and_exact_capture_replays()
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

        Assert.Equal(2, api.Items.Single().SpanCount);
        Assert.Equal(2, all.Items.Single().SpanCount);

        static TelemetryResource Resource(string id, string service) =>
            new(id, service, null, "dotnet", new Dictionary<string, string?>(), DateTimeOffset.UtcNow, TelemetryResourceStatus.Active);

        static TelemetryTrace Trace(string id, TelemetryResource resource, DateTimeOffset start) =>
            new(id, null, "operation", start, start.AddSeconds(1), TimeSpan.FromSeconds(1), SpanStatus.Ok, [resource.Id], [], 1);
    }

    [Fact]
    public async Task SQLite_capture_ledger_refuses_same_batch_identity_with_different_typed_content()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database);
        var batchId = DiagnosticsDrainBatchId.New();

        await fixture.Store.WriteAsync(batchId, CaptureBatch("trace-original"));
        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(async () =>
            await fixture.Store.WriteAsync(batchId, CaptureBatch("trace-conflict")));

        Assert.Contains("batch identity", exception.Message, StringComparison.Ordinal);
        Assert.Equal(
            ["trace-original"],
            (await fixture.Store.QueryTracesAsync(new() { Take = 10 })).Items.Select(trace => trace.TraceId));
    }

    [Fact]
    public async Task SQLite_capture_replay_identity_is_independent_of_mutable_catalog_enrichment()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var original = Resource("resource-replay", "orders-v1", now);
        var updated = Resource(original.Id, "orders-v2", now.AddMinutes(1));
        var trace = new TelemetryTrace(
            "trace-catalog-replay", null, "operation", now, now, TimeSpan.Zero,
            SpanStatus.Ok, [original.Id], [], 1);
        var traceBatch = new OpenTelemetryBatch([], [trace], [], [], [], []);
        var traceBatchId = DiagnosticsDrainBatchId.New();

        await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), new([original], [], [], [], [], []));
        await fixture.Store.WriteAsync(traceBatchId, traceBatch);
        await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), new([updated], [], [], [], [], []));
        await fixture.Store.WriteAsync(traceBatchId, traceBatch);

        Assert.Equal(
            [trace.TraceId],
            (await fixture.Store.QueryTracesAsync(new() { ServiceName = "orders-v1", Take = 10 }))
            .Items.Select(item => item.TraceId));
        Assert.Empty((await fixture.Store.QueryTracesAsync(new() { ServiceName = "orders-v2", Take = 10 })).Items);
    }

    [Fact]
    public async Task SQLite_capture_refuses_corrupt_persisted_summary_metadata_without_partial_append()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database);
        var traceId = "trace-corrupt-summary";
        await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), CaptureBatch(traceId));
        var summaryUnit = Assert.Single(
            V2OpenTelemetryStorageSchema.CreateUnits(),
            unit => unit.Id.Value == V2OpenTelemetryStorageSchema.TraceSummaryUnitId);
        using (var summaries = fixture.Connection.OpenOwnedSession(
                   summaryUnit,
                   StorageAccess.Scoped(V2OpenTelemetryBinding.Default.StorageScope)))
        {
            var traceKey = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(PortableStringComparison.CreateSearchKey(
                    traceId,
                    PortableStringComparisonPolicy.UnicodeOrdinalIgnoreCase))));
            var key = new StorageKey(new Dictionary<string, object?>
            {
                [V2OpenTelemetryStorageSchema.TraceKey] = traceKey
            });
            var existing = summaries.Read(key);
            Assert.NotNull(existing);
            var corrupt = existing!.Values.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            corrupt[V2OpenTelemetryStorageSchema.ServiceNames] = "{not-json";
            Assert.True(summaries.Upsert(new StorageValues(corrupt)).Succeeded);
        }

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), CaptureBatch(traceId)));

        var traceUnit = Assert.Single(
            V2OpenTelemetryStorageSchema.CreateUnits(),
            unit => unit.Id.Value == V2OpenTelemetryStorageSchema.TraceUnitId);
        using var traces = fixture.Connection.OpenOwnedSession(
            traceUnit,
            StorageAccess.Scoped(V2OpenTelemetryBinding.Default.StorageScope));
        var table = new TableId(traceUnit.Name);
        var sequence = new ColumnRef(
            table,
            V2OpenTelemetryStorageSchema.Sequence,
            QueryType.Int64,
            isNullable: false);
        var count = traces.Query(new QueryRequest(
            table,
            Predicate.AlwaysTrue.Instance,
            [new OrderTerm(sequence, OrderDirection.Ascending, NullOrder.Last)],
            Projection.ColumnsOnly(sequence),
            Paging.Keyset(1),
            ResultShape.TotalCount.Instance));
        Assert.Equal(1, count.TotalCount);
    }

    [Fact]
    public async Task SQLite_v3_summary_filters_array_elements_case_insensitively_and_orders_deterministically()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database);
        var start = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var api = Resource("Resource-API", "Orders-API", start);
        var worker = Resource("resource-worker", "orders-worker", start);
        var first = new TelemetryTrace(
            "trace-first", null, "checkout", start, start.AddSeconds(1), TimeSpan.FromSeconds(1),
            SpanStatus.Ok, [api.Id], ["Tenant/WORKFLOW-Alpha-123"], 1);
        var second = new TelemetryTrace(
            "trace-second", null, "checkout", start, start.AddSeconds(1), TimeSpan.FromSeconds(1),
            SpanStatus.Ok, [worker.Id], ["tenant/workflow-beta-456"], 1);

        await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), new([api, worker], [first, second], [], [], [], []));

        var workflow = await fixture.Store.QueryTracesAsync(new() { WorkflowInstanceId = "workflow-alpha", Take = 10 });
        var resource = await fixture.Store.QueryTracesAsync(new() { ResourceId = "resource-api", Take = 10 });
        var service = await fixture.Store.QueryTracesAsync(new() { ServiceName = "orders-api", Take = 10 });
        var firstOrder = (await fixture.Store.QueryTracesAsync(new() { Take = 10 })).Items.Select(item => item.TraceId).ToArray();
        var secondOrder = (await fixture.Store.QueryTracesAsync(new() { Take = 10 })).Items.Select(item => item.TraceId).ToArray();

        Assert.Equal([first.TraceId], workflow.Items.Select(item => item.TraceId));
        Assert.Equal([first.TraceId], resource.Items.Select(item => item.TraceId));
        Assert.Equal([first.TraceId], service.Items.Select(item => item.TraceId));
        Assert.Equal(firstOrder, secondOrder);
    }

    [Fact]
    public async Task SQLite_v3_summary_search_and_detail_use_case_insensitive_canonical_trace_identity()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-unicode", "orders", now);
        var trace = new TelemetryTrace(
            "Trace-ÄBC", null, "Über Checkout", now, now.AddSeconds(1), TimeSpan.FromSeconds(1),
            SpanStatus.Ok, [resource.Id], [], 1);
        var span = new TelemetrySpan(
            "span-record-unicode", "trace-äbc", "span-unicode", null, resource.Id, "checkout", "server",
            now, now.AddSeconds(1), SpanStatus.Ok, null, new Dictionary<string, string?>(), [], []);
        var log = new OtlpLogRecord(
            "log-unicode", resource.Id, now, "Information", 9, "captured", "TRACE-ÄBC", span.SpanId,
            new Dictionary<string, string?>());

        await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), new([resource], [trace], [span], [], [], [log]));

        Assert.Equal([trace.TraceId], (await fixture.Store.QueryTracesAsync(new() { TraceId = "äb", Take = 10 })).Items.Select(item => item.TraceId));
        Assert.Equal([trace.TraceId], (await fixture.Store.QueryTracesAsync(new() { Search = "ÜBER", Take = 10 })).Items.Select(item => item.TraceId));
        var detail = await fixture.Store.GetTraceAsync("TRACE-äbc");
        Assert.Equal([span.SpanId], detail!.Spans.Select(item => item.SpanId));
        Assert.Equal([log.Id], detail.Logs.Select(item => item.Id));
    }

    [Fact]
    public async Task SQLite_v3_summary_preserves_every_service_on_a_multi_resource_trace()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var api = Resource("resource-api", "orders-api", now);
        var worker = Resource("resource-worker", "orders-worker", now);
        var trace = new TelemetryTrace(
            "trace-multi-resource", null, "operation", now, now, TimeSpan.Zero,
            SpanStatus.Ok, [api.Id, worker.Id], [], 1);

        await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), new([api, worker], [trace], [], [], [], []));

        Assert.Equal(
            [trace.TraceId],
            (await fixture.Store.QueryTracesAsync(new() { ServiceName = "ORDERS-WORKER", Take = 10 }))
            .Items.Select(item => item.TraceId));
    }

    [Fact]
    public async Task V3_trace_queries_honor_pre_cancelled_tokens()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.Store.QueryTracesAsync(new(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await fixture.Store.GetTraceAsync("trace", cancellation.Token));
    }

    [Fact]
    public async Task SQLite_v3_summary_refuses_over_bound_element_cardinality_without_partial_capture()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(database);
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-bound", "orders", now);
        var trace = new TelemetryTrace(
            "trace-bound", null, "operation", now, now, TimeSpan.Zero,
            SpanStatus.Ok, [resource.Id],
            Enumerable.Range(0, 5_001)
                .Select(index => $"workflow-{index}").ToArray(),
            1);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), new([resource], [trace], [], [], [], [])));

        Assert.Empty((await fixture.Store.QueryTracesAsync(new() { Take = 10 })).Items);
        Assert.Empty((await fixture.Store.QueryResourcesAsync(new() { Take = 10 })).Items);
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
    public async Task SQLite_trace_retention_recomputes_a_partially_retained_summary()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(
            database,
            new OpenTelemetryDiagnosticsOptions { TraceCapacity = 2, MaxQuerySize = 20 },
            start: true);
        var start = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var api = Resource("resource-retention-api", "orders-api", start);
        var worker = Resource("resource-retention-worker", "orders-worker", start);
        var unrelated = Resource("resource-retention-other", "other", start);
        var first = new TelemetryTrace(
            "trace-shared", null, "operation", start, start, TimeSpan.Zero,
            SpanStatus.Error, [api.Id], ["workflow-old"], 1);
        var other = new TelemetryTrace(
            "trace-other", null, "operation", start.AddSeconds(1), start.AddSeconds(1), TimeSpan.Zero,
            SpanStatus.Ok, [unrelated.Id], [], 4);
        var latest = new TelemetryTrace(
            "trace-shared", null, "operation", start.AddSeconds(2), start.AddSeconds(2), TimeSpan.Zero,
            SpanStatus.Ok, [api.Id, worker.Id], ["workflow-new"], 2);

        await fixture.Store.WriteAsync(new([api, worker, unrelated], [first], [], [], [], []));
        await fixture.Store.WriteAsync(new([], [other], [], [], [], []));
        await fixture.Store.WriteAsync(new([], [latest], [], [], [], []));
        await fixture.Store.CompleteDrainingAsync();

        var shared = (await fixture.Store.GetTraceAsync("TRACE-SHARED"))!.Trace;
        Assert.Equal(latest.StartTime, shared.StartTime);
        Assert.Equal(SpanStatus.Ok, shared.Status);
        Assert.Equal(2, shared.SpanCount);
        Assert.Equal(["workflow-new"], shared.WorkflowInstanceIds);
        Assert.Equal(
            [latest.TraceId],
            (await fixture.Store.QueryTracesAsync(new() { ServiceName = "orders-api", Take = 10 }))
            .Items.Select(trace => trace.TraceId));
        Assert.Equal(
            [latest.TraceId],
            (await fixture.Store.QueryTracesAsync(new() { ServiceName = "orders-worker", Take = 10 }))
            .Items.Select(trace => trace.TraceId));
    }

    [Fact]
    public async Task SQLite_zero_trace_retention_removes_raw_history_and_its_summary()
    {
        using var database = new TemporarySqliteDatabase();
        await using var fixture = await OpenStoreAsync(
            database,
            new OpenTelemetryDiagnosticsOptions { TraceCapacity = 0, MaxQuerySize = 20 },
            start: true);

        await fixture.Store.WriteAsync(DiagnosticsDrainBatchId.New(), CaptureBatch("trace-zero-retention"));
        await fixture.Store.CompleteDrainingAsync();

        Assert.Null(await fixture.Store.GetTraceAsync("trace-zero-retention"));
        Assert.Equal(0, (await fixture.Store.GetDiagnosticsAsync()).TraceCount);
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

    [Theory]
    [InlineData(CommitFault.BeforeCommit)]
    [InlineData(CommitFault.AfterCommit)]
    public async Task SQLite_atomic_capture_retries_rollback_and_acknowledgement_loss_without_duplicates(
        CommitFault fault)
    {
        using var database = new TemporarySqliteDatabase();
        using var inner = new SqliteProviderFactory().Create(database.ConnectionString);
        using var connection = new FaultInjectingConnection(inner, fault);
        await using var store = new GroundworkOpenTelemetryStore(
            connection,
            Options.Create(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 100 }),
            V2OpenTelemetryBinding.Default);
        await using var lease = await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync();
        var batch = CaptureBatch("fault-trace");

        await store.WriteAsync(DiagnosticsDrainBatchId.New(), batch);

        var trace = Assert.Single((await store.QueryTracesAsync(new() { Take = 10 })).Items);
        Assert.Equal("fault-trace", trace.TraceId);
        Assert.Equal(1, trace.SpanCount);
        Assert.Equal(1, (await store.GetDiagnosticsAsync()).TraceCount);
    }

    [Fact]
    public async Task SQLite_retention_acknowledgement_loss_replays_one_stable_operation()
    {
        using var database = new TemporarySqliteDatabase();
        using var inner = new SqliteProviderFactory().Create(database.ConnectionString);
        using var connection = new FaultInjectingConnection(
            inner,
            CommitFault.AfterCommit,
            faultAtCommit: 3,
            recordRetentionOperations: true);
        await using var store = new GroundworkOpenTelemetryStore(
            connection,
            Options.Create(new OpenTelemetryDiagnosticsOptions
            {
                TraceCapacity = 1,
                MaxQuerySize = 100
            }),
            V2OpenTelemetryBinding.Default);
        await using var lease = await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync();
        store.Start();

        await store.WriteAsync(DiagnosticsDrainBatchId.New(), CaptureBatch("retention-old"));
        await store.WriteAsync(DiagnosticsDrainBatchId.New(), CaptureBatch("retention-new"));
        await store.CompleteDrainingAsync();

        Assert.True(connection.RetentionOperations.Count >= 2);
        Assert.All(
            connection.RetentionOperations,
            operation => Assert.Equal(connection.RetentionOperations[0], operation));
        Assert.Null(await store.GetTraceAsync("retention-old"));
        Assert.Equal("retention-new", Assert.Single((await store.QueryTracesAsync(new() { Take = 10 })).Items).TraceId);
        Assert.Equal(1, (await store.GetDiagnosticsAsync()).TraceCount);
    }

    [Fact]
    public async Task Readiness_refuses_before_schema_or_drain_when_a_required_capability_is_missing()
    {
        using var database = new TemporarySqliteDatabase();
        using var inner = new SqliteProviderFactory().Create(database.ConnectionString);
        using var connection = new FaultInjectingConnection(
            inner,
            fault: null,
            hiddenCapability: BatchWriteCapabilities.ExactRetentionAffectedKeys);
        await using var store = new GroundworkOpenTelemetryStore(
            connection,
            Options.Create(new OpenTelemetryDiagnosticsOptions()),
            V2OpenTelemetryBinding.Default);

        var exception = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await ((IDiagnosticsPersistenceStartupResource)store).AcquireAsync());

        Assert.Contains(BatchWriteCapabilities.ExactRetentionAffectedKeys.Value, exception.Message, StringComparison.Ordinal);
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

    private static OpenTelemetryBatch CaptureBatch(string traceId)
    {
        var now = DateTimeOffset.UtcNow;
        var resource = Resource($"{traceId}-resource", "orders", now);
        var trace = new TelemetryTrace(
            traceId, null, "operation", now, now, TimeSpan.Zero,
            SpanStatus.Ok, [resource.Id], [], 1);
        return new([resource], [trace], [], [], [], []);
    }

    public enum CommitFault
    {
        BeforeCommit,
        AfterCommit
    }

    private sealed class FaultInjectingConnection(
        IStorageProviderConnection inner,
        CommitFault? fault,
        CapabilityId? hiddenCapability = null,
        int faultAtCommit = 1,
        bool recordRetentionOperations = false) : IStorageProviderConnection
    {
        private readonly List<OperationId> retentionOperations = [];
        private int commitCount;
        private CommitFault? Fault { get; } = fault;
        private int FaultAtCommit { get; } = faultAtCommit;
        private bool RecordRetentionOperations { get; } = recordRetentionOperations;

        public IReadOnlyList<OperationId> RetentionOperations
        {
            get
            {
                lock (retentionOperations)
                    return retentionOperations.ToArray();
            }
        }

        public IProviderCatalog Catalog => inner.Catalog;
        public ISchemaCoordinator Schema => inner.Schema;
        public IReadOnlyList<CapabilityDescriptor> Capabilities => inner.Capabilities
            .Where(capability => capability.Id != hiddenCapability)
            .ToArray();

        public IStorageSession OpenSession(
            StorageUnit unit,
            StorageAccess access,
            IProviderCommandObserver? observer = null) =>
            inner.OpenSession(unit, access, observer);

        public IOwnedStorageSession OpenOwnedSession(
            StorageUnit unit,
            StorageAccess access,
            IProviderCommandObserver? observer = null) =>
            inner.OpenOwnedSession(unit, access, observer);

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, params StorageUnit[] units) =>
            Wrap(inner.BeginUnitOfWork(access, units));

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            params StorageUnit[] units) =>
            Wrap(inner.BeginUnitOfWork(access, options, units));

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IProviderCommandObserver? observer,
            params StorageUnit[] units) =>
            Wrap(inner.BeginUnitOfWork(access, options, observer, units));

        public void Dispose() { }

        private IUnitOfWork Wrap(IUnitOfWork work) => new FaultInjectingUnitOfWork(this, work);

        private sealed class FaultInjectingUnitOfWork(
            FaultInjectingConnection owner,
            IUnitOfWork inner) : IUnitOfWork
        {
            public IStorageSession OpenSession(StorageUnit unit)
            {
                var session = inner.OpenSession(unit);
                return owner.RecordRetentionOperations && unit.Id.Value == V2OpenTelemetryStorageSchema.TraceUnitId
                    ? new RecordingRetentionSession(owner, session)
                    : session;
            }
            public void Stage(RowWrite write) => inner.Stage(write);
            public BatchWriteSummary Commit() => inner.Commit();
            public BatchWriteReport CommitWithOutcomes() => inner.CommitWithOutcomes();

            public async ValueTask<BatchWriteReport> CommitWithOutcomesAsync(
                CancellationToken cancellationToken = default)
            {
                if (owner.Fault is null)
                    return await inner.CommitWithOutcomesAsync(cancellationToken);
                var currentCommit = Interlocked.Increment(ref owner.commitCount);
                if (currentCommit == owner.FaultAtCommit && owner.Fault == CommitFault.BeforeCommit)
                {
                    inner.Rollback();
                    throw new IOException("Injected rollback before commit.");
                }

                var report = await inner.CommitWithOutcomesAsync(cancellationToken);
                if (currentCommit == owner.FaultAtCommit)
                    throw new IOException("Injected acknowledgement loss after commit.");
                return report;
            }

            public ValueTask<BatchWriteSummary> CommitAsync(CancellationToken cancellationToken = default) =>
                inner.CommitAsync(cancellationToken);

            public void Rollback() => inner.Rollback();
            public void Dispose() => inner.Dispose();
        }

        private sealed class RecordingRetentionSession(
            FaultInjectingConnection owner,
            IStorageSession inner) :
            IStorageSession,
            IExactAppendStorageSession,
            IExactRetentionStorageSession,
            IExactRetentionAffectedKeysStorageSession
        {
            public StorageUnit Unit => inner.Unit;
            public StorageAccess Access => inner.Access;
            public StoredEntry? Read(StorageKey key) => inner.Read(key);
            public ValueTask<StoredEntry?> ReadAsync(
                StorageKey key,
                CancellationToken cancellationToken = default) => inner.ReadAsync(key, cancellationToken);
            public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) =>
                inner.Query(request, options);
            public ValueTask<QueryMaterializedResult> QueryAsync(
                QueryRequest request,
                QueryRenderOptions? options = null,
                CancellationToken cancellationToken = default) => inner.QueryAsync(request, options, cancellationToken);
            public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
            public ValueTask<AggregationResult> AggregateAsync(
                AggregationQuery query,
                CancellationToken cancellationToken = default) => inner.AggregateAsync(query, cancellationToken);
            public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
            public ValueTask<WriteOutcome> InsertAsync(
                StorageValues values,
                WriteOptions? options = null,
                CancellationToken cancellationToken = default) => inner.InsertAsync(values, options, cancellationToken);
            public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
            public ValueTask<WriteOutcome> UpdateAsync(
                StorageValues values,
                WriteOptions? options = null,
                CancellationToken cancellationToken = default) => inner.UpdateAsync(values, options, cancellationToken);
            public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
            public ValueTask<WriteOutcome> UpsertAsync(
                StorageValues values,
                WriteOptions? options = null,
                CancellationToken cancellationToken = default) => inner.UpsertAsync(values, options, cancellationToken);
            public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
            public ValueTask<WriteOutcome> DeleteAsync(
                StorageKey key,
                WriteOptions? options = null,
                CancellationToken cancellationToken = default) => inner.DeleteAsync(key, options, cancellationToken);
            public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) =>
                inner.Append(operationId, values);
            public ValueTask<WriteOutcome> AppendAsync(
                OperationId operationId,
                IReadOnlyList<StorageValues> values,
                CancellationToken cancellationToken = default) => inner.AppendAsync(operationId, values, cancellationToken);

            public AppendOutcomeReport AppendWithOutcomes(
                OperationId operationId,
                IReadOnlyList<StorageValues> values) =>
                ((IExactAppendStorageSession)inner).AppendWithOutcomes(operationId, values);

            public ValueTask<AppendOutcomeReport> AppendWithOutcomesAsync(
                OperationId operationId,
                IReadOnlyList<StorageValues> values,
                CancellationToken cancellationToken = default) =>
                ((IExactAppendStorageSession)inner).AppendWithOutcomesAsync(operationId, values, cancellationToken);

            public RetentionOperationResult ApplyRetention(
                OperationId operationId,
                RetentionExecutionOptions? options = null)
            {
                Record(operationId);
                return ((IExactRetentionStorageSession)inner).ApplyRetention(operationId, options);
            }

            public ValueTask<RetentionOperationResult> ApplyRetentionAsync(
                OperationId operationId,
                RetentionExecutionOptions? options = null)
            {
                Record(operationId);
                return ((IExactRetentionStorageSession)inner).ApplyRetentionAsync(operationId, options);
            }

            private void Record(OperationId operationId)
            {
                lock (owner.retentionOperations)
                    owner.retentionOperations.Add(operationId);
            }
        }
    }

    private sealed class OpenTelemetryStoreFixture(
        IStorageProviderConnection connection,
        GroundworkOpenTelemetryStore store,
        IDiagnosticsPersistenceResourceLease lease) : IAsyncDisposable
    {
        public IStorageProviderConnection Connection => connection;
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
