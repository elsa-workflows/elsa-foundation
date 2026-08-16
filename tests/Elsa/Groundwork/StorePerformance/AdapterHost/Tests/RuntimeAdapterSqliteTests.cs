using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

/// <summary>Vertical public-store proofs for the E3 leaves against a real file-backed SQLite provider.</summary>
public sealed class RuntimeAdapterSqliteTests
{
    [Theory]
    [InlineData(RuntimeBookmarkLookupWorkload.WorkloadId)]
    [InlineData(RuntimeQueueDrainWorkload.WorkloadId)]
    [InlineData(RuntimeOutboxDrainWorkload.WorkloadId)]
    [Trait("Category", "Sqlite")]
    public async Task E3_leaf_prepares_and_executes_each_timed_public_store_operation(string workloadId)
    {
        var catalog = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot());
        var workload = catalog.Workloads[workloadId];
        var request = await RequestAsync(workload);
        var nativePlan = Document(workload, request);

        await using var adapter = await BenchmarkAdapterFactory.CreateAsync(
            new AdapterContext(request, workload, nativePlan),
            CancellationToken.None);
        await adapter.PrepareAsync(CancellationToken.None);

        Assert.Equal(workload.OperationSequence, adapter.Operations.Select(operation => operation.Id));
        foreach (var operation in adapter.Operations)
        {
            await operation.PrepareInvocationAsync(0, CancellationToken.None);
            await operation.InvokeAsync(0, CancellationToken.None);
        }
    }

    [SkippableTheory]
    [InlineData(RuntimeBookmarkLookupWorkload.WorkloadId)]
    [InlineData(RuntimeQueueDrainWorkload.WorkloadId)]
    [InlineData(RuntimeOutboxDrainWorkload.WorkloadId)]
    [Trait("Category", "LongRunning")]
    public async Task E3_leaf_executes_the_full_frozen_correctness_scenario(string workloadId)
    {
        Skip.IfNot(
            string.Equals(Environment.GetEnvironmentVariable("ELSA_E3_FULL_CORRECTNESS"), "1", StringComparison.Ordinal),
            "Set ELSA_E3_FULL_CORRECTNESS=1 to run the approximately ten-minute SQLite correctness baseline.");
        var catalog = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot());
        var workload = catalog.Workloads[workloadId];
        var request = await RequestAsync(workload);
        var nativePlan = Document(workload, request);

        await using var adapter = await BenchmarkAdapterFactory.CreateAsync(
            new AdapterContext(request, workload, nativePlan),
            CancellationToken.None);
        await adapter.PrepareAsync(CancellationToken.None);

        var correctness = await adapter.VerifyCorrectnessAsync(CancellationToken.None);

        Assert.Equal(workload.Correctness.ResultDigestSha256, correctness.ObservedResultDigestSha256);
        Assert.Equal(workload.RequiredNativeRoutes, correctness.NativePlan.Routes.Select(route => route.RouteIdentity));
    }

    private static async Task<RunRequest> RequestAsync(PerformanceWorkload workload)
    {
        await using var probe = GroundworkProviderDriverFactory.Create("sqlite");
        await probe.InitializeAsync();
        return new RunRequest(
            "e3-baseline",
            $"{workload.Id}-sqlite",
            workload.Id,
            workload.Version,
            "sqlite",
            "groundwork",
            "shared-documents-with-linked-index-tables",
            "medium",
            new string('a', 40),
            SourceProvenance.HarnessAssemblySha256(),
            new Dictionary<string, string> { ["Groundwork.Sqlite"] = "0.0.1-preview.131" },
            probe.CompositionFingerprint.Value,
            HostFingerprint.CaptureSha256(),
            probe.Descriptor.ProviderVersion,
            probe.Descriptor.Topology.Description,
            new Dictionary<string, string> { ["engine-version"] = probe.Descriptor.ProviderVersion },
            workload.Input.Seed,
            workload.Input.FingerprintSha256,
            $"{workload.Id}-sqlite-native",
            NativePlanEvidenceStaging.ReferenceFor(workload.Id, "sqlite"),
            new string('b', 64),
            ProcessKind.Measured,
            1);
    }

    private static NativePlanEvidenceDocument Document(PerformanceWorkload workload, RunRequest request) => new(
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
        workload.RequiredNativeRoutes.Select(route => new NativeRouteEvidence(
            route,
            $"{workload.Id}.{route}.txt",
            new string('c', 64),
            "provider-native-route-plan",
            "frozen-index",
            100_000,
            true,
            true,
            32,
            1)).ToArray());
}
