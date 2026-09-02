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
    public void Diagnostics_remains_blocked_until_the_absolute_budget_is_ratified()
    {
        var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[
            DiagnosticsDurableHistoryWorkload.WorkloadId];

        Assert.True(BenchmarkAdmissionGuard.TryGetBlockedReason(workload, out var reason));
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, reason);
    }
}
