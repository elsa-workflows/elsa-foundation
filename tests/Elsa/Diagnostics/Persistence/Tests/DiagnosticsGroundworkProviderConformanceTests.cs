using Elsa.Diagnostics.OpenTelemetry.Core.Exceptions;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Records;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Groundwork.DiagnosticRecords;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

[Collection(DiagnosticsProviderCollection.Name)]
public sealed class DiagnosticsGroundworkProviderConformanceTests(DiagnosticsProviderFixture providers)
{
    public static TheoryData<DiagnosticsProviderKind> ProviderKinds =>
        new()
        {
            DiagnosticsProviderKind.Sqlite,
            DiagnosticsProviderKind.SqlServer,
            DiagnosticsProviderKind.PostgreSql,
            DiagnosticsProviderKind.MongoDb
        };

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task OpenTelemetry_catalogs_records_and_counts_survive_restart(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var binding = GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-a", "collector-a");
        var batch = OpenTelemetryBatch();

        await using (var first = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding))
        {
            var store = new GroundworkOpenTelemetryStore(
                first.Stores,
                Options.Create(new OpenTelemetryDiagnosticsOptions()),
                binding);
            await store.WriteAsync(DiagnosticsDrainBatchId.New(), batch);
        }

        await using var restarted = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding);
        var restartedStore = new GroundworkOpenTelemetryStore(
            restarted.Stores,
            Options.Create(new OpenTelemetryDiagnosticsOptions()),
            binding);
        var diagnostics = await restartedStore.GetDiagnosticsAsync();
        var resources = await restartedStore.QueryResourcesAsync(new() { Take = 10 });
        var metrics = await restartedStore.QueryMetricsAsync(new() { Take = 10 });
        var logs = await restartedStore.QueryLogsAsync(new() { Take = 10 });

        Assert.Equal((1, 1, 1, 1, 1, 1),
            (diagnostics.ResourceCount, diagnostics.TraceCount, diagnostics.SpanCount,
                diagnostics.MetricInstrumentCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
        Assert.Equal("resource-1", Assert.Single(resources.Items).Id);
        Assert.Equal("instrument-1", Assert.Single(metrics.Instruments).Id);
        Assert.Equal("point-1", Assert.Single(metrics.Points).Id);
        Assert.Equal("log-1", Assert.Single(logs.Items).Id);
    }

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task OpenTelemetry_operation_identity_and_concurrent_writers_match_across_providers(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var binding = GroundworkOpenTelemetryBinding.Create("tenant-a", "shell-a", "collector-a");
        await using var firstProvider =
            await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding);
        await using var secondProvider =
            await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding);
        await using var first = new GroundworkOpenTelemetryStore(
            firstProvider.Stores,
            Options.Create(new OpenTelemetryDiagnosticsOptions()),
            binding);
        await using var second = new GroundworkOpenTelemetryStore(
            secondProvider.Stores,
            Options.Create(new OpenTelemetryDiagnosticsOptions()),
            binding);
        var batchId = DiagnosticsDrainBatchId.New();
        var batch = OpenTelemetryBatch();

        await Task.WhenAll(
            first.WriteAsync(batchId, batch).AsTask(),
            second.WriteAsync(batchId, batch).AsTask());
        await first.WriteAsync(batchId, batch);

        var conflicting = batch with
        {
            Logs = batch.Logs.Select(log => log with { Body = $"{log.Body}-changed" }).ToArray()
        };
        var conflict = await Assert.ThrowsAsync<OpenTelemetryPersistenceConflictException>(() =>
            first.WriteAsync(batchId, conflicting).AsTask());
        var diagnostics = await first.GetDiagnosticsAsync();

        Assert.Equal(OpenTelemetryPersistenceFailureReason.ConflictingOperation, conflict.Reason);
        Assert.Equal(batchId.ToString(), conflict.Context["batchId"]);
        Assert.IsType<DiagnosticOperationConflictException>(conflict.InnerException);
        Assert.Equal((1, 1, 1, 1, 1, 1),
            (diagnostics.ResourceCount, diagnostics.TraceCount, diagnostics.SpanCount,
                diagnostics.MetricInstrumentCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount));
    }

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task Repeated_trace_fragments_reduce_before_filters_ordering_take_continuation_and_restart(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var binding = GroundworkOpenTelemetryBinding.Create("tenant-a", "trace-summary-scope", "collector-a");
        var start = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var resource = Resource("resource-api", "Orders", start);
        var alphaFirst = new TelemetryTrace(
            "trace-alpha", "root-alpha", "first alpha", start, start.AddMilliseconds(10), TimeSpan.FromMilliseconds(10),
            SpanStatus.Error, [resource.Id], [], 2);
        var alphaSecond = new TelemetryTrace(
            "trace-alpha", "root-later", "later alpha", start.AddSeconds(1), start.AddSeconds(1).AddMilliseconds(25),
            TimeSpan.FromMilliseconds(25), SpanStatus.Ok, [resource.Id], ["workflow-alpha"], 3);
        var beta = new TelemetryTrace(
            "trace-beta", "root-beta", "beta", start.AddSeconds(2), start.AddSeconds(2).AddMilliseconds(5),
            TimeSpan.FromMilliseconds(5), SpanStatus.Ok, [resource.Id], ["workflow-beta"], 1);
        var gamma = new TelemetryTrace(
            "trace-gamma", "root-gamma", "gamma", start.AddSeconds(3), start.AddSeconds(3).AddMilliseconds(5),
            TimeSpan.FromMilliseconds(5), SpanStatus.Ok, [resource.Id], ["workflow-gamma"], 1);

        await using (var firstProvider = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding))
        await using (var secondProvider = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding))
        await using (var first = new GroundworkOpenTelemetryStore(
                         firstProvider.Stores,
                         Options.Create(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 10 }),
                         binding))
        await using (var second = new GroundworkOpenTelemetryStore(
                         secondProvider.Stores,
                         Options.Create(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 10 }),
                         binding))
        {
            await first.WriteAsync(DiagnosticsDrainBatchId.New(), new([resource], [], [], [], [], []));
            await Task.WhenAll(
                first.WriteAsync(DiagnosticsDrainBatchId.New(), new([], [alphaFirst], [], [], [], [])).AsTask(),
                second.WriteAsync(DiagnosticsDrainBatchId.New(), new([], [alphaSecond], [], [], [], [])).AsTask());
            await first.WriteAsync(DiagnosticsDrainBatchId.New(), new([], [beta, gamma], [], [], [], []));
        }

        await using var restartedProvider = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding);
        await using var restarted = new GroundworkOpenTelemetryStore(
            restartedProvider.Stores,
            Options.Create(new OpenTelemetryDiagnosticsOptions { MaxQuerySize = 10 }),
            binding);

        var merged = Assert.Single((await restarted.QueryTracesAsync(new()
        {
            TraceId = "ALPHA",
            ResourceId = "RESOURCE-API",
            ServiceName = "ORDERS",
            WorkflowInstanceId = "WORKFLOW-ALPHA",
            Status = SpanStatus.Error,
            From = start,
            To = alphaSecond.EndTime,
            Search = "ALPHA",
            Take = 10
        })).Items);
        var newestTwo = await restarted.QueryTracesAsync(new() { Take = 2 });
        var detail = await restarted.GetTraceAsync("TRACE-ALPHA");

        Assert.Equal((alphaFirst.StartTime, alphaSecond.EndTime, alphaSecond.EndTime - alphaFirst.StartTime),
            (merged.StartTime, merged.EndTime, merged.Duration));
        Assert.Equal(("root-alpha", "first alpha", SpanStatus.Error, 5),
            (merged.RootSpanId, merged.Name, merged.Status, merged.SpanCount));
        Assert.Equal([resource.Id], merged.ResourceIds);
        Assert.Equal(["workflow-alpha"], merged.WorkflowInstanceIds);
        Assert.Equal(["trace-beta", "trace-gamma"], newestTwo.Items.Select(trace => trace.TraceId));
        Assert.NotNull(detail);
        Assert.Equal(merged.TraceId, detail.Trace.TraceId);
        Assert.Equal(merged.RootSpanId, detail.Trace.RootSpanId);
        Assert.Equal(merged.Name, detail.Trace.Name);
        Assert.Equal(merged.StartTime, detail.Trace.StartTime);
        Assert.Equal(merged.EndTime, detail.Trace.EndTime);
        Assert.Equal(merged.Duration, detail.Trace.Duration);
        Assert.Equal(merged.Status, detail.Trace.Status);
        Assert.Equal(merged.ResourceIds, detail.Trace.ResourceIds);
        Assert.Equal(merged.WorkflowInstanceIds, detail.Trace.WorkflowInstanceIds);
        Assert.Equal(merged.SpanCount, detail.Trace.SpanCount);

        var scope = new DiagnosticStorageScope(binding.TenantId, binding.ScopeId);
        var firstPage = await restartedProvider.Stores.Traces.QueryGroupsAsync(new(
            scope,
            new(binding.TraceStreamId),
            OpenTelemetryRecordStreamDefinitions.TraceSummaryProfileName,
            2,
            new(RecordFields.StartTime),
            OpenTelemetryRecordStreamDefinitions.MaxTraceRecordCapacity));
        var secondPage = await restartedProvider.Stores.Traces.QueryGroupsAsync(new(
            scope,
            new(binding.TraceStreamId),
            OpenTelemetryRecordStreamDefinitions.TraceSummaryProfileName,
            2,
            new(RecordFields.StartTime),
            OpenTelemetryRecordStreamDefinitions.MaxTraceRecordCapacity,
            Continuation: firstPage.Continuation));

        Assert.Equal(["trace-alpha", "trace-beta"], firstPage.Groups.Select(group => GroupString(group, RecordFields.TraceId)));
        Assert.Equal(["trace-gamma"], secondPage.Groups.Select(group => GroupString(group, RecordFields.TraceId)));
        Assert.Null(secondPage.Continuation);
    }

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task Grouped_trace_union_accepts_valid_values_observed_before_retention(
        DiagnosticsProviderKind providerKind)
    {
        const int distinctValues = 257;
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var binding = GroundworkOpenTelemetryBinding.Create("tenant-a", "trace-union-boundary", "collector-a");
        var start = new DateTimeOffset(2026, 7, 25, 15, 0, 0, TimeSpan.Zero);
        var resources = Enumerable.Range(0, distinctValues)
            .Select(index => Resource($"resource-{index:D3}", $"service-{index:D3}", start.AddTicks(index)))
            .ToArray();
        var fragments = resources
            .Select((resource, index) => new TelemetryTrace(
                "trace-wide",
                index == 0 ? "root-wide" : null,
                index == 0 ? "wide trace" : null,
                start.AddTicks(index),
                start.AddTicks(index + 1),
                TimeSpan.FromTicks(1),
                SpanStatus.Ok,
                [resource.Id],
                [$"workflow-{index:D3}"],
                1))
            .ToArray();

        await using var providerLease =
            await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding);
        await using var store = new GroundworkOpenTelemetryStore(
            providerLease.Stores,
            Options.Create(new OpenTelemetryDiagnosticsOptions()),
            binding);
        var serializer = new CanonicalRecordSerializer();
        var records = fragments
            .Select((trace, index) => serializer.ToRecord(
                $"trace-fragment-{index:D3}",
                trace,
                [$"service-{index:D3}"]))
            .ToArray();
        await providerLease.Stores.Traces.AppendAsync(DiagnosticRecordBatch.Create(
            new(binding.TenantId, binding.ScopeId),
            new(binding.TraceStreamId),
            new(DateTimeOffset.UtcNow, "pre-retention-trace-union"),
            records));

        var trace = Assert.Single((await store.QueryTracesAsync(new()
        {
            TraceId = "TRACE-WIDE",
            ServiceName = "SERVICE-256",
            WorkflowInstanceId = "WORKFLOW-256",
            Take = 1
        })).Items);

        Assert.Equal(distinctValues, trace.ResourceIds.Count);
        Assert.Equal(distinctValues, trace.WorkflowInstanceIds.Count);
        Assert.Equal(distinctValues, trace.SpanCount);
        Assert.Contains("resource-256", trace.ResourceIds);
        Assert.Contains("workflow-256", trace.WorkflowInstanceIds);
    }

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task Grouped_trace_queries_reduce_the_configured_newest_raw_window_before_filters(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var binding = GroundworkOpenTelemetryBinding.Create("tenant-a", "trace-input-window", "collector-a");
        var start = new DateTimeOffset(2026, 7, 25, 16, 0, 0, TimeSpan.Zero);
        var serializer = new CanonicalRecordSerializer();
        var traces = Enumerable.Range(0, 3)
            .Select(index => Trace(
                $"trace-{index}",
                $"resource-{index}",
                start.AddSeconds(index),
                $"trace {index}",
                SpanStatus.Ok))
            .ToArray();

        await using var providerLease =
            await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding);
        await providerLease.Stores.Traces.AppendAsync(DiagnosticRecordBatch.Create(
            new(binding.TenantId, binding.ScopeId),
            new(binding.TraceStreamId),
            new(DateTimeOffset.UtcNow, "pre-retention-trace-window"),
            traces.Select((trace, index) =>
                serializer.ToRecord($"trace-window-{index}", trace, [$"service-{index}"])).ToArray()));
        await using var store = new GroundworkOpenTelemetryStore(
            providerLease.Stores,
            Options.Create(new OpenTelemetryDiagnosticsOptions { TraceCapacity = 2 }),
            binding);

        var visible = await store.QueryTracesAsync(new() { Take = 10 });
        var excludedByWindow = await store.QueryTracesAsync(new() { TraceId = "trace-0", Take = 10 });
        var excludedDetail = await store.GetTraceAsync("trace-0");

        Assert.Equal(["trace-1", "trace-2"], visible.Items.Select(trace => trace.TraceId));
        Assert.Empty(excludedByWindow.Items);
        Assert.Null(excludedDetail);
    }

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task Preview81_trace_definition_fails_closed_and_preview86_requires_fresh_unreleased_storage(
        DiagnosticsProviderKind providerKind)
    {
        var previousProvider = await providers.CreateIsolatedAsync(providerKind);
        const string streamId = "otel:upgrade-proof:traces";
        await DiagnosticsGroundworkProviderHarness.CreateDiagnosticRecordStreamAsync(
            previousProvider,
            Preview81TraceDefinition(streamId));

        var incompatible = await Assert.ThrowsAnyAsync<Exception>(() =>
            DiagnosticsGroundworkProviderHarness.CreateDiagnosticRecordStreamAsync(
                previousProvider,
                OpenTelemetryRecordStreamDefinitions.CreateTraces(streamId)));
        Assert.Contains("incompatible persisted definition", incompatible.ToString(), StringComparison.OrdinalIgnoreCase);

        var freshProvider = await providers.CreateIsolatedAsync(providerKind);
        await DiagnosticsGroundworkProviderHarness.CreateDiagnosticRecordStreamAsync(
            freshProvider,
            OpenTelemetryRecordStreamDefinitions.CreateTraces(streamId));
    }

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task Catalog_identity_uses_the_provider_unicode_case_policy_across_collision_restart_retention_and_scopes(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var binding = GroundworkOpenTelemetryBinding.Create("tenant-a", "catalog-case-scope", "collector-a");
        var foreignBinding = GroundworkOpenTelemetryBinding.Create("tenant-a", "catalog-case-foreign", "collector-a");
        var time = new DateTimeOffset(2026, 7, 25, 14, 0, 0, TimeSpan.Zero);
        var upperDeseret = "\U00010400";
        var lowerDeseret = "\U00010428";
        var originalId = $"resource-{upperDeseret}";
        var collidingId = $"RESOURCE-{lowerDeseret}";
        var original = Resource(originalId, $"Orders-{upperDeseret}", time);
        var collision = Resource(collidingId, $"Orders-{lowerDeseret}", time.AddSeconds(1));

        await using (var firstProvider = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding))
        await using (var first = new GroundworkOpenTelemetryStore(
                         firstProvider.Stores,
                         Options.Create(new OpenTelemetryDiagnosticsOptions { ResourceCapacity = 2, MaxQuerySize = 10 }),
                         binding))
        {
            await first.WriteAsync(DiagnosticsDrainBatchId.New(), new([original], [], [], [], [], []));
            await first.WriteAsync(DiagnosticsDrainBatchId.New(), new([collision], [], [], [], [], []));

            var loaded = Assert.Single((await first.QueryResourcesAsync(new()
            {
                ServiceName = $"ORDERS-{upperDeseret}",
                Take = 10
            })).Items);
            Assert.Equal(collidingId, loaded.Id);
            Assert.Equal(collision.ServiceName, loaded.ServiceName);
        }

        await using (var foreignProvider = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, foreignBinding))
        await using (var foreign = new GroundworkOpenTelemetryStore(
                         foreignProvider.Stores,
                         Options.Create(new OpenTelemetryDiagnosticsOptions { ResourceCapacity = 2, MaxQuerySize = 10 }),
                         foreignBinding))
        {
            await foreign.WriteAsync(DiagnosticsDrainBatchId.New(), new(
                [Resource(originalId, "foreign-service", time.AddSeconds(2))], [], [], [], [], []));
            Assert.Equal("foreign-service", Assert.Single((await foreign.QueryResourcesAsync(new() { Take = 10 })).Items).ServiceName);
        }

        await using var restartedProvider = await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, binding);
        await using var restarted = new GroundworkOpenTelemetryStore(
            restartedProvider.Stores,
            Options.Create(new OpenTelemetryDiagnosticsOptions { ResourceCapacity = 1, MaxQuerySize = 10 }),
            binding);

        var restartedResource = Assert.Single((await restarted.QueryResourcesAsync(new() { Take = 10 })).Items);
        Assert.Equal(collidingId, restartedResource.Id);
        Assert.Equal(collision.ServiceName, restartedResource.ServiceName);

        var retained = Resource("resource-later", "later-service", time.AddSeconds(3));
        await restarted.WriteAsync(DiagnosticsDrainBatchId.New(), new([retained], [], [], [], [], []));

        Assert.Equal([retained.Id], (await restarted.QueryResourcesAsync(new() { Take = 10 })).Items.Select(resource => resource.Id));
        Assert.Empty((await restarted.QueryResourcesAsync(new() { ServiceName = $"orders-{lowerDeseret}", Take = 10 })).Items);
    }

    private static DiagnosticRecordStreamDefinition Preview81TraceDefinition(string streamId)
    {
        const int maxIdentifierBytes = 512;
        const int maxNameBytes = 4_096;
        const int maxMultiValues = 256;
        var fields = new[]
        {
            String(RecordFields.TraceId, required: true, latestPerKey: true),
            String(RecordFields.ResourceId, required: true, multiple: true),
            String(RecordFields.ServiceName, multiple: true),
            String(RecordFields.WorkflowInstanceId, multiple: true),
            Int64(RecordFields.Status, required: true),
            Timestamp(RecordFields.StartTime, required: true, orderable: true),
            Timestamp(RecordFields.EndTime, required: true),
            String(RecordFields.Name, maxStringBytes: maxNameBytes)
        };
        var definition = new DiagnosticRecordStreamDefinition(
            new(streamId),
            1,
            "elsa_open_telemetry_traces",
            fields,
            new(
                MaxBatchRecords: 1_000,
                MaxPayloadBytes: 1_048_576,
                MaxRecordIdBytes: 128,
                MaxFieldsPerRecord: fields.Length,
                MaxQueryLimit: 5_000,
                MaxPredicateNodes: 32,
                MaxPredicateValues: 9,
                MaxJsonDepth: 64),
            MaxOperationClockSkew: TimeSpan.FromMinutes(5),
            AppendIdempotencyWindow: TimeSpan.FromHours(1),
            TrimIdempotencyWindow: TimeSpan.FromHours(1));
        DiagnosticRecordStreamDefinitionValidator.ValidateAndThrow(definition);
        return definition;

        static DiagnosticFieldDefinition String(
            string name,
            bool required = false,
            bool orderable = false,
            bool latestPerKey = false,
            bool multiple = false,
            int maxStringBytes = maxIdentifierBytes) => new(
            name,
            DiagnosticFieldType.String,
            multiple ? DiagnosticFieldCardinality.Multiple : DiagnosticFieldCardinality.Scalar,
            new HashSet<DiagnosticPredicateOperator>
            {
                DiagnosticPredicateOperator.Equal,
                DiagnosticPredicateOperator.In,
                DiagnosticPredicateOperator.Contains
            },
            IsRequired: required,
            IsOrderable: orderable,
            SupportsLatestPerKey: latestPerKey,
            CasePolicy: DiagnosticStringCasePolicy.UnicodeOrdinalIgnoreCase,
            MaxValues: multiple ? maxMultiValues : 1,
            MaxStringBytes: maxStringBytes);

        static DiagnosticFieldDefinition Int64(string name, bool required = false) => new(
            name,
            DiagnosticFieldType.Int64,
            DiagnosticFieldCardinality.Scalar,
            new HashSet<DiagnosticPredicateOperator>
            {
                DiagnosticPredicateOperator.Equal,
                DiagnosticPredicateOperator.In,
                DiagnosticPredicateOperator.RangeInclusive
            },
            IsRequired: required);

        static DiagnosticFieldDefinition Timestamp(
            string name,
            bool required = false,
            bool orderable = false) => new(
            name,
            DiagnosticFieldType.Timestamp,
            DiagnosticFieldCardinality.Scalar,
            new HashSet<DiagnosticPredicateOperator>
            {
                DiagnosticPredicateOperator.Equal,
                DiagnosticPredicateOperator.In,
                DiagnosticPredicateOperator.RangeInclusive
            },
            IsRequired: required,
            IsOrderable: orderable);
    }

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task Query_filters_ordering_limits_and_catalog_capacity_match_across_providers(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var timestamp = new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);

        var telemetryBinding = GroundworkOpenTelemetryBinding.Create("tenant-a", "query-scope", "collector-a");
        await using var telemetryProvider =
            await DiagnosticsGroundworkProviderHarness.CreateOpenTelemetryAsync(provider, telemetryBinding);
        await using var telemetry = new GroundworkOpenTelemetryStore(
            telemetryProvider.Stores,
            Options.Create(new OpenTelemetryDiagnosticsOptions
            {
                ResourceCapacity = 2,
                MetricInstrumentCapacity = 2,
                MaxQuerySize = 20
            }),
            telemetryBinding);
        var resourceOld = Resource("resource-old", "Old", timestamp.AddSeconds(-1));
        var resource = Resource("resource-api", "Orders", timestamp);
        var resourceNew = Resource(
            "resource-new",
            "New",
            timestamp.AddSeconds(1),
            TelemetryResourceStatus.Stale);
        var traceA = Trace("trace-a", resource.Id, timestamp, "process order", SpanStatus.Ok);
        var traceB = Trace("trace-b", resource.Id, timestamp, "process payment", SpanStatus.Error, "workflow-b");
        var instrumentOld = Instrument("instrument-old", resourceOld.Id, "old.duration");
        var instrument = Instrument("instrument-a", resource.Id, "orders.duration");
        var instrumentNew = Instrument("instrument-new", resourceNew.Id, "new.duration");
        var pointA = Point("point-a", instrument, resource.Id, timestamp, traceA.TraceId);
        var pointB = Point("point-b", instrument, resource.Id, timestamp, traceB.TraceId);
        var logA = Log("log-a", resource.Id, timestamp, traceA.TraceId, "span-a", "Information", "order accepted");
        var logB = Log("log-b", resource.Id, timestamp, traceB.TraceId, "span-b", "Error", "payment failed");

        await telemetry.WriteAsync(DiagnosticsDrainBatchId.New(), new(
            [resourceOld, resource, resourceNew],
            [traceA, traceB],
            [
                Span("span-a-record", traceA.TraceId, "span-a", resource.Id, timestamp),
                Span("span-b-record", traceB.TraceId, "span-b", resource.Id, timestamp)
            ],
            [instrumentOld, instrument, instrumentNew],
            [pointA, pointB],
            [logA, logB]));

        var traces = await telemetry.QueryTracesAsync(new()
        {
            TraceId = "TRACE-A",
            ResourceId = resource.Id,
            ServiceName = "ORDERS",
            WorkflowInstanceId = "WORKFLOW-A",
            Status = SpanStatus.Ok,
            From = timestamp,
            To = timestamp,
            Search = "ORDER",
            Take = 10
        });
        var traceIdMatches = await telemetry.QueryTracesAsync(new() { TraceId = "TRACE-A", Take = 10 });
        var workflowMatches = await telemetry.QueryTracesAsync(new() { WorkflowInstanceId = "WORKFLOW-A", Take = 10 });
        var metrics = await telemetry.QueryMetricsAsync(new()
        {
            ResourceId = resource.Id,
            ServiceName = "ORDERS",
            InstrumentName = "DURATION",
            From = timestamp,
            To = timestamp,
            Take = 10
        });
        var logs = await telemetry.QueryLogsAsync(new()
        {
            ResourceId = resource.Id,
            ServiceName = "orders",
            TraceId = "B",
            SpanId = "SPAN-B",
            Severity = "err",
            From = timestamp,
            To = timestamp,
            Search = "FAILED",
            Take = 10
        });
        var detail = await telemetry.GetTraceAsync("TRACE-A");
        var resources = await telemetry.QueryResourcesAsync(new() { Take = 20 });
        var searchedResources = await telemetry.QueryResourcesAsync(new() { Search = "ORDERS", Take = 20 });
        var serviceResources = await telemetry.QueryResourcesAsync(new() { ServiceName = "orders", Take = 20 });
        var activeResources = await telemetry.QueryResourcesAsync(new() { Status = TelemetryResourceStatus.Active, Take = 20 });
        var diagnostics = await telemetry.GetDiagnosticsAsync();

        Assert.Equal([traceA.TraceId], traces.Items.Select(trace => trace.TraceId));
        Assert.Equal([traceA.TraceId], traceIdMatches.Items.Select(trace => trace.TraceId));
        Assert.Equal([traceA.TraceId], workflowMatches.Items.Select(trace => trace.TraceId));
        Assert.Equal([pointA.Id, pointB.Id], metrics.Points.Select(point => point.Id));
        Assert.Equal([logB.Id], logs.Items.Select(log => log.Id));
        Assert.Equal(["span-a"], detail!.Spans.Select(span => span.SpanId));
        Assert.Equal(["resource-new", "resource-api"], resources.Items.Select(item => item.Id));
        Assert.Equal([resource.Id], searchedResources.Items.Select(item => item.Id));
        Assert.Equal([resource.Id], serviceResources.Items.Select(item => item.Id));
        Assert.Equal([resource.Id], activeResources.Items.Select(item => item.Id));
        Assert.Equal((2, 2), (diagnostics.ResourceCount, diagnostics.MetricInstrumentCount));
    }

    private static OpenTelemetryBatch OpenTelemetryBatch()
    {
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-1);
        var resource = new TelemetryResource(
            "resource-1", "api", "api-1", "dotnet", new Dictionary<string, string?>(), timestamp,
            TelemetryResourceStatus.Active);
        var trace = new TelemetryTrace(
            "trace-1", "span-root", "request", timestamp, timestamp.AddMilliseconds(10),
            TimeSpan.FromMilliseconds(10), SpanStatus.Ok, [resource.Id], [], 1);
        var span = new TelemetrySpan(
            "span-record-1", trace.TraceId, "span-1", null, resource.Id, "request", "internal",
            timestamp, timestamp.AddMilliseconds(10), SpanStatus.Ok, null,
            new Dictionary<string, string?>(), [], []);
        var instrument = new MetricInstrument(
            "instrument-1", resource.Id, "request.duration", "ms", null, MetricKind.Gauge,
            new Dictionary<string, string?>());
        var point = new MetricPoint(
            "point-1", instrument.Id, instrument.Name, resource.Id, timestamp, 10, null, null,
            new Dictionary<string, string?>(), trace.TraceId, span.SpanId);
        var log = new OtlpLogRecord(
            "log-1", resource.Id, timestamp, "Information", null, "request completed", trace.TraceId,
            span.SpanId, new Dictionary<string, string?>());
        return new([resource], [trace], [span], [instrument], [point], [log]);
    }

    private static TelemetryResource Resource(
        string id,
        string serviceName,
        DateTimeOffset lastSeen,
        TelemetryResourceStatus status = TelemetryResourceStatus.Active) =>
        new(id, serviceName, null, "dotnet", new Dictionary<string, string?>(), lastSeen, status);

    private static TelemetryTrace Trace(
        string id,
        string resourceId,
        DateTimeOffset timestamp,
        string name,
        SpanStatus status,
        string workflowInstanceId = "workflow-a") =>
        new(id, null, name, timestamp, timestamp, TimeSpan.Zero, status, [resourceId], [workflowInstanceId], 1);

    private static TelemetrySpan Span(
        string id,
        string traceId,
        string spanId,
        string resourceId,
        DateTimeOffset timestamp) =>
        new(id, traceId, spanId, null, resourceId, "operation", "internal", timestamp, timestamp,
            SpanStatus.Ok, null, new Dictionary<string, string?>(), [], []);

    private static MetricInstrument Instrument(string id, string resourceId, string name) =>
        new(id, resourceId, name, "ms", null, MetricKind.Gauge, new Dictionary<string, string?>());

    private static MetricPoint Point(
        string id,
        MetricInstrument instrument,
        string resourceId,
        DateTimeOffset timestamp,
        string traceId) =>
        new(id, instrument.Id, instrument.Name, resourceId, timestamp, 1, null, null,
            new Dictionary<string, string?>(), traceId, null);

    private static OtlpLogRecord Log(
        string id,
        string resourceId,
        DateTimeOffset timestamp,
        string traceId,
        string spanId,
        string severity,
        string body) =>
        new(id, resourceId, timestamp, severity, null, body, traceId, spanId, new Dictionary<string, string?>());

    private static string GroupString(DiagnosticRecordGroup group, string field) =>
        Assert.Single(group.Fields[field]).CanonicalValue;

    private sealed class AcknowledgementLosingRecordStore(IDiagnosticRecordStore inner) : IDiagnosticRecordStore
    {
        private int _loseAcknowledgement = 1;
        public int AppendCalls { get; private set; }
        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;

        public async ValueTask<DiagnosticAppendResult> AppendAsync(
            DiagnosticRecordBatch batch,
            CancellationToken cancellationToken = default)
        {
            AppendCalls++;
            var result = await inner.AppendAsync(batch, cancellationToken);
            if (Interlocked.Exchange(ref _loseAcknowledgement, 0) == 1)
                throw new DiagnosticAcknowledgementLostException(
                    DiagnosticOperationKind.Append,
                    batch.Stream,
                    batch.OperationId);
            return result;
        }
    }

    private sealed class QueryFailingRecordStore(IDiagnosticRecordStore inner, Exception failure)
        : IDiagnosticRecordStore
    {
        public bool FailQueries { get; set; }
        public DiagnosticRecordStoreHandlers Handlers => inner.Handlers;

        public ValueTask<DiagnosticRecordPage> QueryAsync(
            DiagnosticRecordQuery query,
            CancellationToken cancellationToken = default) =>
            FailQueries
                ? ValueTask.FromException<DiagnosticRecordPage>(failure)
                : inner.QueryAsync(query, cancellationToken);
    }
}
