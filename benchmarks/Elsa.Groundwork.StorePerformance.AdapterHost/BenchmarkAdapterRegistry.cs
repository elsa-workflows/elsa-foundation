using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Closed dispatch table for the child host. Adapter selection is an exact contract binding; an unknown
/// workload, adapter, or physical form is refused instead of silently receiving checkpoint behavior.
/// </summary>
internal static class BenchmarkAdapterRegistry
{
    internal const string GroundworkV2Adapter = "groundwork-v2";
    internal const string WorkloadVersion = "1.1.0";
    internal const string CheckpointPhysicalForm = "checkpoint-unit-of-work-with-linked-outbox";

    public static IBenchmarkAdapter Create(
        RunRequest request,
        string connectionString,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = new AdapterKey(request.WorkloadId, request.WorkloadVersion, request.Adapter, request.PhysicalForm);
        return key switch
        {
            { WorkloadId: "checkpoint-commit", WorkloadVersion: WorkloadVersion, Adapter: GroundworkV2Adapter, PhysicalForm: CheckpointPhysicalForm } =>
                new CheckpointCommitAdapter(request, connectionString, outputDirectory),
            { WorkloadId: RuntimeBookmarkLookupWorkload.WorkloadId, WorkloadVersion: WorkloadVersion, Adapter: GroundworkV2Adapter, PhysicalForm: BookmarkLookupAdapter.PhysicalForm } =>
                new BookmarkLookupAdapter(request, connectionString, outputDirectory),
            { WorkloadId: RuntimeQueueDrainWorkload.WorkloadId, WorkloadVersion: WorkloadVersion, Adapter: GroundworkV2Adapter, PhysicalForm: QueueDrainAdapter.PhysicalForm } =>
                new QueueDrainAdapter(request, connectionString, outputDirectory),
            { WorkloadId: RuntimeOutboxDrainWorkload.WorkloadId, WorkloadVersion: WorkloadVersion, Adapter: GroundworkV2Adapter, PhysicalForm: OutboxDrainAdapter.PhysicalForm } =>
                new OutboxDrainAdapter(request, connectionString, outputDirectory),
            { WorkloadId: RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId, WorkloadVersion: WorkloadVersion, Adapter: GroundworkV2Adapter, PhysicalForm: TriggerBindingStimulusLookupAdapter.PhysicalForm } =>
                new TriggerBindingStimulusLookupAdapter(request, connectionString, outputDirectory),
            { WorkloadId: RuntimeRecurringScheduleSelectionWorkload.WorkloadId, WorkloadVersion: WorkloadVersion, Adapter: GroundworkV2Adapter, PhysicalForm: RecurringScheduleSelectionAdapter.PhysicalForm } =>
                new RecurringScheduleSelectionAdapter(request, connectionString, outputDirectory),
            { WorkloadId: IamNormalizedLookupWorkload.WorkloadId, WorkloadVersion: WorkloadVersion, Adapter: GroundworkV2Adapter, PhysicalForm: IamNormalizedLookupAdapter.PhysicalForm } =>
                new IamNormalizedLookupAdapter(request, connectionString, outputDirectory),
            { WorkloadId: DistributedPlacementTakeoverWorkload.WorkloadId, WorkloadVersion: WorkloadVersion, Adapter: GroundworkV2Adapter, PhysicalForm: DistributedPlacementTakeoverAdapter.PhysicalForm } =>
                new DistributedPlacementTakeoverAdapter(request, connectionString, outputDirectory),
            _ => throw new PerformanceContractException(
                $"No Groundwork adapter is registered for exact workload/adapter/physical form '{request.WorkloadId}/{request.Adapter}/{request.PhysicalForm}'.")
        };
    }

    private readonly record struct AdapterKey(string WorkloadId, string WorkloadVersion, string Adapter, string PhysicalForm);
}
