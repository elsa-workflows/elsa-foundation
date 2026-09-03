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
}
