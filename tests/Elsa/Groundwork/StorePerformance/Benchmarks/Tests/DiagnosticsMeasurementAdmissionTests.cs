using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class DiagnosticsMeasurementAdmissionTests
{
    [Fact]
    public void Diagnostics_first_measurement_plan_is_admitted_without_promoting_the_workload()
    {
        var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[
            ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];

        var plan = MatrixPlan.Create(workload, Request(workload));

        Assert.Equal(
            [ProcessKind.Warmup, ProcessKind.Measured, ProcessKind.Measured, ProcessKind.Measured],
            plan.Runs.Select(run => run.ProcessKind));
        Assert.Equal([0, 1, 2, 3], plan.Runs.Select(run => run.ProcessIndex));
        Assert.True(BenchmarkAdmissionGuard.TryGetBlockedReason(workload, out var reason));
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, reason);
        Assert.Throws<PerformanceContractException>(() => BenchmarkAdmissionGuard.RequireReady(workload));
    }

    [Fact]
    public async Task Diagnostics_matrix_produces_one_ungraded_measurement_without_promoting_the_workload()
    {
        using var fixture = DiagnosticsMatrixFixture.Create();

        await ProcessMatrixRunner.RunForTestAsync(
            fixture.Plan,
            fixture.OutputDirectory,
            fixture.WriteArtifactAsync,
            CancellationToken.None);

        var measurement = Measurement.Measure(fixture.OutputDirectory, fixture.Catalog);

        Assert.Equal(MeasurementResultStatus.Ungraded, measurement.EvaluationStatus);
        Assert.True(measurement.Complete);
        Assert.True(measurement.CorrectnessValid);
        Assert.Null(measurement.BlockReason);
    }

    [Fact]
    public async Task Diagnostics_matrix_rejects_a_second_ungraded_measurement_set_before_starting_a_child()
    {
        using var fixture = DiagnosticsMatrixFixture.Create();

        await ProcessMatrixRunner.RunForTestAsync(
            fixture.Plan,
            fixture.OutputDirectory,
            fixture.WriteArtifactAsync,
            CancellationToken.None);

        var childCalled = false;
        var error = await Assert.ThrowsAsync<PerformanceContractException>(() =>
            ProcessMatrixRunner.RunForTestAsync(
                fixture.PlanFor("diagnostics-set-2", "diagnostics-set-2.native-plan.json"),
                fixture.OutputDirectory,
                (_, _, _) =>
                {
                    childCalled = true;
                    return Task.FromResult(0);
                },
                CancellationToken.None));

        Assert.Contains(ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, error.Message, StringComparison.Ordinal);
        Assert.False(childCalled);
    }

    [Fact]
    public void Diagnostics_comparison_phase_remains_blocked_after_measurement_admission()
    {
        var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[
            ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];

        var exception = Assert.Throws<PerformanceContractException>(() =>
            BenchmarkAdmissionGuard.RequireForPhase(workload, BenchmarkPhase.Comparison));

        Assert.Contains(
            ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostics_ef_oracle_is_not_admitted_as_a_timed_measurement_target()
    {
        var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[
            ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];

        var exception = Assert.Throws<PerformanceContractException>(() =>
            MatrixPlan.Create(workload, Request(
                workload,
                "ef-diagnostics-oracle",
                "efcore-diagnostics-relational-tables",
                new Dictionary<string, string>
                {
                    ["Microsoft.EntityFrameworkCore"] = "10.0.8",
                    ["Microsoft.EntityFrameworkCore.Sqlite"] = "10.0.8"
                })));

        Assert.Contains(DiagnosticsAdmission.EfCorrectnessOnlyMeasurementReasonCode, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Complete_measurement_plan_counts_trace_detail_as_one_logical_route()
    {
        var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[
            ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId] with
        {
            RequiredNativeRoutes = ["resources-by-last-seen", "trace-detail"]
        };
        var plan = new NativePlanEvidence(
            "diagnostics-plan",
            "diagnostics.native-plan.json",
            new string('a', 64),
            [new NativeRouteEvidence("resources-by-last-seen", "resource.raw.json", new string('b', 64), "index-search", "ix-resource", 128, true, false, 127, 127)])
        {
            TraceDetailConstituents =
            [
                new DiagnosticsTraceDetailConstituentEvidence(
                    "trace-detail/summary-by-trace-key", "", "", "primary-key-read", "", "SELECT", 100_000,
                    true, true, 1, 1, 1, 1, 1)
            ]
        };

        Assert.True(DiagnosticsAdmission.HasCompleteProviderNativePlan(workload, plan));
        Assert.False(DiagnosticsAdmission.HasCompleteProviderNativePlan(
            workload,
            plan with { TraceDetailConstituents = [] }));
        Assert.False(DiagnosticsAdmission.HasCompleteProviderNativePlan(
            workload,
            plan with { TraceDetailConstituents = null! }));
    }

    [Fact]
    public void Cross_process_evidence_comparison_includes_diagnostics_specific_fields()
    {
        var constituent = new DiagnosticsTraceDetailConstituentEvidence(
            "trace-detail/summary-by-trace-key", "", "", "primary-key-read", "", "SELECT", 100_000,
            true, true, 1, 1, 1, 1, 1);
        var nativePlan = new NativePlanEvidence("plan", "plan.json", new string('a', 64), [])
        {
            RouteContract = DiagnosticsNativePlanContract.BlockedRouteContract,
            BlockedRoutes = ["trace-detail"],
            TraceDetailConstituents = [constituent]
        };
        var first = new CorrectnessEvidence(
            new string('b', 64), "3.46.0", "file-backed", new Dictionary<string, string> { ["journal_mode"] = "wal" }, nativePlan);

        Assert.False(Comparison.SameEvidence(
            first,
            first with { NativePlan = nativePlan with { BlockedRoutes = ["structured-log-replay"] } }));
        Assert.False(Comparison.SameEvidence(
            first,
            first with
            {
                NativePlan = nativePlan with
                {
                    TraceDetailConstituents = [constituent with { CommandText = "SELECT changed" }]
                }
            }));
    }

    private static MatrixRequest Request(
        PerformanceWorkload workload,
        string adapter = "groundwork-v2",
        string physicalForm = "ordinary-groundwork-diagnostics-units",
        IReadOnlyDictionary<string, string>? packageVersions = null) => new(
        "diagnostics-cohort",
        "diagnostics-set",
        workload.Id,
        workload.Version,
        "sqlite",
        adapter,
        physicalForm,
        "100k",
        new string('a', 40),
        new string('b', 64),
        packageVersions ?? new Dictionary<string, string> { ["Groundwork.Sqlite"] = "0.4.0-preview.17" },
        new string('c', 64),
        new string('d', 64),
        "3.46.0",
        workload.RequiredProviderEvidence["sqlite"],
        new Dictionary<string, string> { ["journal_mode"] = "wal", ["synchronous"] = "normal" },
        workload.Input.Seed,
        workload.Input.FingerprintSha256,
        "diagnostics-plan",
        "diagnostics-set.native-plan.json",
        new string('e', 64));

    private static MatrixRequest MatrixRequest(RunRequest request) => new(
        request.ComparisonCohortId,
        request.MeasurementSetId,
        request.WorkloadId,
        request.WorkloadVersion,
        request.Provider,
        request.Adapter,
        request.PhysicalForm,
        request.Scale,
        request.CommitSha,
        request.HarnessAssemblySha256,
        request.PackageVersions,
        request.CompositionFingerprint,
        request.HostFingerprintSha256,
        request.ProviderVersion,
        request.ProviderTopology,
        request.ProviderConfiguration,
        request.Seed,
        request.InputFingerprintSha256,
        request.NativePlanIdentity,
        request.NativePlanEvidenceReference,
        request.NativePlanContentSha256);

    private sealed class DiagnosticsMatrixFixture : IDisposable
    {
        private readonly DiagnosticsArtifactFixture source;
        private readonly IReadOnlyDictionary<(ProcessKind ProcessKind, int ProcessIndex), ProcessArtifact> sourceArtifacts;
        private readonly RunRequest anchor;

        private DiagnosticsMatrixFixture()
        {
            source = DiagnosticsArtifactFixture.Create(fullNativePlan: true);
            source.Bind();
            sourceArtifacts = ArtifactStore.ReadAll(source.Directory).Artifacts
                .ToDictionary(artifact => (artifact.Request.ProcessKind, artifact.Request.ProcessIndex));
            Catalog = DiagnosticsArtifactFixture.CatalogWithDeclaredRoutes(DiagnosticsArtifactFixture.IndexedDiagnosticsRoutes);
            Workload = Catalog.Workloads[ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];
            anchor = sourceArtifacts[(ProcessKind.Warmup, 0)].Request;
            Plan = MatrixPlan.Create(Workload, MatrixRequest(anchor));
            OutputDirectory = Path.Combine(Path.GetTempPath(), $"elsa646-diagnostics-matrix-{Guid.NewGuid():N}");
        }

        public WorkloadCatalog Catalog { get; }
        public PerformanceWorkload Workload { get; }
        public MatrixPlan Plan { get; }
        public string OutputDirectory { get; }

        public static DiagnosticsMatrixFixture Create() => new();

        public MatrixPlan PlanFor(string measurementSetId, string nativePlanEvidenceReference) =>
            MatrixPlan.Create(Workload, MatrixRequest(anchor) with
            {
                MeasurementSetId = measurementSetId,
                NativePlanEvidenceReference = nativePlanEvidenceReference
            });

        public Task<int> WriteArtifactAsync(RunRequest run, string outputDirectory, CancellationToken _)
        {
            var sourceArtifact = sourceArtifacts[(run.ProcessKind, run.ProcessIndex)];
            Directory.CreateDirectory(outputDirectory);
            File.Copy(
                ArtifactStore.EvidencePath(source.Directory, run.NativePlanEvidenceReference),
                ArtifactStore.EvidencePath(outputDirectory, run.NativePlanEvidenceReference),
                overwrite: true);
            foreach (var rawPlan in ArtifactStore.ReferencedRawPlanNames(sourceArtifact.Correctness.NativePlan, sourceArtifact.Request))
                File.Copy(ArtifactStore.RawPlanPath(source.Directory, rawPlan), ArtifactStore.RawPlanPath(outputDirectory, rawPlan), overwrite: true);
            ArtifactStore.Write(outputDirectory, sourceArtifact with { Request = run });
            return Task.FromResult(0);
        }

        public void Dispose()
        {
            if (Directory.Exists(OutputDirectory))
                Directory.Delete(OutputDirectory, recursive: true);
            source.Dispose();
        }
    }
}
