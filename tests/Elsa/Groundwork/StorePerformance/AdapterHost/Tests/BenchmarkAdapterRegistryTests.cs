using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class BenchmarkAdapterRegistryTests
{
    [Fact]
    public async Task Dispatches_bookmark_lookup_to_the_exact_groundwork_physical_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request("bookmark-lookup", "document-type-specific-tables"), "unused", "unused");

        Assert.IsType<BookmarkLookupAdapter>(adapter);
    }

    [Fact]
    public async Task Preserves_checkpoint_dispatch_for_its_exact_contract_key()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request("checkpoint-commit", "checkpoint-unit-of-work-with-linked-outbox"), "unused", "unused");

        Assert.IsType<CheckpointCommitAdapter>(adapter);
    }

    [Theory]
    [InlineData("bookmark-lookup", "checkpoint-unit-of-work-with-linked-outbox")]
    [InlineData("unknown-workload", "document-type-specific-tables")]
    [InlineData("bookmark-lookup", "unregistered-form")]
    public void Refuses_unregistered_workload_adapter_and_physical_form_without_fallback(
        string workloadId,
        string physicalForm)
    {
        var exception = Assert.Throws<PerformanceContractException>(() =>
            BenchmarkAdapterRegistry.Create(Request(workloadId, physicalForm), "unused", "unused"));

        Assert.Contains("exact workload/adapter/physical form", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_workload_version_that_is_not_the_registered_contract()
    {
        var exception = Assert.Throws<PerformanceContractException>(() =>
            BenchmarkAdapterRegistry.Create(Request("bookmark-lookup", "document-type-specific-tables", "9.9.9"), "unused", "unused"));

        Assert.Contains("exact workload/adapter/physical form", exception.Message, StringComparison.Ordinal);
    }

    private static RunRequest Request(string workloadId, string physicalForm, string workloadVersion = "1.1.0") => new(
        ComparisonCohortId: "cohort",
        MeasurementSetId: "set",
        WorkloadId: workloadId,
        WorkloadVersion: workloadVersion,
        Provider: "sqlite",
        ProviderVersion: "3.46.0",
        ProviderTopology: "file-backed-distinct-connections",
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal),
        Adapter: BenchmarkAdapterRegistry.GroundworkV2Adapter,
        PhysicalForm: physicalForm,
        Scale: "small",
        CommitSha: new string('a', 40),
        CompositionFingerprint: new string('b', 64),
        HarnessAssemblySha256: new string('c', 64),
        HostFingerprintSha256: new string('d', 64),
        PackageVersions: new Dictionary<string, string>(StringComparer.Ordinal),
        Seed: "seed",
        InputFingerprintSha256: new string('e', 64),
        NativePlanIdentity: "identity",
        NativePlanEvidenceReference: "native-plan.json",
        NativePlanContentSha256: new string('f', 64),
        ProcessKind: ProcessKind.Measured,
        ProcessIndex: 0);
}
