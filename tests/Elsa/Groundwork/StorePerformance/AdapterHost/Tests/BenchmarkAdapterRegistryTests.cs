using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
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
    public async Task Dispatches_outbox_drain_to_its_exact_groundwork_physical_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request("outbox-drain", OutboxDrainAdapter.PhysicalForm), "unused", "unused");

        Assert.IsType<OutboxDrainAdapter>(adapter);
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads["outbox-drain"];
        Assert.Contains(OutboxDrainAdapter.PhysicalForm, workload.PhysicalFormsFor646);
    }

    [Fact]
    public async Task Dispatches_trigger_binding_lookup_to_its_exact_groundwork_physical_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request("trigger-binding-stimulus-lookup", TriggerBindingStimulusLookupAdapter.PhysicalForm),
            "unused",
            "unused");

        Assert.IsType<TriggerBindingStimulusLookupAdapter>(adapter);
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads["trigger-binding-stimulus-lookup"];
        Assert.Contains(TriggerBindingStimulusLookupAdapter.PhysicalForm, workload.PhysicalFormsFor646);
    }

    [Fact]
    public async Task Dispatches_recurring_schedule_selection_to_its_exact_groundwork_physical_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request("recurring-schedule-selection", RecurringScheduleSelectionAdapter.PhysicalForm),
            "unused",
            "unused");

        Assert.IsType<RecurringScheduleSelectionAdapter>(adapter);
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads["recurring-schedule-selection"];
        Assert.Contains(RecurringScheduleSelectionAdapter.PhysicalForm, workload.PhysicalFormsFor646);
    }

    [Fact]
    public async Task Dispatches_iam_normalized_lookup_update_to_its_exact_groundwork_physical_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request(
                "iam-normalized-lookup-update",
                IamNormalizedLookupAdapter.PhysicalForm,
                adapter: BenchmarkAdapterRegistry.GroundworkAspNetCoreIdentityAdapter),
            "unused",
            "unused");

        Assert.IsType<IamNormalizedLookupAdapter>(adapter);
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads["iam-normalized-lookup-update"];
        Assert.Contains(IamNormalizedLookupAdapter.PhysicalForm, workload.PhysicalFormsFor646);
    }

    [Fact]
    public async Task Matrix_admission_dispatches_iam_to_the_ratified_identity_adapter()
    {
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads["iam-normalized-lookup-update"];
        var request = new MatrixRequest(
            "iam-admission-cohort",
            "iam-admission-set",
            workload.Id,
            workload.Version,
            "sqlite",
            BenchmarkAdapterRegistry.GroundworkAspNetCoreIdentityAdapter,
            IamNormalizedLookupAdapter.PhysicalForm,
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
            "iam-admission-native-plan",
            "iam-admission.native-plan.json",
            new string('e', 64));

        var plan = MatrixPlan.Create(workload, request);

        await using var adapter = BenchmarkAdapterRegistry.Create(plan.Runs[0], "unused", "unused");

        Assert.IsType<IamNormalizedLookupAdapter>(adapter);
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
    public async Task Outbox_drain_operations_are_not_admitted_before_correctness_preparation()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request("outbox-drain", OutboxDrainAdapter.PhysicalForm), "unused", "unused");

        var exception = Assert.Throws<PerformanceContractException>(() => adapter.Operations);

        Assert.Contains("before correctness preparation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Trigger_binding_lookup_operations_are_not_admitted_before_correctness_preparation()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request("trigger-binding-stimulus-lookup", TriggerBindingStimulusLookupAdapter.PhysicalForm),
            "unused",
            "unused");

        var exception = Assert.Throws<PerformanceContractException>(() => adapter.Operations);

        Assert.Contains("before correctness preparation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recurring_schedule_selection_operations_are_not_admitted_before_correctness_preparation()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request("recurring-schedule-selection", RecurringScheduleSelectionAdapter.PhysicalForm),
            "unused",
            "unused");

        var exception = Assert.Throws<PerformanceContractException>(() => adapter.Operations);

        Assert.Contains("before correctness preparation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Iam_normalized_lookup_update_operations_are_not_admitted_before_correctness_preparation()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request(
                "iam-normalized-lookup-update",
                IamNormalizedLookupAdapter.PhysicalForm,
                adapter: BenchmarkAdapterRegistry.GroundworkAspNetCoreIdentityAdapter),
            "unused",
            "unused");

        var exception = Assert.Throws<PerformanceContractException>(() => adapter.Operations);

        Assert.Contains("before correctness preparation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gives_each_iam_matrix_process_a_distinct_deterministic_persistence_scope()
    {
        await using var warmup = (IamNormalizedLookupAdapter)BenchmarkAdapterRegistry.Create(
            Request(
                "iam-normalized-lookup-update",
                IamNormalizedLookupAdapter.PhysicalForm,
                adapter: BenchmarkAdapterRegistry.GroundworkAspNetCoreIdentityAdapter) with
            {
                ProcessKind = ProcessKind.Warmup,
                ProcessIndex = 0
            },
            "unused",
            "unused");
        await using var measured = (IamNormalizedLookupAdapter)BenchmarkAdapterRegistry.Create(
            Request(
                "iam-normalized-lookup-update",
                IamNormalizedLookupAdapter.PhysicalForm,
                adapter: BenchmarkAdapterRegistry.GroundworkAspNetCoreIdentityAdapter) with
            {
                ProcessKind = ProcessKind.Measured,
                ProcessIndex = 1
            },
            "unused",
            "unused");
        await using var measuredRetry = (IamNormalizedLookupAdapter)BenchmarkAdapterRegistry.Create(
            Request(
                "iam-normalized-lookup-update",
                IamNormalizedLookupAdapter.PhysicalForm,
                adapter: BenchmarkAdapterRegistry.GroundworkAspNetCoreIdentityAdapter) with
            {
                ProcessKind = ProcessKind.Measured,
                ProcessIndex = 1
            },
            "unused",
            "unused");

        Assert.NotEqual(warmup.PersistenceScope, measured.PersistenceScope);
        Assert.Equal(measured.PersistenceScope, measuredRetry.PersistenceScope);
    }

    [Fact]
    public async Task Dispatches_placement_takeover_to_its_exact_groundwork_physical_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request("placement-takeover", DistributedPlacementTakeoverAdapter.PhysicalForm),
            "unused",
            "unused");

        Assert.IsType<DistributedPlacementTakeoverAdapter>(adapter);
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot()).Workloads["placement-takeover"];
        Assert.Contains(DistributedPlacementTakeoverAdapter.PhysicalForm, workload.PhysicalFormsFor646);
    }

    [Fact]
    public async Task Placement_takeover_operations_are_not_admitted_before_correctness_preparation()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request("placement-takeover", DistributedPlacementTakeoverAdapter.PhysicalForm),
            "unused",
            "unused");

        var exception = Assert.Throws<PerformanceContractException>(() => adapter.Operations);

        Assert.Contains("before correctness preparation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatches_command_send_lease_ack_to_its_exact_groundwork_physical_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request(DistributedCommandSendLeaseAckWorkload.WorkloadId, DistributedCommandSendLeaseAckAdapter.PhysicalForm),
            "unused",
            "unused");

        Assert.IsType<DistributedCommandSendLeaseAckAdapter>(adapter);
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot())
            .Workloads[DistributedCommandSendLeaseAckWorkload.WorkloadId];
        Assert.Contains(DistributedCommandSendLeaseAckAdapter.PhysicalForm, workload.PhysicalFormsFor646);
    }

    [Fact]
    public async Task Command_send_lease_ack_operations_are_not_admitted_before_correctness_preparation()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request(DistributedCommandSendLeaseAckWorkload.WorkloadId, DistributedCommandSendLeaseAckAdapter.PhysicalForm),
            "unused",
            "unused");

        var exception = Assert.Throws<PerformanceContractException>(() => adapter.Operations);

        Assert.Contains("before correctness preparation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatches_due_timer_selection_to_its_exact_groundwork_physical_form()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request(RuntimeDueTimerSelectionWorkload.WorkloadId, DueTimerSelectionAdapter.PhysicalForm),
            "unused",
            "unused");

        Assert.IsType<DueTimerSelectionAdapter>(adapter);
        var workload = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot())
            .Workloads[RuntimeDueTimerSelectionWorkload.WorkloadId];
        Assert.Contains(DueTimerSelectionAdapter.PhysicalForm, workload.PhysicalFormsFor646);
    }

    [Fact]
    public async Task Due_timer_selection_operations_are_not_admitted_before_correctness_preparation()
    {
        await using var adapter = BenchmarkAdapterRegistry.Create(
            Request(RuntimeDueTimerSelectionWorkload.WorkloadId, DueTimerSelectionAdapter.PhysicalForm),
            "unused",
            "unused");

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
    [InlineData("outbox-drain", "shared-documents-with-linked-index-tables", "groundwork-v2")]
    [InlineData("outbox-drain", "dedicated-post-commit-outbox-documents", "other-adapter")]
    [InlineData("outbox-drain", "dedicated-post-commit-outbox-documents", "groundwork-v2", "9.9.9")]
    [InlineData("trigger-binding-stimulus-lookup", "shared-documents-with-linked-index-tables", "groundwork-v2")]
    [InlineData("trigger-binding-stimulus-lookup", "linked-executable-source-reference-index", "other-adapter")]
    [InlineData("trigger-binding-stimulus-lookup", "linked-executable-source-reference-index", "groundwork-v2", "9.9.9")]
    [InlineData("recurring-schedule-selection", "shared-documents-with-linked-index-tables", "groundwork-v2")]
    [InlineData("recurring-schedule-selection", "dedicated-recurring-schedule-documents", "other-adapter")]
    [InlineData("recurring-schedule-selection", "dedicated-recurring-schedule-documents", "groundwork-v2", "9.9.9")]
    [InlineData("iam-normalized-lookup-update", "shared-documents-with-linked-index-tables", "groundwork-v2")]
    [InlineData("iam-normalized-lookup-update", "entity-type-specific-physical-tables-current-identity-shape", "other-adapter")]
    [InlineData("iam-normalized-lookup-update", "entity-type-specific-physical-tables-current-identity-shape", "groundwork-v2", "9.9.9")]
    [InlineData("placement-takeover", "shared-documents-with-linked-index-tables", "groundwork-v2")]
    [InlineData("placement-takeover", "dedicated-placement-lease-documents", "other-adapter")]
    [InlineData("placement-takeover", "dedicated-placement-lease-documents", "groundwork-v2", "9.9.9")]
    [InlineData("command-send-lease-ack", "shared-documents-with-linked-index-tables", "groundwork-v2")]
    [InlineData("command-send-lease-ack", "dedicated-command-transport-documents", "other-adapter")]
    [InlineData("command-send-lease-ack", "dedicated-command-transport-documents", "groundwork-v2", "9.9.9")]
    [InlineData("due-timer-selection", "shared-documents-with-linked-index-tables", "groundwork-v2")]
    [InlineData("due-timer-selection", "dedicated-durable-timer-documents", "other-adapter")]
    [InlineData("due-timer-selection", "dedicated-durable-timer-documents", "groundwork-v2", "9.9.9")]
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
