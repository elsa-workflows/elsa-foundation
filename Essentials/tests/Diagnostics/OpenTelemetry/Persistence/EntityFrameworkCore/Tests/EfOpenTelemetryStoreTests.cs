using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Entities;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Stores;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Tests;

public sealed class EfOpenTelemetryStoreTests
{
    [Fact]
    public async Task Accepted_batch_round_trips_every_signal_and_marks_the_source_before_returning()
    {
        await using var fixture = await CreateFixtureAsync(new() { MaxQuerySize = 10 });
        var resource = TelemetryTestData.Resource("resource-api", "api");
        var trace = TelemetryTestData.Trace("trace-1", resource.Id, spanCount: 1, workflowInstanceIds: ["workflow-1"]);
        var span = TelemetryTestData.Span("span-record-1", trace.TraceId, "span-1", resource.Id);
        var instrument = TelemetryTestData.Instrument("instrument-1", resource.Id, "request.duration");
        var point = TelemetryTestData.Point("point-1", instrument.Id, resource.Id, traceId: trace.TraceId, spanId: span.SpanId) with
        {
            InstrumentName = instrument.Name
        };
        var log = TelemetryTestData.Log("log-1", resource.Id, trace.TraceId, "hello from otel");

        await fixture.Store.WriteAsync(new([resource], [trace], [span], [instrument], [point], [log]));

        Assert.Equal(resource.Id, Assert.Single(fixture.SourceRegistry.List()).Id);
        Assert.Equal(resource.Id, Assert.Single((await fixture.Store.QueryResourcesAsync(new() { ServiceName = "API", Take = 10 })).Items).Id);
        Assert.Equal(trace.TraceId, Assert.Single((await fixture.Store.QueryTracesAsync(new() { WorkflowInstanceId = "workflow-1", Take = 10 })).Items).TraceId);
        Assert.Equal(instrument.Id, Assert.Single((await fixture.Store.QueryMetricsAsync(new() { InstrumentName = "duration", Take = 10 })).Instruments).Id);
        Assert.Equal(point.Id, Assert.Single((await fixture.Store.QueryMetricsAsync(new() { InstrumentName = "duration", Take = 10 })).Points).Id);
        Assert.Equal(log.Id, Assert.Single((await fixture.Store.QueryLogsAsync(new() { Search = "hello", Severity = "information", Take = 10 })).Items).Id);

        var detail = await fixture.Store.GetTraceAsync(trace.TraceId);
        Assert.NotNull(detail);
        Assert.Equal("GET", Assert.Single(Assert.Single(detail!.Spans).Attributes).Value);
        Assert.Equal("event-a", Assert.Single(Assert.Single(detail.Spans).Events).Name);
        Assert.Equal("linked-trace", Assert.Single(Assert.Single(detail.Spans).Links).TraceId);
        Assert.Equal(log.Body, Assert.Single(detail.Logs).Body);
        Assert.Equal(resource.Id, Assert.Single(detail.Resources).Id);
        Assert.Equal(trace.TraceId, detail.Trace.TraceId);
        Assert.Equal(trace.WorkflowInstanceIds, detail.Trace.WorkflowInstanceIds);
    }

    [Fact]
    public async Task Resource_service_filter_is_case_insensitive_and_has_a_stable_tie_break()
    {
        await using var fixture = await CreateFixtureAsync();
        var sameTime = TelemetryTestData.Now;
        var resources = new[]
        {
            TelemetryTestData.Resource("resource-z", "Orders", sameTime),
            TelemetryTestData.Resource("resource-a", "orders", sameTime),
            TelemetryTestData.Resource("resource-other", "billing", sameTime)
        };

        await fixture.Store.WriteAsync(new(resources, [], [], [], [], []));

        var first = (await fixture.Store.QueryResourcesAsync(new() { ServiceName = "ORDERS", Take = 2 })).Items;
        var second = (await fixture.Store.QueryResourcesAsync(new() { ServiceName = "orders", Take = 2 })).Items;

        Assert.Equal(["resource-z", "resource-a"], first.Select(resource => resource.Id));
        Assert.Equal(first.Select(resource => resource.Id), second.Select(resource => resource.Id));
        Assert.DoesNotContain(first, resource => resource.ServiceName == "billing");
    }

    [Fact]
    public async Task Trace_detail_is_not_limited_by_the_summary_query_page_size()
    {
        await using var fixture = await CreateFixtureAsync(new() { MaxQuerySize = 1 });
        var resource = TelemetryTestData.Resource("resource-page", "orders");
        var trace = TelemetryTestData.Trace("trace-page", resource.Id, spanCount: 3);
        var spans = Enumerable.Range(1, 3)
            .Select(index => TelemetryTestData.Span($"span-record-{index}", trace.TraceId, $"span-{index}", resource.Id,
                TelemetryTestData.Now.AddTicks(index)))
            .ToArray();
        var logs = Enumerable.Range(1, 3)
            .Select(index => TelemetryTestData.Log($"log-{index}", resource.Id, trace.TraceId, $"log {index}") with
            {
                Timestamp = TelemetryTestData.Now.AddTicks(index)
            })
            .ToArray();

        await fixture.Store.WriteAsync(new([resource], [trace], spans, [], [], logs));

        var detail = await fixture.Store.GetTraceAsync(trace.TraceId);
        Assert.Equal(["span-1", "span-2", "span-3"], detail!.Spans.Select(span => span.SpanId));
        Assert.Equal(["log-1", "log-2", "log-3"], detail.Logs.Select(log => log.Id));
    }

    [Fact]
    public async Task Trace_detail_uses_the_persisted_ordinal_identity_for_equal_time_ties()
    {
        await using var fixture = await CreateFixtureAsync();
        var resource = TelemetryTestData.Resource("resource-detail-order", "orders");
        var trace = TelemetryTestData.Trace("trace-detail-order", resource.Id, spanCount: 2);
        var spans = new[]
        {
            TelemetryTestData.Span("span-record-a", trace.TraceId, "span-a", resource.Id),
            TelemetryTestData.Span("span-record-z", trace.TraceId, "span-z", resource.Id)
        };
        var logs = new[]
        {
            TelemetryTestData.Log("log-a", resource.Id, trace.TraceId, "a"),
            TelemetryTestData.Log("log-z", resource.Id, trace.TraceId, "z")
        };

        await fixture.Store.WriteAsync(new([resource], [trace], spans, [], [], logs));

        var detail = await fixture.Store.GetTraceAsync(trace.TraceId);
        Assert.Equal(["span-z", "span-a"], detail!.Spans.Select(span => span.SpanId));
        Assert.Equal(["log-z", "log-a"], detail.Logs.Select(log => log.Id));
    }

    [Fact]
    public async Task Trace_detail_loads_resources_from_the_validated_summary_when_membership_rows_are_stale()
    {
        await using var fixture = await CreateFixtureAsync();
        var resource = TelemetryTestData.Resource("resource-summary-detail", "orders");
        var trace = TelemetryTestData.Trace("trace-summary-detail", resource.Id, spanCount: 1);
        await fixture.Store.WriteAsync(new([resource], [trace], [], [], [], []));
        await fixture.WithDbAsync(db => db.TraceSummaryMemberships
            .Where(x => x.Kind == OpenTelemetryTraceSummaryMembershipKind.Resource)
            .ExecuteDeleteAsync());

        var detail = await fixture.Store.GetTraceAsync(trace.TraceId);

        Assert.Equal(resource.Id, Assert.Single(detail!.Resources).Id);
    }

    [Fact]
    public async Task Repeated_trace_records_merge_start_end_status_span_count_and_workflows()
    {
        await using var fixture = await CreateFixtureAsync();
        var start = TelemetryTestData.Now;
        var resource = TelemetryTestData.Resource("resource-merge", "orders", start);
        var first = TelemetryTestData.Trace("trace-merge", resource.Id, start, SpanStatus.Error, 2);
        var second = TelemetryTestData.Trace("trace-merge", resource.Id, start.AddSeconds(1), SpanStatus.Ok, 3, "workflow-a");

        await fixture.Store.WriteAsync(new([resource], [first], [], [], [], []));
        await fixture.Store.WriteAsync(new([], [second], [], [], [], []));

        var summary = Assert.Single((await fixture.Store.QueryTracesAsync(new() { Take = 10 })).Items);
        Assert.Equal(first.StartTime, summary.StartTime);
        Assert.Equal(second.EndTime, summary.EndTime);
        Assert.Equal(second.EndTime - first.StartTime, summary.Duration);
        Assert.Equal(SpanStatus.Error, summary.Status);
        Assert.Equal(5, summary.SpanCount);
        Assert.Equal(["workflow-a"], summary.WorkflowInstanceIds);
        Assert.Equal("trace-merge", Assert.Single((await fixture.Store.QueryTracesAsync(new() { Status = SpanStatus.Error, Take = 10 })).Items).TraceId);
    }

    [Fact]
    public async Task Repeated_trace_memberships_merge_by_the_pinned_projection()
    {
        await using var fixture = await CreateFixtureAsync();
        var longS = TelemetryTestData.Resource("resource-ſ", "orders");
        var asciiS = TelemetryTestData.Resource("resource-s", "orders");
        var first = TelemetryTestData.Trace("trace-pinned-merge", longS.Id, workflowInstanceIds: ["workflow-ſ"]);
        var second = TelemetryTestData.Trace("trace-pinned-merge", asciiS.Id, workflowInstanceIds: ["workflow-s"]);

        await fixture.Store.WriteAsync(new([longS], [first], [], [], [], []));
        await fixture.Store.WriteAsync(new([asciiS], [second], [], [], [], []));

        var summary = Assert.Single((await fixture.Store.QueryTracesAsync(new() { Take = 10 })).Items);
        Assert.Equal([asciiS.Id], summary.ResourceIds);
        Assert.Equal(["workflow-s"], summary.WorkflowInstanceIds);
    }

    [Fact]
    public async Task Equal_start_trace_records_merge_with_a_stable_tie_breaker()
    {
        await using var fixture = await CreateFixtureAsync();
        var resource = TelemetryTestData.Resource("resource-stable-merge", "orders");
        var firstArrival = TelemetryTestData.Trace("trace-stable-merge", resource.Id) with
        {
            RootSpanId = "root-z",
            Name = "operation-z"
        };
        var secondArrival = firstArrival with
        {
            RootSpanId = "root-a",
            Name = "operation-a"
        };

        await fixture.Store.WriteAsync(new([resource], [firstArrival], [], [], [], []));
        await fixture.Store.WriteAsync(new([], [secondArrival], [], [], [], []));

        var summary = Assert.Single((await fixture.Store.QueryTracesAsync(new() { Take = 10 })).Items);
        Assert.Equal(secondArrival.RootSpanId, summary.RootSpanId);
        Assert.Equal(secondArrival.Name, summary.Name);
    }

    [Fact]
    public async Task Trace_membership_bound_is_applied_after_canonicalization()
    {
        await using var fixture = await CreateFixtureAsync();
        var resource = TelemetryTestData.Resource("resource-canonical-bound", "orders");
        var trace = TelemetryTestData.Trace("trace-canonical-bound", resource.Id) with
        {
            ResourceIds = Enumerable.Repeat(resource.Id, 5_001).ToArray()
        };

        await fixture.Store.WriteAsync(new([resource], [trace], [], [], [], []));

        var summary = Assert.Single((await fixture.Store.QueryTracesAsync(new() { Take = 10 })).Items);
        Assert.Equal([resource.Id], summary.ResourceIds);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Null_trace_membership_collections_are_caller_validation_failures(bool nullResources)
    {
        await using var fixture = await CreateFixtureAsync();
        var resource = TelemetryTestData.Resource("resource-null-memberships", "orders");
        var valid = TelemetryTestData.Trace("trace-null-memberships", resource.Id, workflowInstanceIds: ["workflow"]);
        var invalid = nullResources
            ? valid with { ResourceIds = null! }
            : valid with { WorkflowInstanceIds = null! };

        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Store.WriteAsync(new([resource], [invalid], [], [], [], [])).AsTask());
        Assert.Equal(0, (await fixture.Store.GetDiagnosticsAsync()).TraceCount);
    }

    [Fact]
    public async Task Invalid_query_filters_remain_caller_validation_failures()
    {
        await using var fixture = await CreateFixtureAsync();
        const string malformed = "\ud800";
        var overBound = new string('x', 513);

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.QueryResourcesAsync(new() { ServiceName = malformed }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.QueryTracesAsync(new() { WorkflowInstanceId = malformed }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.QueryMetricsAsync(new() { InstrumentName = malformed }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Store.QueryLogsAsync(new() { Search = malformed }).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.QueryResourcesAsync(new() { ServiceName = overBound }).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.QueryTracesAsync(new() { ResourceId = overBound }).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.QueryMetricsAsync(new() { ResourceId = overBound }).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.QueryLogsAsync(new() { ResourceId = overBound }).AsTask());
        var overBoundTraceId = new string('x', 257);
        var overBoundSummaryElement = new string('x', 513);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.QueryTracesAsync(new() { TraceId = overBoundTraceId }).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.QueryTracesAsync(new() { WorkflowInstanceId = overBoundSummaryElement }).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.QueryLogsAsync(new() { TraceId = overBoundTraceId }).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.QueryLogsAsync(new() { SpanId = overBoundTraceId }).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.GetTraceAsync(overBoundTraceId).AsTask());
    }

    [Fact]
    public async Task Trace_filters_use_case_insensitive_unicode_canonical_keys_and_multi_resource_services()
    {
        await using var fixture = await CreateFixtureAsync();
        var api = TelemetryTestData.Resource("Resource-API", "Orders-API");
        var worker = TelemetryTestData.Resource("resource-worker", "orders-worker");
        var trace = new TelemetryTrace("Trace-ÄBC", null, "Über Checkout", TelemetryTestData.Now,
            TelemetryTestData.Now.AddSeconds(1), TimeSpan.FromSeconds(1), SpanStatus.Ok,
            [api.Id, worker.Id], ["Tenant/WORKFLOW-Alpha-123"], 1);
        var span = TelemetryTestData.Span("span-unicode", "trace-äbc", "span-unicode", api.Id);
        var log = TelemetryTestData.Log("log-unicode", api.Id, "TRACE-ÄBC", "captured");

        await fixture.Store.WriteAsync(new([api, worker], [trace], [span], [], [], [log]));

        Assert.Equal([trace.TraceId], (await fixture.Store.QueryTracesAsync(new() { TraceId = "äbc", Take = 10 })).Items.Select(item => item.TraceId));
        Assert.Equal([trace.TraceId], (await fixture.Store.QueryTracesAsync(new() { Search = "ÜBER", Take = 10 })).Items.Select(item => item.TraceId));
        Assert.Equal([trace.TraceId], (await fixture.Store.QueryTracesAsync(new() { WorkflowInstanceId = "workflow-alpha", Take = 10 })).Items.Select(item => item.TraceId));
        Assert.Equal([trace.TraceId], (await fixture.Store.QueryTracesAsync(new() { ServiceName = "ORDERS-WORKER", Take = 10 })).Items.Select(item => item.TraceId));
        Assert.Equal([span.SpanId], (await fixture.Store.GetTraceAsync("TRACE-äbc"))!.Spans.Select(item => item.SpanId));
        Assert.Equal([log.Id], (await fixture.Store.GetTraceAsync("TRACE-äbc"))!.Logs.Select(item => item.Id));
    }

    [Fact]
    public async Task Metric_and_log_service_filters_follow_durable_resource_catalog_values()
    {
        await using var fixture = await CreateFixtureAsync();
        var api = TelemetryTestData.Resource("resource-api", "api");
        var worker = TelemetryTestData.Resource("resource-worker", "worker");
        var apiInstrument = TelemetryTestData.Instrument("instrument-api", api.Id, "queue.depth");
        var workerInstrument = TelemetryTestData.Instrument("instrument-worker", worker.Id, "queue.depth");

        await fixture.Store.WriteAsync(new(
            [api, worker], [], [], [apiInstrument, workerInstrument],
            [TelemetryTestData.Point("point-api", apiInstrument.Id, api.Id), TelemetryTestData.Point("point-worker", workerInstrument.Id, worker.Id)],
            [TelemetryTestData.Log("log-api", api.Id, "trace-api", "api"), TelemetryTestData.Log("log-worker", worker.Id, "trace-worker", "worker")]));

        Assert.Equal(["point-api"], (await fixture.Store.QueryMetricsAsync(new() { ServiceName = "API", Take = 10 })).Points.Select(point => point.Id));
        Assert.Equal(["log-worker"], (await fixture.Store.QueryLogsAsync(new() { ServiceName = "WORKER", Take = 10 })).Items.Select(log => log.Id));
    }

    [Fact]
    public async Task Signal_filters_honor_resource_status_dates_ids_and_text_fields()
    {
        await using var fixture = await CreateFixtureAsync();
        var before = TelemetryTestData.Now.AddSeconds(-2);
        var after = TelemetryTestData.Now.AddSeconds(2);
        var resource = TelemetryTestData.Resource("resource-filter", "orders", TelemetryTestData.Now, TelemetryResourceStatus.Stale);
        var instrument = TelemetryTestData.Instrument("instrument-filter", resource.Id, "latency");
        var trace = TelemetryTestData.Trace("trace-filter", resource.Id, TelemetryTestData.Now, SpanStatus.Error, 1);
        var span = TelemetryTestData.Span("span-filter", trace.TraceId, "span-filter", resource.Id);
        var point = TelemetryTestData.Point("point-filter", instrument.Id, resource.Id, TelemetryTestData.Now, trace.TraceId, span.SpanId);
        var log = TelemetryTestData.Log("log-filter", resource.Id, trace.TraceId, "needle", "Warning") with
        {
            SpanId = span.SpanId
        };
        await fixture.Store.WriteAsync(new([resource], [trace], [span], [instrument], [point], [log]));

        Assert.Equal([resource.Id], (await fixture.Store.QueryResourcesAsync(new()
        {
            Search = "FILTER",
            Status = TelemetryResourceStatus.Stale,
            Take = 10
        })).Items.Select(item => item.Id));
        Assert.Equal([trace.TraceId], (await fixture.Store.QueryTracesAsync(new()
        {
            ResourceId = resource.Id,
            Status = SpanStatus.Error,
            From = before,
            To = after,
            Take = 10
        })).Items.Select(item => item.TraceId));
        Assert.Equal([point.Id], (await fixture.Store.QueryMetricsAsync(new()
        {
            ResourceId = resource.Id,
            From = before,
            To = after,
            Take = 10
        })).Points.Select(item => item.Id));
        Assert.Equal([log.Id], (await fixture.Store.QueryLogsAsync(new()
        {
            ResourceId = resource.Id,
            TraceId = trace.TraceId,
            SpanId = span.SpanId,
            Severity = "warning",
            From = before,
            To = after,
            Search = "NEEDLE",
            Take = 10
        })).Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Case_equivalent_instrument_ids_collapse_without_losing_points()
    {
        await using var fixture = await CreateFixtureAsync();
        var resource = TelemetryTestData.Resource("resource-api", "api");
        var lower = TelemetryTestData.Instrument("resource-api:request.count:gauge", resource.Id, "request.count");
        var upper = TelemetryTestData.Instrument("RESOURCE-API:REQUEST.COUNT:GAUGE", resource.Id, "REQUEST.COUNT");

        await fixture.Store.WriteAsync(new([resource], [], [], [lower], [TelemetryTestData.Point("point-1", lower.Id, resource.Id)], []));
        await fixture.Store.WriteAsync(new([], [], [], [upper], [TelemetryTestData.Point("point-2", upper.Id, resource.Id, TelemetryTestData.Now.AddSeconds(1))], []));

        var result = await fixture.Store.QueryMetricsAsync(new() { InstrumentName = "REQUEST.COUNT", Take = 10 });
        Assert.Single(result.Instruments);
        Assert.Equal(["point-1", "point-2"], result.Points.Select(point => point.Id));
    }

    [Fact]
    public async Task Persisted_unicode_identity_collapses_catalogs_and_resolves_services_consistently()
    {
        await using var fixture = await CreateFixtureAsync();
        var longSResource = TelemetryTestData.Resource("resource-ſ", "service-old");
        var asciiResource = TelemetryTestData.Resource("resource-s", "service-current");
        var longSInstrument = TelemetryTestData.Instrument("instrument-ſ", longSResource.Id, "requests-old");
        var asciiInstrument = TelemetryTestData.Instrument("instrument-s", asciiResource.Id, "requests-current");
        var trace = TelemetryTestData.Trace("trace-pinned-identity", longSResource.Id, spanCount: 1);
        var point = TelemetryTestData.Point("point-pinned-identity", longSInstrument.Id, longSResource.Id);
        var log = TelemetryTestData.Log("log-pinned-identity", longSResource.Id, trace.TraceId);

        await fixture.Store.WriteAsync(new(
            [longSResource, asciiResource],
            [trace],
            [],
            [longSInstrument, asciiInstrument],
            [point],
            [log]));

        var resource = Assert.Single((await fixture.Store.QueryResourcesAsync(new() { Take = 10 })).Items);
        Assert.Equal(asciiResource.Id, resource.Id);
        Assert.Equal(asciiResource.ServiceName, resource.ServiceName);
        Assert.Equal([trace.TraceId], (await fixture.Store.QueryTracesAsync(new() { ServiceName = "SERVICE-CURRENT", Take = 10 })).Items.Select(x => x.TraceId));
        Assert.Equal([point.Id], (await fixture.Store.QueryMetricsAsync(new() { ServiceName = "SERVICE-CURRENT", Take = 10 })).Points.Select(x => x.Id));
        Assert.Equal(asciiInstrument.Id, Assert.Single((await fixture.Store.QueryMetricsAsync(new() { Take = 10 })).Instruments).Id);
        Assert.Equal([log.Id], (await fixture.Store.QueryLogsAsync(new() { ServiceName = "SERVICE-CURRENT", Take = 10 })).Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Query_bounds_are_clamped_and_ordering_is_deterministic()
    {
        await using var fixture = await CreateFixtureAsync(new() { MaxQuerySize = 2 });
        for (var index = 1; index <= 4; index++)
        {
            var resource = TelemetryTestData.Resource($"resource-{index}", "orders", TelemetryTestData.Now.AddSeconds(index));
            await fixture.Store.WriteAsync(new([resource], [TelemetryTestData.Trace($"trace-{index}", resource.Id, resource.LastSeen, spanCount: 1)], [], [], [], []));
        }

        Assert.Empty((await fixture.Store.QueryTracesAsync(new() { Take = 0 })).Items);
        Assert.Equal(2, (await fixture.Store.QueryTracesAsync(new() { Take = 100 })).Items.Count);
        var first = (await fixture.Store.QueryTracesAsync(new() { Take = 100 })).Items.Select(item => item.TraceId);
        var second = (await fixture.Store.QueryTracesAsync(new() { Take = 100 })).Items.Select(item => item.TraceId);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Resource_query_is_capacity_bounded_before_the_retention_barrier()
    {
        await using var fixture = await CreateFixtureAsync(new() { ResourceCapacity = 1, MaxQuerySize = 10 });
        var older = TelemetryTestData.Resource("resource-capacity-old", "orders", TelemetryTestData.Now);
        var newer = TelemetryTestData.Resource("resource-capacity-new", "orders", TelemetryTestData.Now.AddTicks(1));

        await fixture.EfStore.WriteAsync(DiagnosticsDrainBatchId.New(), new([older, newer], [], [], [], [], []));

        Assert.Equal([newer.Id], (await fixture.Store.QueryResourcesAsync(new() { Take = 10 })).Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Trace_name_bound_accepts_the_declared_limit_and_refuses_longer_names()
    {
        await using var fixture = await CreateFixtureAsync();
        var accepted = TelemetryTestData.Trace("trace-name-accepted", "resource-name", spanCount: 1) with { Name = new string('n', 571) };
        await fixture.Store.WriteAsync(new([], [accepted], [], [], [], []));
        Assert.Equal(accepted.TraceId, Assert.Single((await fixture.Store.QueryTracesAsync(new() { Search = new string('n', 571), Take = 10 })).Items).TraceId);

        var refused = TelemetryTestData.Trace("trace-name-refused", "resource-name", spanCount: 1) with { Name = new string('n', 572) };
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.WriteAsync(new([], [refused], [], [], [], [])).AsTask());

        var whitespace = TelemetryTestData.Trace("trace-name-whitespace", "resource-name", spanCount: 1) with { Name = new string(' ', 1_024) };
        await fixture.Store.WriteAsync(new([], [whitespace], [], [], [], []));
        Assert.Null((await fixture.Store.GetTraceAsync(whitespace.TraceId))!.Trace.Name);
    }

    [Fact]
    public async Task Unbounded_signal_text_fields_round_trip_without_an_artificial_summary_bound()
    {
        await using var fixture = await CreateFixtureAsync();
        var batch = TelemetryTestData.Batch("unbounded-text");
        var longName = new string('n', 1_024);
        var longSeverity = new string('s', 1_024);
        var longBody = new string('b', 5_000);
        batch = batch with
        {
            Spans = [batch.Spans.Single() with { Name = longName }],
            Instruments = [batch.Instruments.Single() with { Name = longName }],
            MetricPoints = [batch.MetricPoints.Single() with { InstrumentName = longName }],
            Logs = [batch.Logs.Single() with { SeverityText = longSeverity, Body = longBody }]
        };

        await fixture.Store.WriteAsync(batch);

        Assert.Equal(longName, Assert.Single((await fixture.Store.GetTraceAsync("unbounded-text"))!.Spans).Name);
        Assert.Equal(longName, Assert.Single((await fixture.Store.QueryMetricsAsync(new() { InstrumentName = new string('n', 64), Take = 10 })).Points).InstrumentName);
        var log = Assert.Single((await fixture.Store.QueryLogsAsync(new() { Search = new string('b', 64), Severity = new string('s', 64), Take = 10 })).Items);
        Assert.Equal(longSeverity, log.SeverityText);
        Assert.Equal(longBody, log.Body);
    }

    [Fact]
    public async Task Over_bound_signal_identity_is_rejected_before_any_part_of_the_capture_commits()
    {
        await using var fixture = await CreateFixtureAsync();
        var batch = TelemetryTestData.Batch("bounded-identity");
        batch = batch with
        {
            Spans = [batch.Spans.Single() with { Id = new string('s', 129) }]
        };

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.WriteAsync(batch).AsTask());

        var diagnostics = await fixture.Store.GetDiagnosticsAsync();
        Assert.Equal((0, 0, 0, 0, 0, 0),
            (diagnostics.ResourceCount, diagnostics.TraceCount, diagnostics.SpanCount,
                diagnostics.MetricInstrumentCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
        Assert.Empty(fixture.SourceRegistry.List());
    }

    [Fact]
    public async Task Over_bound_workflow_cardinality_is_atomic_and_does_not_mark_resources()
    {
        await using var fixture = await CreateFixtureAsync();
        var resource = TelemetryTestData.Resource("resource-bound", "orders");
        var trace = TelemetryTestData.Trace("trace-bound", resource.Id, spanCount: 1,
            workflowInstanceIds: Enumerable.Range(0, 5_001).Select(index => $"workflow-{index}").ToArray());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Store.WriteAsync(new([resource], [trace], [], [], [], [])).AsTask());
        Assert.Empty((await fixture.Store.QueryTracesAsync(new() { Take = 10 })).Items);
        Assert.Empty((await fixture.Store.QueryResourcesAsync(new() { Take = 10 })).Items);
    }

    [Fact]
    public async Task Identical_batch_replay_is_idempotent_and_a_changed_fingerprint_is_rejected()
    {
        await using var fixture = await CreateFixtureAsync();
        var batch = TelemetryTestData.Batch("replay-trace");
        var batchId = DiagnosticsDrainBatchId.New();

        await fixture.EfStore.WriteAsync(batchId, batch);
        await fixture.EfStore.WriteAsync(batchId, batch);

        Assert.Single((await fixture.Store.QueryTracesAsync(new() { Take = 10 })).Items);
        Assert.Single((await fixture.Store.QueryResourcesAsync(new() { Take = 10 })).Items);
        Assert.Single((await fixture.Store.QueryMetricsAsync(new() { Take = 10 })).Points);
        Assert.Single((await fixture.Store.QueryLogsAsync(new() { Take = 10 })).Items);
        var fingerprint = await fixture.WithDbAsync(db => db.CaptureLedger
            .Where(item => item.BatchId == batchId.Value)
            .Select(item => item.Fingerprint)
            .SingleAsync());
        Assert.Equal("0db20969a84d22f8d700db369c437439e88f2406d5fc711631b5e39480c323a2", fingerprint);

        var changed = batch with { Traces = [batch.Traces.Single() with { Name = "changed" }] };
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.EfStore.WriteAsync(batchId, changed).AsTask());
        Assert.Equal(batch.Traces.Single().Name, (await fixture.Store.GetTraceAsync("replay-trace"))?.Trace.Name);
    }

    [Fact]
    public async Task Acknowledgement_loss_replay_commits_one_stable_operation()
    {
        await using var fixture = await CreateFixtureAsync();
        var batch = TelemetryTestData.Batch("ack-loss");
        var batchId = DiagnosticsDrainBatchId.New();

        // The first call represents a commit whose acknowledgement was lost by the caller. Retrying
        // the same durable operation must be safe even though the caller cannot know whether it committed.
        await fixture.EfStore.WriteAsync(batchId, batch);
        await fixture.EfStore.WriteAsync(batchId, batch);

        var diagnostics = await fixture.Store.GetDiagnosticsAsync();
        Assert.Equal(1, diagnostics.TraceCount);
        Assert.Equal(1, diagnostics.SpanCount);
        Assert.Equal(1, diagnostics.MetricPointCount);
        Assert.Equal(1, diagnostics.LogRecordCount);
    }

    [Fact]
    public async Task Replay_fingerprint_does_not_change_when_only_mutable_resource_enrichment_changes()
    {
        await using var fixture = await CreateFixtureAsync();
        var original = TelemetryTestData.Resource("resource-replay", "orders-v1");
        var updated = original with { ServiceName = "orders-v2", LastSeen = original.LastSeen.AddMinutes(1) };
        var trace = TelemetryTestData.Trace("trace-catalog-replay", original.Id, spanCount: 1);
        var traceBatchId = DiagnosticsDrainBatchId.New();

        await fixture.Store.WriteAsync(new([original], [], [], [], [], []));
        await fixture.EfStore.WriteAsync(traceBatchId, new([], [trace], [], [], [], []));
        await fixture.Store.WriteAsync(new([updated], [], [], [], [], []));
        await fixture.EfStore.WriteAsync(traceBatchId, new([], [trace], [], [], [], []));

        Assert.Equal([trace.TraceId], (await fixture.Store.QueryTracesAsync(new() { ServiceName = "orders-v1", Take = 10 })).Items.Select(item => item.TraceId));
        Assert.Empty((await fixture.Store.QueryTracesAsync(new() { ServiceName = "orders-v2", Take = 10 })).Items);
    }

    [Fact]
    public async Task Pre_cancelled_queries_and_writes_honor_cancellation()
    {
        await using var fixture = await CreateFixtureAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.WriteAsync(TelemetryTestData.Batch("cancelled"), cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.QueryTracesAsync(new(), cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.GetTraceAsync("trace", cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.QueryResourcesAsync(new() { Take = 0 }, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.QueryTracesAsync(new() { Take = 0 }, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.QueryMetricsAsync(new() { Take = 0 }, cancellation.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.QueryLogsAsync(new() { Take = 0 }, cancellation.Token).AsTask());

        await using var zeroCapacity = await CreateFixtureAsync(new() { ResourceCapacity = 0 });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => zeroCapacity.Store.QueryResourcesAsync(new() { Take = 10 }, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task Write_before_the_drain_starts_is_rejected_and_counted_without_provider_io()
    {
        var counters = new DiagnosticsPersistenceCounters();
        await using var store = new EfOpenTelemetryStore(
            new SessionlessScopeFactory(),
            Options.Create(new OpenTelemetryDiagnosticsOptions()),
            EfOpenTelemetryBinding.Default,
            observer: counters);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync(TelemetryTestData.Batch("early")).AsTask());

        Assert.Equal(1, counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.WriteBeforeStart]);
    }

    private static async Task<OpenTelemetryEntityFrameworkCoreFixture> CreateFixtureAsync(OpenTelemetryDiagnosticsOptions? options = null)
    {
        var fixture = new OpenTelemetryEntityFrameworkCoreFixture();
        await fixture.InitializeAsync(options);
        return fixture;
    }

    private sealed class SessionlessScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new IOException("The store must not open a session before its drain starts.");
    }
}
