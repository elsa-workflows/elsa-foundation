using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
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

    [Fact]
    public async Task Dispatches_queue_drain_to_its_exact_groundwork_physical_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request("queue-drain", QueueDrainAdapter.PhysicalForm), "unused", "unused");

        Assert.IsType<QueueDrainAdapter>(adapter);
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads["queue-drain"];
        Assert.Contains(QueueDrainAdapter.PhysicalForm, workload.PhysicalFormsFor646);
    }

    [Fact]
    public async Task Queue_drain_operations_are_not_admitted_before_correctness_preparation()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request("queue-drain", QueueDrainAdapter.PhysicalForm), "unused", "unused");

        var exception = Assert.Throws<PerformanceContractException>(() => adapter.Operations);

        Assert.Contains("before correctness preparation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gives_each_checkpoint_matrix_process_a_distinct_deterministic_persistence_scope()
    {
        await using var warmup = (CheckpointCommitAdapter)BenchmarkAdapterRegistry.Create(
            Request("checkpoint-commit", "checkpoint-unit-of-work-with-linked-outbox") with
            {
                ProcessKind = ProcessKind.Warmup,
                ProcessIndex = 0
            },
            "unused",
            "unused");
        await using var measured = (CheckpointCommitAdapter)BenchmarkAdapterRegistry.Create(
            Request("checkpoint-commit", "checkpoint-unit-of-work-with-linked-outbox") with
            {
                ProcessKind = ProcessKind.Measured,
                ProcessIndex = 1
            },
            "unused",
            "unused");
        await using var measuredRetry = (CheckpointCommitAdapter)BenchmarkAdapterRegistry.Create(
            Request("checkpoint-commit", "checkpoint-unit-of-work-with-linked-outbox") with
            {
                ProcessKind = ProcessKind.Measured,
                ProcessIndex = 1
            },
            "unused",
            "unused");

        Assert.NotEqual(warmup.PersistenceScope, measured.PersistenceScope);
        Assert.Equal(measured.PersistenceScope, measuredRetry.PersistenceScope);
    }

    [Theory]
    [InlineData("bookmark-lookup", "document-type-specific-tables", "other-adapter")]
    [InlineData("bookmark-lookup", "checkpoint-unit-of-work-with-linked-outbox", "groundwork-v2")]
    [InlineData("unknown-workload", "document-type-specific-tables", "groundwork-v2")]
    [InlineData("bookmark-lookup", "unregistered-form", "groundwork-v2")]
    [InlineData("queue-drain", "dedicated-scheduler-poison-documents", "groundwork-v2")]
    [InlineData("queue-drain", "dedicated-scheduler-work-documents", "other-adapter")]
    [InlineData("queue-drain", "dedicated-scheduler-work-documents", "groundwork-v2", "9.9.9")]
    public void Refuses_unregistered_workload_adapter_and_physical_form_without_fallback(
        string workloadId,
        string physicalForm,
        string adapter,
        string workloadVersion = "1.1.0")
    {
        var exception = Assert.Throws<PerformanceContractException>(() =>
            BenchmarkAdapterRegistry.Create(Request(workloadId, physicalForm, workloadVersion, adapter), "unused", "unused"));

        Assert.Contains("exact workload/adapter/physical form", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_workload_version_that_is_not_the_registered_contract()
    {
        var exception = Assert.Throws<PerformanceContractException>(() =>
            BenchmarkAdapterRegistry.Create(Request("bookmark-lookup", "document-type-specific-tables", "9.9.9"), "unused", "unused"));

        Assert.Contains("exact workload/adapter/physical form", exception.Message, StringComparison.Ordinal);
    }

    private static RunRequest Request(string workloadId, string physicalForm, string workloadVersion = "1.1.0", string adapter = BenchmarkAdapterRegistry.GroundworkV2Adapter) => new(
        ComparisonCohortId: "cohort",
        MeasurementSetId: "set",
        WorkloadId: workloadId,
        WorkloadVersion: workloadVersion,
        Provider: "sqlite",
        ProviderVersion: "3.46.0",
        ProviderTopology: "file-backed-distinct-connections",
        ProviderConfiguration: new Dictionary<string, string>(StringComparer.Ordinal),
        Adapter: adapter,
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
