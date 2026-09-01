using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Secrets.Core.Contracts;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class EfSecretRepositoryAdapterTests
{
    [Fact]
    public async Task Temporary_ef_comparator_executes_the_public_secret_repository_contract()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-secret-ef-public-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "secrets.db")}";

        try
        {
            var provider = await ProviderProbe.ReadAsync("sqlite", connectionString);
            await using var adapter = new EfSecretRepositoryAdapter(Request(provider), connectionString, root);
            await adapter.PrepareAsync(CancellationToken.None);
            var repository = Assert.IsAssignableFrom<ISecretRepository>(adapter.OpenPublicRepository());
            var secret = SecretCreateReadListWorkload.CreateSecret(
                "public-contract-secret",
                SecretCreateReadListWorkload.PrimaryTenantId,
                "public-contract",
                "value");

            Assert.True(await repository.TryAddAsync(secret));
            Assert.NotNull(await repository.FindAsync(secret.TenantId, secret.Name));
            var page = await repository.ListPageAsync(
                secret.TenantId,
                new SecretRepositoryListRequest(status: secret.Status, take: 1));
            Assert.Single(page.Items);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Captures_the_real_ef_filtered_list_native_route()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-secret-ef-plan-{Guid.NewGuid():N}");
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
            Assert.True(concurrency.ProviderCommandOverlapObserved);
            Assert.Equal(2, concurrency.DistinctPhysicalConnectionCount);
            Assert.Contains("SEARCH", File.ReadAllText(Path.Combine(root, route.RawPlanReference)), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Runs_the_frozen_secret_contract_over_real_sqlite_ef_storage()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-secret-ef-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "secrets.db")}";

        try
        {
            var provider = await ProviderProbe.ReadAsync("sqlite", connectionString);
            var request = Request(provider) with
            {
                NativePlanEvidenceReference = NativePlanEvidenceStaging.ReferenceFor(
                    SecretCreateReadListWorkload.WorkloadId,
                    "sqlite",
                    "set")
            };
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
                ProviderConcurrency = ExpectedEfConcurrency()
            };
            var evidenceDigest = NativePlanEvidenceStaging.Write(root, document);
            request = request with { NativePlanContentSha256 = evidenceDigest };

            await using var adapter = new EfSecretRepositoryAdapter(request, connectionString, root);
            await adapter.PrepareAsync(CancellationToken.None);
            Assert.Throws<PerformanceContractException>(() => adapter.Operations);

            var correctness = await adapter.VerifyCorrectnessAsync(CancellationToken.None);
            var concurrency = Assert.IsType<SecretProviderConcurrencyEvidence>(adapter.ConcurrencyEvidence);

            Assert.Equal(SecretCreateReadListWorkload.ExpectedResultDigest, correctness.ObservedResultDigestSha256);
            Assert.Equal(SecretCreateReadListWorkload.ConcurrentContenders, concurrency.IndependentClientCount);
            Assert.Equal(SecretCreateReadListWorkload.ConcurrentContenders, concurrency.CompletedContenders);
            Assert.Equal(SecretCreateReadListWorkload.ConcurrentContenders, concurrency.ProviderCommandStartCount);
            Assert.True(concurrency.ProviderCommandOverlapObserved);
            Assert.False(concurrency.ProviderCommandsSerializedByDesign);
            Assert.True(concurrency.EveryContenderIssuedProviderCommands);
            Assert.Equal(SecretCreateReadListWorkload.ConcurrentContenders, concurrency.DistinctPhysicalConnectionCount);
            Assert.Equal(evidenceDigest, correctness.NativePlan.ContentSha256);
            Assert.Equal("sqlite", adapter.RoundTripObserver.Provider);
            Assert.Equal("ef-core:DbCommandInterceptor", adapter.RoundTripObserver.Instrumentation);
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
    public async Task Isolates_matrix_process_fixtures_on_one_shared_sqlite_database()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-secret-ef-shared-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "secrets.db")}";

        try
        {
            var provider = await ProviderProbe.ReadAsync("sqlite", connectionString);
            var firstRequest = Request(provider) with { ProcessIndex = 1 };
            var secondRequest = Request(provider) with { ProcessIndex = 2 };
            await using var first = new EfSecretRepositoryAdapter(firstRequest, connectionString, root);
            await using var second = new EfSecretRepositoryAdapter(secondRequest, connectionString, root);
            Assert.NotEqual(first.PhysicalTenantId("tenant-alpha"), second.PhysicalTenantId("tenant-alpha"));
            await first.PrepareAsync(CancellationToken.None);
            await second.PrepareAsync(CancellationToken.None);

            var firstResult = await new SecretCreateReadListWorkload().ExecuteAsync(first);
            var secondResult = await new SecretCreateReadListWorkload().ExecuteAsync(second);

            Assert.Equal(SecretCreateReadListWorkload.ExpectedResultDigest, firstResult.ResultDigest);
            Assert.Equal(SecretCreateReadListWorkload.ExpectedResultDigest, secondResult.ResultDigest);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Enforces_tenant_local_normalized_name_uniqueness()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-secret-ef-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "secrets.db")}";

        try
        {
            var provider = await ProviderProbe.ReadAsync("sqlite", connectionString);
            var request = Request(provider);
            await using var adapter = new EfSecretRepositoryAdapter(request, connectionString, root);
            await adapter.PrepareAsync(CancellationToken.None);
            var scopes = await adapter.OpenIsolatedScopesAsync();
            var alpha = SecretCreateReadListWorkload.CreateSecret("alpha", "tenant-a", "same-name", "a");
            var beta = SecretCreateReadListWorkload.CreateSecret("beta", "tenant-b", "same-name", "b");

            Assert.True(await scopes.Primary.TryAddAsync(alpha));
            Assert.False(await scopes.Secondary.TryAddAsync(SecretCreateReadListWorkload.CreateSecret("alpha-duplicate", "tenant-a", "same-name", "different")));
            Assert.True(await scopes.Secondary.TryAddAsync(beta));
            Assert.Equal("a", (await scopes.Primary.FindAsync("tenant-a", "same-name"))!.LatestActiveVersion!.Payload.Value);
            Assert.Equal("b", (await scopes.Primary.FindAsync("tenant-b", "same-name"))!.LatestActiveVersion!.Payload.Value);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_an_unnormalized_public_identity_before_writing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"groundwork-secret-ef-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var connectionString = $"Data Source={Path.Combine(root, "secrets.db")}";

        try
        {
            var provider = await ProviderProbe.ReadAsync("sqlite", connectionString);
            await using var adapter = new EfSecretRepositoryAdapter(Request(provider), connectionString, root);
            await adapter.PrepareAsync(CancellationToken.None);
            var scopes = await adapter.OpenIsolatedScopesAsync();

            await Assert.ThrowsAsync<ArgumentException>(() =>
                scopes.Primary.TryAddAsync(
                    SecretCreateReadListWorkload.CreateSecret("invalid", "tenant-a", "Same-Name", "value")).AsTask());
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
        "ef-secret-repository",
        EfSecretRepositoryAdapter.PhysicalForm,
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
        "secret-ef-test",
        NativePlanEvidenceStaging.ReferenceFor(SecretCreateReadListWorkload.WorkloadId, "sqlite", "set"),
        new string('e', 64),
        ProcessKind.Measured,
        1);

    private static SecretProviderConcurrencyEvidence ExpectedEfConcurrency() => new(
        SecretCreateReadListWorkload.ConcurrentContenders,
        SecretCreateReadListWorkload.ConcurrentContenders,
        SecretCreateReadListWorkload.ConcurrentContenders,
        ProviderCommandOverlapObserved: true,
        ProviderCommandsSerializedByDesign: false,
        EveryContenderIssuedProviderCommands: true,
        DistinctPhysicalConnectionCount: SecretCreateReadListWorkload.ConcurrentContenders);
}
