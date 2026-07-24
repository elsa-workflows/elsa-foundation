using Elsa.Diagnostics.OpenTelemetry.Core.Exceptions;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
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
    public async Task Structured_log_commit_cursor_and_high_water_survive_restart(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var binding = new StructuredLogStoreBinding("tenant-a", "shell-a", "structured-logs");
        StructuredLogEntry committed;

        await using (var firstProvider = await DiagnosticsGroundworkProviderHarness.CreateStructuredLogsAsync(provider, binding))
        await using (var firstStore = new GroundworkStructuredLogStore(
                         firstProvider.Store,
                         Options.Create(new StructuredLogsOptions()),
                         binding))
        {
            firstStore.Start();
            committed = await firstStore.AppendAsync(StructuredLogEntry());
            Assert.NotNull(committed.ReplayCursor);
        }

        await using var restartedProvider = await DiagnosticsGroundworkProviderHarness.CreateStructuredLogsAsync(provider, binding);
        await using var restartedStore = new GroundworkStructuredLogStore(
            restartedProvider.Store,
            Options.Create(new StructuredLogsOptions()),
            binding);
        restartedStore.Start();
        var recent = await restartedStore.GetRecentAsync(StructuredLogFilter.None);
        var page = await restartedStore.ReadAfterAsync(null, StructuredLogFilter.None, 10);

        Assert.Equal(1, await restartedStore.GetHighWaterMarkAsync());
        Assert.Equal("persisted", Assert.Single(recent).Message);
        Assert.Equal(committed.ReplayCursor, Assert.Single(page.Entries).ReplayCursor);
    }

    [Theory]
    [MemberData(nameof(ProviderKinds))]
    public async Task Structured_log_replay_retry_and_failure_semantics_match_across_providers(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var binding = new StructuredLogStoreBinding("tenant-a", "shell-a", "structured-logs");
        await using var firstProvider =
            await DiagnosticsGroundworkProviderHarness.CreateStructuredLogsAsync(provider, binding);
        var acknowledgementLoss = new AcknowledgementLosingRecordStore(firstProvider.Store);
        await using var first = StartStructuredLogs(acknowledgementLoss, binding);
        await using var secondProvider =
            await DiagnosticsGroundworkProviderHarness.CreateStructuredLogsAsync(provider, binding);
        await using var second = StartStructuredLogs(secondProvider.Store, binding);
        var timestamp = new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero);

        var retried = await first.AppendAsync(StructuredLogEntry(1, "retried", timestamp));
        Assert.Equal(2, acknowledgementLoss.AppendCalls);
        var tied = await Task.WhenAll(
            first.AppendAsync(StructuredLogEntry(2, "writer-a", timestamp)).AsTask(),
            second.AppendAsync(StructuredLogEntry(2, "writer-b", timestamp)).AsTask());
        var recent = (await first.GetRecentAsync(StructuredLogFilter.None)).ToArray();
        var replay = await first.ReadAfterAsync(recent[0].ReplayCursor, StructuredLogFilter.None, 10);

        Assert.Equal(3, recent.Length);
        Assert.Equal(recent.Skip(1).Select(entry => entry.ReplayCursor), replay.Entries.Select(entry => entry.ReplayCursor));
        Assert.Equal(3, recent.Select(entry => entry.ReplayCursor).Distinct().Count());
        Assert.Equal(2, tied.Select(entry => entry.ReplayCursor).Distinct().Count());

        var filtered = await first.ReadAfterAsync(
            retried.ReplayCursor,
            new StructuredLogFilter { SourceId = "selected" },
            1);
        Assert.Empty(filtered.Entries);
        Assert.NotNull(filtered.NextCursor);
        Assert.True(filtered.HasMore);

        await first.TrimAsync(0);
        Assert.Equal(2, await first.GetHighWaterMarkAsync());
        await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            first.ReadAfterAsync(retried.ReplayCursor, StructuredLogFilter.None, 10));

        var foreignBinding = new StructuredLogStoreBinding("tenant-b", "shell-a", "structured-logs");
        await using var foreignProvider =
            await DiagnosticsGroundworkProviderHarness.CreateStructuredLogsAsync(provider, foreignBinding);
        await using var foreign = StartStructuredLogs(foreignProvider.Store, foreignBinding);
        var foreignError = await Assert.ThrowsAsync<StructuredLogReplayCursorUnavailableException>(() =>
            foreign.ReadAfterAsync(retried.ReplayCursor, StructuredLogFilter.None, 10));
        Assert.Equal("The structured log replay cursor is unavailable.", foreignError.Message);

        var queryFailure = new StructuredLogsException("The diagnostics database is unavailable.");
        await using var failingProvider =
            await DiagnosticsGroundworkProviderHarness.CreateStructuredLogsAsync(provider, binding);
        var failingRecords = new QueryFailingRecordStore(failingProvider.Store, queryFailure);
        await using var failing = StartStructuredLogs(failingRecords, binding);
        var committed = await failing.AppendAsync(StructuredLogEntry(3, "query-failure", timestamp));
        failingRecords.FailQueries = true;
        var operational = await Assert.ThrowsAsync<StructuredLogsException>(() =>
            failing.ReadAfterAsync(committed.ReplayCursor, StructuredLogFilter.None, 10));
        Assert.Same(queryFailure, operational);
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
    public async Task Query_filters_ordering_limits_and_catalog_capacity_match_across_providers(
        DiagnosticsProviderKind providerKind)
    {
        var provider = await providers.CreateIsolatedAsync(providerKind);
        var structuredBinding = new StructuredLogStoreBinding("tenant-a", "query-scope", "structured-logs");
        await using var structuredProvider =
            await DiagnosticsGroundworkProviderHarness.CreateStructuredLogsAsync(provider, structuredBinding);
        await using var structured = StartStructuredLogs(structuredProvider.Store, structuredBinding);
        var timestamp = new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);

        await structured.AppendAsync(StructuredLogEntry(1, "debug", timestamp) with
        {
            Level = LogLevel.Debug,
            Category = "Other",
            SourceId = "selected"
        });
        await structured.AppendAsync(StructuredLogEntry(2, "warning", timestamp) with
        {
            Level = LogLevel.Warning,
            Category = "Orders",
            SourceId = "selected"
        });
        await structured.AppendAsync(StructuredLogEntry(2, "error", timestamp) with
        {
            Level = LogLevel.Error,
            Category = "Orders",
            SourceId = "selected"
        });
        await structured.AppendAsync(StructuredLogEntry(3, "foreign-source", timestamp) with
        {
            Level = LogLevel.Critical,
            Category = "Orders",
            SourceId = "other"
        });

        var structuredResult = await structured.GetRecentAsync(new()
        {
            MinimumLevel = LogLevel.Warning,
            Category = "Orders",
            SourceId = "selected",
            MaxCount = 2
        });
        Assert.Equal(["warning", "error"], structuredResult.Select(entry => entry.Message));

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
        var resourceNew = Resource("resource-new", "New", timestamp.AddSeconds(1));
        var traceA = Trace("trace-a", resource.Id, timestamp, "process order", SpanStatus.Ok);
        var traceB = Trace("trace-b", resource.Id, timestamp, "process payment", SpanStatus.Error);
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
            ResourceId = resource.Id,
            ServiceName = "ORDERS",
            Status = SpanStatus.Ok,
            From = timestamp,
            To = timestamp,
            Search = "ORDER",
            Take = 10
        });
        var metrics = await telemetry.QueryMetricsAsync(new()
        {
            ResourceId = resource.Id,
            InstrumentName = "DURATION",
            From = timestamp,
            To = timestamp,
            Take = 10
        });
        var logs = await telemetry.QueryLogsAsync(new()
        {
            ResourceId = resource.Id,
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
        var diagnostics = await telemetry.GetDiagnosticsAsync();

        Assert.Equal([traceA.TraceId], traces.Items.Select(trace => trace.TraceId));
        Assert.Equal([pointA.Id, pointB.Id], metrics.Points.Select(point => point.Id));
        Assert.Equal([logB.Id], logs.Items.Select(log => log.Id));
        Assert.Equal(["span-a"], detail!.Spans.Select(span => span.SpanId));
        Assert.Equal(["resource-new", "resource-api"], resources.Items.Select(item => item.Id));
        Assert.Equal([resource.Id], searchedResources.Items.Select(item => item.Id));
        Assert.Equal([resource.Id], serviceResources.Items.Select(item => item.Id));
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

    private static StructuredLogEntry StructuredLogEntry() =>
        StructuredLogEntry(1, "persisted", DateTimeOffset.UtcNow.AddMinutes(-1));

    private static StructuredLogEntry StructuredLogEntry(
        long sequence,
        string message,
        DateTimeOffset timestamp) => new()
        {
            Sequence = sequence,
            Timestamp = timestamp,
            Level = LogLevel.Information,
            Category = "Provider.Conformance",
            Message = message,
            SourceId = "provider-matrix"
        };

    private static GroundworkStructuredLogStore StartStructuredLogs(
        IDiagnosticRecordStore records,
        StructuredLogStoreBinding binding)
    {
        var store = new GroundworkStructuredLogStore(
            records,
            Options.Create(new StructuredLogsOptions()),
            binding);
        store.Start();
        return store;
    }

    private static TelemetryResource Resource(string id, string serviceName, DateTimeOffset lastSeen) =>
        new(id, serviceName, null, "dotnet", new Dictionary<string, string?>(), lastSeen, TelemetryResourceStatus.Active);

    private static TelemetryTrace Trace(
        string id,
        string resourceId,
        DateTimeOffset timestamp,
        string name,
        SpanStatus status) =>
        new(id, null, name, timestamp, timestamp, TimeSpan.Zero, status, [resourceId], ["workflow-a"], 1);

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
