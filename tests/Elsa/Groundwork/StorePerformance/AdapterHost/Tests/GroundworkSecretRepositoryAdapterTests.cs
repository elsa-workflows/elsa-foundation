using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class GroundworkSecretRepositoryAdapterTests
{
    [Fact]
    public async Task Captures_the_real_groundwork_filtered_list_native_route()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-secret-plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "secrets.db")}";

        try
        {
            var provider = await ProviderProbe.ReadAsync("sqlite", connectionString);
            var request = Request(provider);
            var digest = await SecretNativePlanCapture.CaptureAsync(
                request,
                connectionString,
                root,
                provider,
                CancellationToken.None);
            var document = NativePlanEvidenceStaging.Read(Path.Combine(
                root,
                request.NativePlanEvidenceReference));
            var route = Assert.Single(document.Routes);
            var concurrency = Assert.IsType<SecretProviderConcurrencyEvidence>(document.ProviderConcurrency);

            Assert.Equal(digest, NativePlanEvidenceStaging.Sha256(Path.Combine(root, request.NativePlanEvidenceReference)));
            Assert.Equal("list-filtered", route.RouteIdentity);
            Assert.Equal("index-search", route.PlanClassification);
            Assert.True(route.HasStorageScopePredicate);
            Assert.True(route.HasRoutePredicate);
            Assert.Equal(SecretCreateReadListWorkload.CanonicalSecretCount + SecretCreateReadListWorkload.NoiseSecretCount + 1, route.PhysicalCardinality);
            Assert.Equal(SecretCreateReadListWorkload.PageSize, route.FiniteLimit);
            Assert.Equal(SecretCreateReadListWorkload.PageSize, route.MaterializedCandidateCount);
            Assert.True(concurrency.ProviderCommandsSerializedByDesign);
            Assert.Equal(2, concurrency.ProviderCommandStartCount);
            Assert.Contains("SEARCH", File.ReadAllText(Path.Combine(root, route.RawPlanReference)), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Runs_the_secret_contract_through_the_public_groundwork_repository()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-secret-gw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "secrets.db")}";

        try
        {
            var provider = await ProviderProbe.ReadAsync("sqlite", connectionString);
            var request = Request(provider);
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
                [],
                "test-provenance")
            {
                ProviderConcurrency = ExpectedGroundworkSqliteConcurrency()
            };
            var digest = NativePlanEvidenceStaging.Write(root, document);
            request = request with { NativePlanContentSha256 = digest };

            await using var adapter = new GroundworkSecretRepositoryAdapter(request, connectionString, root);
            await adapter.PrepareAsync(CancellationToken.None);
            var correctness = await adapter.VerifyCorrectnessAsync(CancellationToken.None);
            var concurrency = Assert.IsType<SecretProviderConcurrencyEvidence>(adapter.ConcurrencyEvidence);

            Assert.Equal(SecretCreateReadListWorkload.ExpectedResultDigest, correctness.ObservedResultDigestSha256);
            Assert.Equal(SecretCreateReadListWorkload.ConcurrentContenders, concurrency.IndependentClientCount);
            Assert.Equal(SecretCreateReadListWorkload.ConcurrentContenders, concurrency.CompletedContenders);
            Assert.Equal(SecretCreateReadListWorkload.ConcurrentContenders, concurrency.ProviderCommandStartCount);
            Assert.False(concurrency.ProviderCommandOverlapObserved);
            Assert.True(concurrency.ProviderCommandsSerializedByDesign);
            Assert.True(concurrency.EveryContenderIssuedProviderCommands);
            Assert.False(string.IsNullOrWhiteSpace(correctness.ObservedProviderVersion));
            Assert.Equal("file-backed-distinct-connections", correctness.ObservedProviderTopology);
            Assert.Equal("groundwork-v2:IProviderCommandObserver", adapter.RoundTripObserver!.Instrumentation);
            Assert.True(adapter.RoundTripObserver.IsExact);
            Assert.True(adapter.RoundTripObserver.Snapshot() > 0);
            Assert.Equal(
                [
                    "read-create-winner-by-identity",
                    "list-secrets-bounded-first-page",
                    "list-secrets-bounded-next-offset-page"
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

    [Fact]
    public async Task Enforces_tenant_local_normalized_name_uniqueness_through_the_public_repository()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-secret-gw-unique-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "secrets.db")}";

        try
        {
            var provider = await ProviderProbe.ReadAsync("sqlite", connectionString);
            await using var adapter = new GroundworkSecretRepositoryAdapter(Request(provider), connectionString, root);
            await adapter.PrepareAsync(CancellationToken.None);
            var scopes = await adapter.OpenIsolatedScopesAsync();

            Assert.True(await scopes.Primary.TryAddAsync(
                SecretCreateReadListWorkload.CreateSecret("alpha", "tenant-a", "same-name", "a")));
            Assert.False(await scopes.Secondary.TryAddAsync(
                SecretCreateReadListWorkload.CreateSecret("alpha-duplicate", "tenant-a", "same-name", "different")));
            Assert.True(await scopes.Secondary.TryAddAsync(
                SecretCreateReadListWorkload.CreateSecret("beta", "tenant-b", "same-name", "b")));
            Assert.Equal("a", (await scopes.Primary.FindAsync("tenant-a", "same-name"))!.LatestActiveVersion!.Payload.Value);
            Assert.Equal("b", (await scopes.Primary.FindAsync("tenant-b", "same-name"))!.LatestActiveVersion!.Payload.Value);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static RunRequest Request(ProviderProbe.Result provider) => new(
        "cohort",
        "set",
        SecretCreateReadListWorkload.WorkloadId,
        SecretCreateReadListWorkload.Version,
        "sqlite",
        "groundwork-secret-repository",
        GroundworkSecretRepositoryAdapter.PhysicalForm,
        "small",
        new string('a', 40),
        new string('b', 64),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new string('c', 64),
        new string('d', 64),
        provider.Version,
        provider.Topology,
        provider.Configuration,
        SecretCreateReadListWorkload.Seed,
        SecretCreateReadListWorkload.ExpectedInputFingerprint,
        "secret-groundwork-test",
        NativePlanEvidenceStaging.ReferenceFor(SecretCreateReadListWorkload.WorkloadId, "sqlite", "set"),
        new string('e', 64),
        ProcessKind.Measured,
        1);

    private static SecretProviderConcurrencyEvidence ExpectedGroundworkSqliteConcurrency() => new(
        SecretCreateReadListWorkload.ConcurrentContenders,
        SecretCreateReadListWorkload.ConcurrentContenders,
        SecretCreateReadListWorkload.ConcurrentContenders,
        ProviderCommandOverlapObserved: false,
        ProviderCommandsSerializedByDesign: true,
        EveryContenderIssuedProviderCommands: true,
        DistinctPhysicalConnectionCount: 1);
}
