using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
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
