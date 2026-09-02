using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class RuntimeQueueAndCommandNativePlanCaptureTests
{
    [Fact]
    public async Task Captures_queue_routes_with_real_sqlite_plans()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-queue-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var observed = await ProviderProbe.ReadAsync("sqlite", $"Data Source={Path.Combine(root, "queue.db")}");
            var request = Request(
                RuntimeQueueDrainWorkload.WorkloadId,
                QueueDrainAdapter.PhysicalForm,
                RuntimeQueueDrainWorkload.Seed,
                RuntimeQueueDrainWorkload.ExpectedInputFingerprint,
                "queue-capture") with
            {
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };
            var digest = await QueueDrainNativePlanCapture.CaptureAsync(
                request,
                $"Data Source={Path.Combine(root, "queue.db")}",
                root,
                observed);
            var document = NativePlanEvidenceStaging.Read(Path.Combine(root, request.NativePlanEvidenceReference));

            Assert.Equal(digest, NativePlanEvidenceStaging.Sha256(Path.Combine(root, request.NativePlanEvidenceReference)));
            Assert.Equal(
                ["list-pending-scheduler-workflow-executions", "list-by-workflow-execution"],
                document.Routes.Select(route => route.RouteIdentity));
            AssertRoute(root, request, document.Routes[0], RuntimeNativeResultShape.Page, 16, 16, true, false);
            AssertRoute(root, request, document.Routes[1], RuntimeNativeResultShape.Page, 32, 32, false, true);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Captures_command_routes_with_real_sqlite_plans_including_scalar_count()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-command-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var connectionString = $"Data Source={Path.Combine(root, "command.db")}";
            var observed = await ProviderProbe.ReadAsync("sqlite", connectionString);
            var request = Request(
                DistributedCommandSendLeaseAckWorkload.WorkloadId,
                DistributedCommandSendLeaseAckAdapter.PhysicalForm,
                "spec094-command-send-lease-ack-v1.1",
                DistributedCommandSendLeaseAckWorkload.ExpectedInputFingerprint,
                "command-capture") with
            {
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };
            var digest = await DistributedCommandNativePlanCapture.CaptureAsync(
                request,
                connectionString,
                root,
                observed);
            var document = NativePlanEvidenceStaging.Read(Path.Combine(root, request.NativePlanEvidenceReference));

            Assert.Equal(digest, NativePlanEvidenceStaging.Sha256(Path.Combine(root, request.NativePlanEvidenceReference)));
            Assert.Equal(3, document.Routes.Count);
            AssertRoute(root, request, document.Routes.Single(route => route.RouteIdentity == "list-visible-command-executions"), RuntimeNativeResultShape.Page, 128, 128, true, true);
            AssertRoute(root, request, document.Routes.Single(route => route.RouteIdentity == "lease-visible-commands-by-execution"), RuntimeNativeResultShape.Page, 8, 8, false, true);
            AssertRoute(root, request, document.Routes.Single(route => route.RouteIdentity == "count-pending-commands-by-execution"), RuntimeNativeResultShape.ScalarCount, 0, 64, false, true);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertRoute(
        string root,
        RunRequest request,
        NativeRouteEvidence route,
        RuntimeNativeResultShape resultShape,
        int finiteLimit,
        int resultCount,
        bool latestPerKey,
        bool routePredicate)
    {
        var specification = RuntimeNativePlanContract.For(request.WorkloadId, route.RouteIdentity);
        Assert.Equal(resultShape, route.ResultShape);
        Assert.Equal(finiteLimit, route.FiniteLimit);
        Assert.Equal(resultShape == RuntimeNativeResultShape.Page ? resultCount : 0, route.MaterializedCandidateCount);
        Assert.Equal(resultShape == RuntimeNativeResultShape.ScalarCount ? resultCount : null, route.ScalarResultCount);
        Assert.Equal(specification.NativeFetchLimit, route.NativeFetchLimit);
        Assert.Equal(latestPerKey, route.UsesLatestPerKey);
        Assert.Equal(routePredicate, route.HasRoutePredicate);
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
        new(
            ComparisonCohortId: "cohort",
            MeasurementSetId: "capture",
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
            NativePlanEvidenceReference: NativePlanEvidenceStaging.ReferenceFor(workloadId, "sqlite", "capture"),
            NativePlanContentSha256: new string('e', 64),
            ProcessKind: ProcessKind.Measured,
            ProcessIndex: 1);
}
