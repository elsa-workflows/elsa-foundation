using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class BookmarkLookupNativePlanCaptureTests
{
    [Fact]
    public async Task Captures_both_bookmark_stimulus_routes_with_the_declared_native_indexes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-bookmark-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "bookmark.db")}";

        try
        {
            var observed = await ProviderProbe.ReadAsync("sqlite", connectionString);
            var request = Request() with
            {
                ProviderVersion = observed.Version,
                ProviderTopology = observed.Topology,
                ProviderConfiguration = observed.Configuration
            };

            var digest = await BookmarkLookupNativePlanCapture.CaptureAsync(
                request,
                connectionString,
                root,
                observed);

            var evidencePath = Path.Combine(root, request.NativePlanEvidenceReference);
            var document = NativePlanEvidenceStaging.Read(evidencePath);
            Assert.Equal(digest, NativePlanEvidenceStaging.Sha256(evidencePath));
            Assert.Equal(RuntimeNativePlanContract.RouteContract, document.RouteContract);
            Assert.Equal(
                ["list-by-stimulus-and-type", "list-by-stimulus-type"],
                document.Routes.Select(route => route.RouteIdentity));

            AssertRoute(root, request, document.Routes[0], "list-by-stimulus-and-type", "by_stimulus_and_type_and_bookmark_identity");
            AssertRoute(root, request, document.Routes[1], "list-by-stimulus-type", "by_stimulus_type_and_bookmark_identity");
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
        string routeIdentity,
        string logicalIndex)
    {
        var specification = RuntimeNativePlanContract.For(request.WorkloadId, routeIdentity);
        Assert.Equal(routeIdentity, route.RouteIdentity);
        Assert.Equal(logicalIndex, specification.IndexName);
        Assert.Equal(RuntimeNativePlanContract.ExpectedPhysicalIndexName(request.Provider, specification), route.IndexName);
        Assert.Equal("index-search", route.PlanClassification);
        Assert.True(route.HasStorageScopePredicate);
        Assert.True(route.HasRoutePredicate);
        Assert.Equal(specification.PhysicalCardinality, route.PhysicalCardinality);
        Assert.Equal(specification.FiniteLimit, route.FiniteLimit);
        Assert.Equal(specification.FiniteLimit, route.MaterializedCandidateCount);
        RuntimeNativePlanContract.ValidateEnvelope(
            request.WorkloadId,
            request.Provider,
            request.Adapter,
            route,
            Path.Combine(root, route.RawPlanReference));
    }

    private static RunRequest Request() => new(
        ComparisonCohortId: "cohort",
        MeasurementSetId: "bookmark-capture",
        WorkloadId: RuntimeBookmarkLookupWorkload.WorkloadId,
        WorkloadVersion: RuntimeBookmarkLookupWorkload.Version,
        Provider: "sqlite",
        ProviderVersion: "3.0.0",
        ProviderTopology: "file-backed-distinct-connections",
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal),
        Adapter: BenchmarkAdapterRegistry.GroundworkV2Adapter,
        PhysicalForm: BookmarkLookupAdapter.PhysicalForm,
        Scale: "small",
        CommitSha: new string('a', 40),
        HarnessAssemblySha256: new string('b', 64),
        PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Groundwork.Store"] = "0.4.0-preview.1"
        },
        CompositionFingerprint: new string('c', 64),
        HostFingerprintSha256: new string('d', 64),
        Seed: RuntimeBookmarkLookupWorkload.Seed,
        InputFingerprintSha256: RuntimeBookmarkLookupWorkload.ExpectedInputFingerprint,
        NativePlanIdentity: "bookmark-capture",
        NativePlanEvidenceReference: NativePlanEvidenceStaging.ReferenceFor(
            RuntimeBookmarkLookupWorkload.WorkloadId,
            "sqlite",
            "bookmark-capture"),
        NativePlanContentSha256: new string('e', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: 1);
}
