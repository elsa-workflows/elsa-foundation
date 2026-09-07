using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class RuntimeScheduleNativePlanCaptureTests
{
    [Fact]
    public async Task Captures_due_timer_route_with_the_declared_native_index()
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

            var digest = await DueTimerNativePlanCapture.CaptureAsync(
                request,
                connectionString,
                root,
                observed);

            var document = AssertCapture(root, request, digest, 1);
            var route = Assert.Single(document.Routes);
            AssertRoute(root, request, route, "list-due", "by_due_time_and_timer_id");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Captures_recurring_routes_with_the_declared_native_indexes()
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

            var digest = await RecurringScheduleNativePlanCapture.CaptureAsync(
                request,
                connectionString,
                root,
                observed);

            var document = AssertCapture(root, request, digest, 2);
            AssertRoute(root, request, document.Routes.Single(route => route.RouteIdentity == "list-due"), "list-due", "by_active_next_occurrence_and_schedule_id");
            AssertRoute(root, request, document.Routes.Single(route => route.RouteIdentity == "page-by-publication"), "page-by-publication", "by_activation_and_schedule_id");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static NativePlanEvidenceDocument AssertCapture(
        string root,
        RunRequest request,
        string digest,
        int expectedRoutes)
    {
        var evidencePath = Path.Combine(root, request.NativePlanEvidenceReference);
        var document = NativePlanEvidenceStaging.Read(evidencePath);
        Assert.Equal(digest, NativePlanEvidenceStaging.Sha256(evidencePath));
        Assert.Equal(expectedRoutes, document.Routes.Count);
        Assert.Equal(RuntimeNativePlanContract.RouteContract, document.RouteContract);
        return document;
    }

    private static void AssertRoute(
        string root,
        RunRequest request,
        NativeRouteEvidence route,
        string routeIdentity,
        string logicalIndex)
    {
        var specification = RuntimeNativePlanContract.For(request.WorkloadId, routeIdentity);
        Assert.Equal(routeIdentity, route.RouteIdentity);
        Assert.Equal(RuntimeNativePlanContract.ExpectedPhysicalIndexName(request.Provider, specification), route.IndexName);
        Assert.Equal(logicalIndex, specification.IndexName);
        Assert.Equal("index-search", route.PlanClassification);
        Assert.True(route.HasStorageScopePredicate);
        Assert.True(route.HasRoutePredicate);
        Assert.Equal(specification.PhysicalCardinality, route.PhysicalCardinality);
        Assert.Equal(specification.FiniteLimit, route.FiniteLimit);
        Assert.Equal(specification.NativeFetchLimit, route.NativeFetchLimit);
        Assert.Equal(specification.FiniteLimit, route.MaterializedCandidateCount);
        RuntimeNativePlanContract.ValidateEnvelope(
            request.WorkloadId,
            request.Provider,
            request.Adapter,
            route,
            Path.Combine(root, route.RawPlanReference));
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
