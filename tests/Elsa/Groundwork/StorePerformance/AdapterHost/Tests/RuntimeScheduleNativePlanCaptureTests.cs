using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class RuntimeScheduleNativePlanCaptureTests
{
    [Fact]
    public async Task Due_timer_capture_fails_closed_when_the_native_winning_plan_misses_the_declared_index()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-due-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "due-timer.db")}";

        try
        {
            var observed = await ProviderProbe.ReadAsync("sqlite", connectionString);
            var request = Request(
                RuntimeDueTimerSelectionWorkload.WorkloadId,
                DueTimerSelectionAdapter.PhysicalForm,
                RuntimeDueTimerSelectionWorkload.Seed,
                RuntimeDueTimerSelectionWorkload.ExpectedInputFingerprint,
                "due-timer-capture") with
            {
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };

            var exception = await Record.ExceptionAsync(() =>
                DueTimerNativePlanCapture.CaptureAsync(
                    request,
                    connectionString,
                    root,
                    observed));

            Assert.NotNull(exception);
            Assert.Equal("Groundwork.Diagnostics.ExplainAssertionException", exception.GetType().FullName);
            Assert.Contains("winning plan did not use the required index", exception.Message, StringComparison.Ordinal);
            Assert.Contains("logical-index=by_due_time_and_timer_id", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, NativePlanEvidenceStaging.ReferenceFor(
                request.WorkloadId,
                request.Provider,
                request.MeasurementSetId))));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Recurring_capture_fails_closed_when_the_native_plan_sorts_outside_the_index()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-recurring-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "recurring.db")}";

        try
        {
            var observed = await ProviderProbe.ReadAsync("sqlite", connectionString);
            var request = Request(
                RuntimeRecurringScheduleSelectionWorkload.WorkloadId,
                RecurringScheduleSelectionAdapter.PhysicalForm,
                RuntimeRecurringScheduleSelectionWorkload.Seed,
                RuntimeRecurringScheduleSelectionWorkload.ExpectedInputFingerprint,
                "recurring-capture") with
            {
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };

            var exception = await Assert.ThrowsAsync<PerformanceContractException>(() =>
                RecurringScheduleNativePlanCapture.CaptureAsync(
                    request,
                    connectionString,
                    root,
                    observed));

            Assert.Contains("SQLite sort or materialization spill", exception.Message, StringComparison.Ordinal);
            Assert.Contains("USE TEMP B-TREE FOR ORDER BY", exception.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(root, request.NativePlanEvidenceReference)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static RunRequest Request(
        string workloadId,
        string physicalForm,
        string seed,
        string inputFingerprint,
        string identity) =>
        new RunRequest(
            ComparisonCohortId: "cohort",
            MeasurementSetId: "set",
            WorkloadId: workloadId,
            WorkloadVersion: "1.1.0",
            Provider: "sqlite",
            ProviderVersion: "3.0.0",
            ProviderTopology: "file-backed-distinct-connections",
            ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal),
            Adapter: BenchmarkAdapterRegistry.GroundworkV2Adapter,
            PhysicalForm: physicalForm,
            Scale: "small",
            CommitSha: new string('a', 40),
            HarnessAssemblySha256: new string('b', 64),
            PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Groundwork.Store"] = "0.4.0-preview.1"
            },
            CompositionFingerprint: new string('c', 64),
            HostFingerprintSha256: new string('d', 64),
            Seed: seed,
            InputFingerprintSha256: inputFingerprint,
            NativePlanIdentity: identity,
            NativePlanEvidenceReference: "placeholder.native-plan.json",
            NativePlanContentSha256: new string('e', 64),
            ProcessKind: ProcessKind.Measured,
            ProcessIndex: 1) with
        {
            NativePlanEvidenceReference = NativePlanEvidenceStaging.ReferenceFor(workloadId, "sqlite", "set")
        };
}
