using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class DiagnosticsDurableHistoryAdapterTests
{
    [Fact]
    public async Task Dispatches_to_the_exact_groundwork_diagnostics_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request(),
            "unused",
            "unused");

        Assert.IsType<DiagnosticsDurableHistoryAdapter>(adapter);
    }

    [Fact]
    public async Task Dispatches_to_the_temporary_sqlite_ef_diagnostics_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request(
                adapter: EfDiagnosticsDurableHistoryAdapter.AdapterId,
                physicalForm: EfDiagnosticsDurableHistoryAdapter.PhysicalForm),
            "unused",
            "unused");

        Assert.IsType<EfDiagnosticsDurableHistoryAdapter>(adapter);
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mongodb")]
    public async Task Temporary_ef_diagnostics_comparator_refuses_non_sqlite_before_provider_open(string provider)
    {
        await using var adapter = new EfDiagnosticsDurableHistoryAdapter(
            Request(
                provider,
                adapter: EfDiagnosticsDurableHistoryAdapter.AdapterId,
                physicalForm: EfDiagnosticsDurableHistoryAdapter.PhysicalForm),
            "unused",
            "unused");

        var exception = await Assert.ThrowsAsync<PerformanceContractException>(() => adapter.PrepareAsync(CancellationToken.None));

        Assert.Contains("only supports sqlite", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Timed_operations_remain_closed_while_the_absolute_budget_gate_is_blocked()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(Request(), "unused", "unused");

        var exception = Assert.Throws<PerformanceContractException>(() => adapter.Operations);

        Assert.Contains(ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Frozen_sequence_and_native_route_cardinalities_match_the_catalog()
    {
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads[
            DiagnosticsDurableHistoryWorkload.WorkloadId];

        Assert.Equal(workload.OperationSequence, DiagnosticsDurableHistoryWorkload.OperationIds);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ExpectedInputFingerprint, workload.Input.FingerprintSha256);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ExpectedResultDigest, workload.Correctness.ResultDigestSha256);
        Assert.Equal(
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["resources-by-last-seen"] = DiagnosticsDurableHistoryWorkload.ResourceCount,
                ["resources-by-status"] = DiagnosticsDurableHistoryWorkload.ResourceCount,
                ["resources-by-service"] = DiagnosticsDurableHistoryWorkload.ResourceCount,
                ["traces-by-last-seen"] = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                ["trace-detail"] = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                ["metrics-by-last-seen"] = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                ["logs-by-last-seen"] = DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream,
                ["structured-log-recent"] = DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream,
                ["structured-log-replay"] = DiagnosticsDurableHistoryWorkload.AppendedRecordsPerStream
            },
            DiagnosticsDurableHistoryWorkload.NativeRouteCardinalities);
    }

    [Fact]
    public void Diagnostics_admission_retains_the_unratified_absolute_budget_reason()
    {
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads[
            DiagnosticsDurableHistoryWorkload.WorkloadId];

        Assert.True(BenchmarkAdmissionGuard.TryGetBlockedReason(workload, out var reason));
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, reason);
    }

    [Fact]
    public void Trace_detail_artifacts_are_partitioned_by_their_exact_logical_index()
    {
        var directory = Directory.CreateTempSubdirectory("diagnostics-native-pages-");
        try
        {
            var before = Path.Combine(directory.FullName, "000001-sqlite-optimizer-selected-before.txt");
            var spanFirst = Path.Combine(directory.FullName, "000002-sqlite-optimizer-selected-elsa_otel_spans_trace_detail.txt");
            var log = Path.Combine(directory.FullName, "000003-sqlite-optimizer-selected-elsa_otel_logs_trace_detail.txt");
            var spanSecond = Path.Combine(directory.FullName, "000004-sqlite-optimizer-selected-elsa_otel_spans_trace_detail.txt");
            File.WriteAllText(before, "before");
            File.WriteAllText(spanFirst, "span one");
            File.WriteAllText(log, "log");
            File.WriteAllText(spanSecond, "span two");

            var actual = DiagnosticsNativePlanCapture.RequireNativeArtifacts(
                directory.FullName,
                new HashSet<string>(StringComparer.Ordinal) { before },
                "sqlite",
                "elsa_otel_spans_trace_detail",
                2);

            Assert.Equal([spanFirst, spanSecond], actual);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public void Process_identity_is_bound_into_storage_and_diagnostic_scopes()
    {
        var first = Request(processIndex: 1);
        var second = Request(processIndex: 2);

        Assert.NotEqual(
            DiagnosticsDurableHistoryAdapter.PersistenceScopeForTesting(first),
            DiagnosticsDurableHistoryAdapter.PersistenceScopeForTesting(second));
        Assert.NotEqual(
            DiagnosticsDurableHistoryAdapter.BindingScopeForTesting(first, "primary"),
            DiagnosticsDurableHistoryAdapter.BindingScopeForTesting(second, "primary"));
        Assert.InRange(
            DiagnosticsDurableHistoryAdapter.BindingTenantForTesting(first, "primary").Length,
            1,
            64);
        Assert.InRange(
            DiagnosticsDurableHistoryAdapter.BindingStorageScopeForTesting(first, "primary").Length,
            1,
            64);
    }

    private static RunRequest Request(
        string provider = "sqlite",
        string adapter = DiagnosticsDurableHistoryAdapter.AdapterId,
        string physicalForm = DiagnosticsDurableHistoryAdapter.PhysicalForm,
        int processIndex = 1) => new(
        ComparisonCohortId: "cohort",
        MeasurementSetId: "set",
        WorkloadId: DiagnosticsDurableHistoryWorkload.WorkloadId,
        WorkloadVersion: DiagnosticsDurableHistoryWorkload.Version,
        Provider: provider,
        ProviderVersion: "3.46.0",
        ProviderTopology: "file-backed-distinct-connections",
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal),
        Adapter: adapter,
        PhysicalForm: physicalForm,
        Scale: "small",
        CommitSha: new string('a', 40),
        HarnessAssemblySha256: new string('b', 64),
        PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal),
        CompositionFingerprint: new string('c', 64),
        HostFingerprintSha256: new string('d', 64),
        Seed: DiagnosticsDurableHistoryWorkload.Seed,
        InputFingerprintSha256: DiagnosticsDurableHistoryWorkload.ExpectedInputFingerprint,
        NativePlanIdentity: "diagnostics-plan",
        NativePlanEvidenceReference: "diagnostics-plan.json",
        NativePlanContentSha256: new string('e', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: processIndex);
}
