using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class IamNativeEvidenceAdmissionTests
{
    [Fact]
    public void Exact_iam_native_route_evidence_is_admitted()
    {
        using var fixture = EvidenceFixture.Create();

        ArtifactAdmission.ValidateCorrectness(
            fixture.Workload,
            fixture.Request,
            fixture.Evidence,
            fixture.Directory);
    }

    [Theory]
    [InlineData("find-user-by-normalized-name", 2)]
    [InlineData("find-user-by-normalized-email", 1)]
    [InlineData("find-role-by-normalized-name", 2)]
    [InlineData("list-user-roles", 1)]
    [InlineData("list-role-users", 1)]
    public void Iam_native_routes_require_their_exact_finite_limit(string routeIdentity, int finiteLimit)
    {
        using var fixture = EvidenceFixture.Create(routes => routes.Select(route =>
            route.RouteIdentity == routeIdentity
                ? route with { FiniteLimit = finiteLimit }
                : route).ToArray());

        var error = Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateCorrectness(
                fixture.Workload,
                fixture.Request,
                fixture.Evidence,
                fixture.Directory));

        Assert.Contains("IAM", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Iam_native_routes_require_exact_physical_cardinality()
    {
        using var fixture = EvidenceFixture.Create(routes => routes
            .Select(route => route with { PhysicalCardinality = 99_999 })
            .ToArray());

        var error = Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateCorrectness(
                fixture.Workload,
                fixture.Request,
                fixture.Evidence,
                fixture.Directory));

        Assert.Contains("IAM", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Iam_native_routes_require_one_materialized_candidate()
    {
        using var fixture = EvidenceFixture.Create(routes => routes
            .Select(route => route with { MaterializedCandidateCount = 2 })
            .ToArray());

        var error = Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateCorrectness(
                fixture.Workload,
                fixture.Request,
                fixture.Evidence,
                fixture.Directory));

        Assert.Contains("IAM", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Iam_native_evidence_requires_the_frozen_route_contract()
    {
        using var fixture = EvidenceFixture.Create(routeContract: "test-provenance");

        var error = Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateCorrectness(
                fixture.Workload,
                fixture.Request,
                fixture.Evidence,
                fixture.Directory));

        Assert.Contains("route evidence", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Iam_native_evidence_requires_the_frozen_route_names()
    {
        using var fixture = EvidenceFixture.Create(routes => routes
            .Select(route => route.RouteIdentity == "list-role-users"
                ? route with { RouteIdentity = "list-role-users-drift" }
                : route)
            .ToArray());

        var error = Assert.Throws<PerformanceContractException>(() =>
            ArtifactAdmission.ValidateCorrectness(
                fixture.Workload,
                fixture.Request,
                fixture.Evidence,
                fixture.Directory));

        Assert.Contains("IAM", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class EvidenceFixture : IDisposable
    {
        private EvidenceFixture(
            PerformanceWorkload workload,
            RunRequest request,
            CorrectnessEvidence evidence,
            string directory)
        {
            Workload = workload;
            Request = request;
            Evidence = evidence;
            Directory = directory;
        }

        public PerformanceWorkload Workload { get; }
        public RunRequest Request { get; }
        public CorrectnessEvidence Evidence { get; }
        public string Directory { get; }

        public static EvidenceFixture Create(
            Func<IReadOnlyList<NativeRouteEvidence>, IReadOnlyList<NativeRouteEvidence>>? transform = null,
            string routeContract = "provider-native-routes")
        {
            var workload = WorkloadCatalog.Load(Repository.Root()).Workloads[IamNormalizedLookupWorkload.WorkloadId];
            var directory = Path.Combine(Path.GetTempPath(), $"elsa646-iam-native-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);

            var request = new RunRequest(
                "iam-native-cohort",
                "iam-native-set",
                workload.Id,
                workload.Version,
                "sqlite",
                "groundwork-aspnetcore-identity",
                "entity-type-specific-physical-tables-current-identity-shape",
                "100k",
                new string('a', 40),
                new string('b', 64),
                new Dictionary<string, string> { ["Groundwork.Identity"] = "0.0.1-preview.60" },
                new string('c', 64),
                new string('d', 64),
                "3.46.0",
                "file-backed-distinct-connections",
                new Dictionary<string, string> { ["journal_mode"] = "wal" },
                workload.Input.Seed,
                workload.Input.FingerprintSha256,
                "iam-native-plan",
                "iam-native-plan.json",
                new string('e', 64),
                ProcessKind.Measured,
                1);

            var routes = IamNormalizedLookupWorkload.NativeRouteLimits
                .Select(pair => CreateRoute(pair.Key, pair.Value))
                .ToArray();
            routes = (transform?.Invoke(routes) ?? routes).ToArray();
            foreach (var route in routes)
            {
                var rawPlan = RawPlan(route.RouteIdentity);
                File.WriteAllText(Path.Combine(directory, route.RawPlanReference), rawPlan);
            }

            var document = new NativePlanEvidenceDocument(
                2,
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
                request.CompositionFingerprint,
                request.HostFingerprintSha256,
                request.ProviderVersion,
                request.ProviderTopology,
                request.ProviderConfiguration,
                request.Seed,
                request.InputFingerprintSha256,
                request.NativePlanIdentity,
                routes,
                routeContract);
            var evidencePath = Path.Combine(directory, request.NativePlanEvidenceReference);
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(document, ArtifactStore.JsonOptions));
            request = request with { NativePlanContentSha256 = ArtifactStore.HashFile(evidencePath) };

            return new EvidenceFixture(
                workload,
                request,
                new CorrectnessEvidence(
                    workload.Correctness.ResultDigestSha256,
                    request.ProviderVersion,
                    request.ProviderTopology,
                    request.ProviderConfiguration,
                    new NativePlanEvidence(
                        request.NativePlanIdentity,
                        request.NativePlanEvidenceReference,
                        request.NativePlanContentSha256,
                        routes)),
                directory);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }

        private static NativeRouteEvidence CreateRoute(string routeIdentity, int finiteLimit)
        {
            var rawPlanReference = $"{routeIdentity}.raw.json";
            return new NativeRouteEvidence(
                routeIdentity,
                rawPlanReference,
                Hash(RawPlan(routeIdentity)),
                "index-seek",
                $"ix-{routeIdentity}",
                100_000,
                true,
                true,
                finiteLimit,
                1);
        }

        private static string RawPlan(string routeIdentity) =>
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Route = routeIdentity,
                ProviderPlan = $"EXPLAIN {routeIdentity}"
            });

        private static string Hash(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
