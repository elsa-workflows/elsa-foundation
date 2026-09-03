using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class DiagnosticsDurableHistoryWorkloadTests
{
    [Fact]
    public void Frozen_sequence_and_golden_vectors_match_the_catalog()
    {
        var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[
            DiagnosticsDurableHistoryWorkload.WorkloadId];

        Assert.Equal(workload.OperationSequence, DiagnosticsDurableHistoryWorkload.OperationIds);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ExpectedInputFingerprint, workload.Input.FingerprintSha256);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ExpectedResultDigest, workload.Correctness.ResultDigestSha256);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ExpectedInputFingerprint,
            ReproducibleWorkloadScenarioCatalog.GoldenVectors[DiagnosticsDurableHistoryWorkload.WorkloadId].InputFingerprint);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ExpectedResultDigest,
            ReproducibleWorkloadScenarioCatalog.GoldenVectors[DiagnosticsDurableHistoryWorkload.WorkloadId].ResultDigest);
    }

    [Fact]
    public void Native_route_contract_carries_frozen_cardinalities_and_limit()
    {
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ResourceCount,
            DiagnosticsDurableHistoryWorkload.NativeRouteCardinalities["resources-by-last-seen"]);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ResourceCount,
            DiagnosticsDurableHistoryWorkload.NativeRouteCardinalities["resources-by-status"]);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ResourceCount,
            DiagnosticsDurableHistoryWorkload.NativeRouteCardinalities["resources-by-service"]);
        Assert.All(DiagnosticsDurableHistoryWorkload.NativeRouteLimits,
            route => Assert.InRange(route.Value, 1, DiagnosticsDurableHistoryWorkload.NativeRouteCardinalities[route.Key] - 1));
    }

    [Fact]
    public void Native_plan_fixture_uses_bounded_batches_with_exact_catalog_and_trace_detail_totals()
    {
        const int recordCount = 2_500;
        const int batchSize = 1_000;
        var batches = DiagnosticsDurableHistoryWorkload
            .OpenTelemetryBatches(recordCount, bindSignalsToLatestTrace: true, batchSize)
            .ToArray();
        var selectedTrace = DiagnosticsDurableHistoryWorkload.TraceIdForTesting(
            DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream - 1);

        Assert.Equal(
            (recordCount + batchSize - 1) / batchSize,
            batches.Length);
        Assert.Equal(recordCount, batches.Sum(batch => batch.Traces.Count));
        Assert.Equal(recordCount, batches.Sum(batch => batch.Spans.Count));
        Assert.Equal(recordCount, batches.Sum(batch => batch.MetricPoints.Count));
        Assert.Equal(recordCount, batches.Sum(batch => batch.Logs.Count));
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ResourceCount, batches.Sum(batch => batch.Resources.Count));
        Assert.Equal(DiagnosticsDurableHistoryWorkload.InstrumentCount, batches.Sum(batch => batch.Instruments.Count));
        Assert.All(batches.SelectMany(batch => batch.Spans), span => Assert.Equal(selectedTrace, span.TraceId));
        Assert.All(batches.SelectMany(batch => batch.Logs), record => Assert.Equal(selectedTrace, record.TraceId));

        var nativeFirst = DiagnosticsDurableHistoryWorkload.NativePlanFixtureBatches().First();
        Assert.Equal(DiagnosticsDurableHistoryWorkload.NormalizedRecordsPerOtlpBatch, nativeFirst.Traces.Count);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.NormalizedRecordsPerOtlpBatch, nativeFirst.Spans.Count);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.NormalizedRecordsPerOtlpBatch, nativeFirst.MetricPoints.Count);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.NormalizedRecordsPerOtlpBatch, nativeFirst.Logs.Count);
    }

    [Fact]
    public void Diagnostics_remains_blocked_until_the_absolute_budget_is_ratified()
    {
        var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[
            DiagnosticsDurableHistoryWorkload.WorkloadId];

        Assert.True(BenchmarkAdmissionGuard.TryGetBlockedReason(workload, out var reason));
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, reason);
    }

    [Fact]
    public async Task Prepares_all_fifteen_frozen_operations_in_catalog_order()
    {
        var adapter = new MeasuredAdapter();

        var operations = await new DiagnosticsDurableHistoryWorkload().PrepareMeasuredOperationsAsync(adapter);

        Assert.Equal(DiagnosticsDurableHistoryWorkload.OperationIds, operations.Select(operation => operation.Id));
    }

    [Fact]
    public async Task Mutation_setup_runs_before_the_timed_invocation()
    {
        var adapter = new MeasuredAdapter();
        var operations = await new DiagnosticsDurableHistoryWorkload().PrepareMeasuredOperationsAsync(adapter);
        var operation = Assert.Single(operations, operation => operation.Id == "append-structured-log-batches");
        var timed = false;

        await ProcessMeasurement.InvokeOnceForTestAsync(
            new AdapterOperation(operation),
            7,
            () =>
            {
                timed = true;
                Assert.Equal(["trim"], adapter.PrimaryStructuredLog.Calls);
            },
            CancellationToken.None);

        Assert.True(timed);
        Assert.Equal("append", adapter.PrimaryStructuredLog.Calls[^1]);
    }

    [Fact]
    public async Task Adjacent_open_telemetry_invocations_use_disjoint_signal_identities()
    {
        var adapter = new MeasuredAdapter();
        var operations = await new DiagnosticsDurableHistoryWorkload().PrepareMeasuredOperationsAsync(adapter);
        var operation = Assert.Single(operations, operation => operation.Id == "append-open-telemetry-batches");

        await operation.PrepareInvocationAsync(7);
        await operation.InvokeAsync(7);
        await operation.PrepareInvocationAsync(8);
        await operation.InvokeAsync(8);

        Assert.Empty(adapter.PrimaryOpenTelemetry.Batches);
        Assert.Equal(16, adapter.SecondaryOpenTelemetry.Batches.Count);
        var firstTimed = adapter.SecondaryOpenTelemetry.Batches[7].Traces.Select(trace => trace.TraceId);
        var secondTimed = adapter.SecondaryOpenTelemetry.Batches[15].Traces.Select(trace => trace.TraceId);
        Assert.Empty(firstTimed.Intersect(secondTimed, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Reopen_operations_open_a_fresh_client_inside_each_invocation()
    {
        var adapter = new MeasuredAdapter();
        var operations = await new DiagnosticsDurableHistoryWorkload().PrepareMeasuredOperationsAsync(adapter);
        var operation = Assert.Single(operations, operation => operation.Id == "reopen-and-read-structured-log-high-water");

        await ProcessMeasurement.InvokeOnceForTestAsync(
            new AdapterOperation(operation), 1, static () => { }, CancellationToken.None);
        await ProcessMeasurement.InvokeOnceForTestAsync(
            new AdapterOperation(operation), 2, static () => { }, CancellationToken.None);

        Assert.Equal(2, adapter.ReopenCount);
        Assert.Equal(3, adapter.ResetReopenedCount);
    }

    private sealed class AdapterOperation(IDiagnosticsDurableHistoryWorkloadOperation operation) : IBenchmarkOperation
    {
        public string Id => operation.Id;

        public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
            operation.PrepareInvocationAsync(invocation, cancellationToken).AsTask();

        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) =>
            operation.InvokeAsync(invocation, cancellationToken).AsTask();
    }

    private sealed class MeasuredAdapter : IDiagnosticsDurableHistoryWorkloadAdapter
    {
        public RecordingStructuredLogStore PrimaryStructuredLog { get; } = new();
        private readonly RecordingStructuredLogStore secondaryStructuredLog = new();
        public RecordingOpenTelemetryStore PrimaryOpenTelemetry { get; } = new();
        public RecordingOpenTelemetryStore SecondaryOpenTelemetry { get; } = new();
        private readonly DiagnosticsDurableHistoryClient reopened;
        private readonly DiagnosticsDurableHistoryClient primary;
        private readonly DiagnosticsDurableHistoryClient secondary;

        public MeasuredAdapter()
        {
            primary = new(PrimaryStructuredLog, PrimaryOpenTelemetry);
            secondary = new(secondaryStructuredLog, SecondaryOpenTelemetry);
            reopened = new(new RecordingStructuredLogStore(), new RecordingOpenTelemetryStore());
        }

        public ValueTask<DiagnosticsDurableHistoryScopes> OpenScopedClientsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DiagnosticsDurableHistoryScopes(primary, secondary));

        public ValueTask FlushAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public int ReopenCount { get; private set; }
        public int ResetReopenedCount { get; private set; }

        public ValueTask<DiagnosticsDurableHistoryClient> ReopenClientAsync(CancellationToken cancellationToken = default)
        {
            ReopenCount++;
            return ValueTask.FromResult(reopened);
        }

        public ValueTask ResetReopenedClientsAsync(CancellationToken cancellationToken = default)
        {
            ResetReopenedCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingStructuredLogStore : IStructuredLogStore
    {
        public List<string> Calls { get; } = [];

        public ValueTask<StructuredLogEntry> AppendAsync(StructuredLogEntry entry, CancellationToken cancellationToken = default)
        {
            Calls.Add("append");
            return ValueTask.FromResult(entry with { ReplayCursor = new StructuredLogReplayCursor("recording") });
        }

        public Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);

        public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(StructuredLogFilter filter, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StructuredLogEntry>>([]);

        public Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<StructuredLogReplayCursor?>(null);

        public Task<StructuredLogReadPage> ReadAfterAsync(StructuredLogReplayCursor? afterCursor, StructuredLogFilter filter, int maxCount, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StructuredLogReadPage([], afterCursor, false));

        public Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default)
        {
            Calls.Add("trim");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOpenTelemetryStore : IOpenTelemetryStore
    {
        public List<OpenTelemetryBatch> Batches { get; } = [];

        public ValueTask WriteAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken = default)
        {
            Batches.Add(batch);
            return ValueTask.CompletedTask;
        }
        public ValueTask<OpenTelemetryResourceResult> QueryResourcesAsync(OpenTelemetryResourceFilter filter, CancellationToken cancellationToken = default) => ValueTask.FromResult(new OpenTelemetryResourceResult([], 0));
        public ValueTask<OpenTelemetryTraceResult> QueryTracesAsync(OpenTelemetryTraceFilter filter, CancellationToken cancellationToken = default) => ValueTask.FromResult(new OpenTelemetryTraceResult([], 0));
        public ValueTask<OpenTelemetryTraceDetail?> GetTraceAsync(string traceId, CancellationToken cancellationToken = default) => ValueTask.FromResult<OpenTelemetryTraceDetail?>(null);
        public ValueTask<OpenTelemetryMetricResult> QueryMetricsAsync(OpenTelemetryMetricFilter filter, CancellationToken cancellationToken = default) => ValueTask.FromResult(new OpenTelemetryMetricResult([], [], 0));
        public ValueTask<OpenTelemetryLogResult> QueryLogsAsync(OpenTelemetryLogFilter filter, CancellationToken cancellationToken = default) => ValueTask.FromResult(new OpenTelemetryLogResult([], 0));
        public ValueTask<OpenTelemetryStorageDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(new OpenTelemetryStorageDiagnostics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public async Task Retained_count_terminates_when_the_store_clamps_pages_to_the_frozen_query_limit()
    {
        var store = new ClampedTailStore(
            DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
            DiagnosticsDurableHistoryWorkload.QueryLimit);

        var count = await DiagnosticsDurableHistoryWorkload.CountRetainedAsync(store, CancellationToken.None);

        Assert.Equal(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, count);
        Assert.Equal(
            (DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream + DiagnosticsDurableHistoryWorkload.QueryLimit - 1) /
            DiagnosticsDurableHistoryWorkload.QueryLimit,
            store.ReadCount);
    }

    [Fact]
    public async Task Retained_count_rejects_a_nonadvancing_cursor_immediately()
    {
        var store = new NonAdvancingTailStore();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DiagnosticsDurableHistoryWorkload.CountRetainedAsync(store, CancellationToken.None));

        Assert.Contains("without advancing its cursor", exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, store.ReadCount);
    }

    private sealed class ClampedTailStore(int total, int clamp) : StructuredLogStoreStub
    {
        private static readonly StructuredLogEntry Entry = new();

        public int ReadCount { get; private set; }

        public override Task<StructuredLogReadPage> ReadAfterAsync(
            StructuredLogReplayCursor? afterCursor,
            StructuredLogFilter filter,
            int maxCount,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            var position = afterCursor is null ? 0 : int.Parse(afterCursor.Value.Value);
            var take = Math.Min(Math.Min(maxCount, clamp), total - position);
            var nextPosition = position + take;
            StructuredLogReplayCursor? next = take == 0
                ? afterCursor
                : new(nextPosition.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return Task.FromResult(new StructuredLogReadPage(
                Enumerable.Repeat(Entry, take).ToArray(),
                next,
                nextPosition < total));
        }
    }

    private sealed class NonAdvancingTailStore : StructuredLogStoreStub
    {
        private static readonly StructuredLogReplayCursor Cursor = new("unchanged");
        private static readonly StructuredLogEntry Entry = new();

        public int ReadCount { get; private set; }

        public override Task<StructuredLogReadPage> ReadAfterAsync(
            StructuredLogReplayCursor? afterCursor,
            StructuredLogFilter filter,
            int maxCount,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            return Task.FromResult(new StructuredLogReadPage([Entry], Cursor, true));
        }
    }

    private abstract class StructuredLogStoreStub : IStructuredLogStore
    {
        public ValueTask<StructuredLogEntry> AppendAsync(
            StructuredLogEntry entry,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<long> GetHighWaterMarkAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StructuredLogEntry>> GetRecentAsync(
            StructuredLogFilter filter,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StructuredLogReplayCursor?> GetTailCursorAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public abstract Task<StructuredLogReadPage> ReadAfterAsync(
            StructuredLogReplayCursor? afterCursor,
            StructuredLogFilter filter,
            int maxCount,
            CancellationToken cancellationToken = default);

        public Task TrimAsync(int keepNewest, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
