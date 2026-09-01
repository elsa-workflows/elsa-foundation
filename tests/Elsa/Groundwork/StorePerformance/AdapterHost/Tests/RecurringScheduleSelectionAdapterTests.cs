using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class RecurringScheduleSelectionAdapterTests
{
    [Fact]
    public async Task Runs_frozen_correctness_and_prepares_operations_over_real_sqlite_runtime_stores()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-recurring-adapter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "recurring.db");
        var connectionString = $"Data Source={database}";

        try
        {
            var observed = await ProviderProbe.ReadAsync("sqlite", connectionString);
            var request = Request() with
            {
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };
            var reference = NativePlanEvidenceStaging.ReferenceFor(
                request.WorkloadId,
                request.Provider,
                request.MeasurementSetId);
            request = request with { NativePlanEvidenceReference = reference };
            var evidence = new NativePlanEvidenceDocument(
                SchemaVersion: 2,
                ComparisonCohortId: request.ComparisonCohortId,
                MeasurementSetId: request.MeasurementSetId,
                WorkloadId: request.WorkloadId,
                WorkloadVersion: request.WorkloadVersion,
                Provider: request.Provider,
                Adapter: request.Adapter,
                PhysicalForm: request.PhysicalForm,
                Scale: request.Scale,
                CommitSha: request.CommitSha,
                HarnessAssemblySha256: request.HarnessAssemblySha256,
                CompositionFingerprint: request.CompositionFingerprint,
                HostFingerprintSha256: request.HostFingerprintSha256,
                ProviderVersion: request.ProviderVersion,
                ProviderTopology: request.ProviderTopology,
                ProviderConfiguration: request.ProviderConfiguration,
                Seed: request.Seed,
                InputFingerprintSha256: request.InputFingerprintSha256,
                Identity: request.NativePlanIdentity,
                Routes: [],
                RouteContract: "test-provenance");
            var evidenceDigest = NativePlanEvidenceStaging.Write(root, evidence);
            request = request with { NativePlanContentSha256 = evidenceDigest };

            await using var adapter = new RecurringScheduleSelectionAdapter(request, connectionString, root);
            await adapter.PrepareAsync(CancellationToken.None);
            Assert.Throws<PerformanceContractException>(() => adapter.Operations);

            var correctness = await adapter.VerifyCorrectnessAsync(CancellationToken.None);

            Assert.Equal(RuntimeRecurringScheduleSelectionWorkload.ExpectedResultDigest, correctness.ObservedResultDigestSha256);
            Assert.Equal(observed.Version, correctness.ObservedProviderVersion);
            Assert.Equal(observed.Topology, correctness.ObservedProviderTopology);
            Assert.Equal(observed.Configuration, correctness.ObservedProviderConfiguration);
            Assert.Equal(evidenceDigest, correctness.NativePlan.ContentSha256);
            Assert.Equal("groundwork-v2:IProviderCommandObserver", adapter.RoundTripObserver!.Instrumentation);
            Assert.True(adapter.RoundTripObserver.IsExact);
            Assert.True(adapter.RoundTripObserver.Snapshot() > 0);
            Assert.Equal(
                [
                    "list-bounded-due-schedules",
                    "load-publication-projections",
                    "advance-current-schedule",
                    "attempt-stale-advance",
                    "reopen-and-read-projection-state"
                ],
                adapter.Operations.Select(operation => operation.Id));
            foreach (var operation in adapter.Operations)
            {
                await operation.PrepareInvocationAsync(-1, CancellationToken.None);
                var before = adapter.RoundTripObserver.Snapshot();
                await operation.InvokeAsync(-1, CancellationToken.None);
                Assert.True(adapter.RoundTripObserver.Snapshot() > before, operation.Id);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static RunRequest Request() => new(
        ComparisonCohortId: "cohort",
        MeasurementSetId: "set",
        WorkloadId: RuntimeRecurringScheduleSelectionWorkload.WorkloadId,
        WorkloadVersion: "1.1.0",
        Provider: "sqlite",
        ProviderVersion: "3.0.0",
        ProviderTopology: "file-backed-distinct-connections",
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal),
        Adapter: BenchmarkAdapterRegistry.GroundworkV2Adapter,
        PhysicalForm: RecurringScheduleSelectionAdapter.PhysicalForm,
        Scale: "small",
        CommitSha: new string('a', 40),
        HarnessAssemblySha256: new string('b', 64),
        PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Groundwork.Store"] = "0.4.0-preview.1"
        },
        CompositionFingerprint: new string('c', 64),
        HostFingerprintSha256: new string('d', 64),
        Seed: RuntimeRecurringScheduleSelectionWorkload.Seed,
        InputFingerprintSha256: RuntimeRecurringScheduleSelectionWorkload.ExpectedInputFingerprint,
        NativePlanIdentity: "recurring-schedule-test-provenance",
        NativePlanEvidenceReference: "recurring-schedule.sqlite.set.native-plan.json",
        NativePlanContentSha256: new string('e', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: 1);
}
