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
    internal const string GroundworkAspNetCoreIdentityAdapter = "groundwork-aspnetcore-identity";
    internal const string EfSecretRepositoryAdapterId = "ef-secret-repository";
    internal const string EfDiagnosticsAdapterId = "ef-diagnostics-oracle";
    internal const string GroundworkSecretRepositoryAdapterId = "groundwork-secret-repository";
    internal const string WorkloadVersion = "1.1.0";
    internal const string RecoveryWorkloadVersion = "1.2.0";
    internal const string CheckpointPhysicalForm = "checkpoint-unit-of-work-with-linked-outbox";

    private static readonly string[] GroundworkProviders = ["sqlite", "sqlserver", "postgresql", "mongodb"];
    private static readonly string[] SqliteOnly = ["sqlite"];

    private static readonly IReadOnlyList<AdapterRegistration> Registrations =
    [
        Registration("checkpoint-commit", WorkloadVersion, GroundworkV2Adapter, CheckpointPhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.Routeless,
            static (request, connection, output) => new CheckpointCommitAdapter(request, connection, output)),
        Registration(RuntimeBookmarkLookupWorkload.WorkloadId, WorkloadVersion, GroundworkV2Adapter, BookmarkLookupAdapter.PhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.Unsupported,
            static (request, connection, output) => new BookmarkLookupAdapter(request, connection, output)),
        Registration(RuntimeRecoveryScanWorkload.WorkloadId, RecoveryWorkloadVersion, GroundworkV2Adapter, RecoveryScanAdapter.PhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.Complete,
            static (request, connection, output) => new RecoveryScanAdapter(request, connection, output)),
        Registration(RuntimeQueueDrainWorkload.WorkloadId, WorkloadVersion, GroundworkV2Adapter, QueueDrainAdapter.PhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.Unsupported,
            static (request, connection, output) => new QueueDrainAdapter(request, connection, output)),
        Registration(RuntimeOutboxDrainWorkload.WorkloadId, WorkloadVersion, GroundworkV2Adapter, OutboxDrainAdapter.PhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.Unsupported,
            static (request, connection, output) => new OutboxDrainAdapter(request, connection, output)),
        Registration(RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId, WorkloadVersion, GroundworkV2Adapter, TriggerBindingStimulusLookupAdapter.PhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.Complete,
            static (request, connection, output) => new TriggerBindingStimulusLookupAdapter(request, connection, output)),
        Registration(RuntimeRecurringScheduleSelectionWorkload.WorkloadId, WorkloadVersion, GroundworkV2Adapter, RecurringScheduleSelectionAdapter.PhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.Complete,
            static (request, connection, output) => new RecurringScheduleSelectionAdapter(request, connection, output)),
        Registration(IamNormalizedLookupWorkload.WorkloadId, WorkloadVersion, GroundworkAspNetCoreIdentityAdapter, IamNormalizedLookupAdapter.PhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.Complete,
            static (request, connection, output) => new IamNormalizedLookupAdapter(request, connection, output)),
        Registration(DistributedPlacementTakeoverWorkload.WorkloadId, WorkloadVersion, GroundworkV2Adapter, DistributedPlacementTakeoverAdapter.PhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.Complete,
            static (request, connection, output) => new DistributedPlacementTakeoverAdapter(request, connection, output)),
        Registration(DistributedCommandSendLeaseAckWorkload.WorkloadId, WorkloadVersion, GroundworkV2Adapter, DistributedCommandSendLeaseAckAdapter.PhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.Unsupported,
            static (request, connection, output) => new DistributedCommandSendLeaseAckAdapter(request, connection, output)),
        Registration(RuntimeDueTimerSelectionWorkload.WorkloadId, WorkloadVersion, GroundworkV2Adapter, DueTimerSelectionAdapter.PhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.Complete,
            static (request, connection, output) => new DueTimerSelectionAdapter(request, connection, output)),
        Registration(SecretCreateReadListWorkload.WorkloadId, WorkloadVersion, EfSecretRepositoryAdapterId, EfSecretRepositoryAdapter.PhysicalForm,
            SqliteOnly, NativePlanCaptureKind.Complete,
            static (request, connection, output) => new EfSecretRepositoryAdapter(request, connection, output)),
        Registration(SecretCreateReadListWorkload.WorkloadId, WorkloadVersion, GroundworkSecretRepositoryAdapterId, GroundworkSecretRepositoryAdapter.PhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.Complete,
            static (request, connection, output) => new GroundworkSecretRepositoryAdapter(request, connection, output)),
        Registration(DiagnosticsDurableHistoryWorkload.WorkloadId, "1.2.0", DiagnosticsDurableHistoryAdapter.AdapterId, DiagnosticsDurableHistoryAdapter.PhysicalForm,
            GroundworkProviders, NativePlanCaptureKind.PartialBlocked,
            static (request, connection, output) => new DiagnosticsDurableHistoryAdapter(request, connection, output)),
        Registration(DiagnosticsDurableHistoryWorkload.WorkloadId, "1.2.0", EfDiagnosticsAdapterId, EfDiagnosticsDurableHistoryAdapter.PhysicalForm,
            SqliteOnly, NativePlanCaptureKind.CorrectnessOnly,
            static (request, connection, output) => new EfDiagnosticsDurableHistoryAdapter(request, connection, output))
    ];

    internal static IReadOnlyList<AdapterRegistrationDescriptor> Describe() =>
        Registrations.Select(registration => registration.Descriptor).ToArray();

    public static IBenchmarkAdapter Create(
        RunRequest request,
        string connectionString,
        string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(request);
        var registration = Registrations.SingleOrDefault(candidate => candidate.Descriptor.Matches(request));
        if (registration is null)
            throw new PerformanceContractException(
                $"No Groundwork adapter is registered for exact workload/version/adapter/physical form/provider " +
                $"'{request.WorkloadId}/{request.WorkloadVersion}/{request.Adapter}/{request.PhysicalForm}/{request.Provider}'.");
        return registration.Factory(request, connectionString, outputDirectory);
    }

    private static AdapterRegistration Registration(
        string workloadId,
        string workloadVersion,
        string adapter,
        string physicalForm,
        IReadOnlyList<string> providers,
        NativePlanCaptureKind nativePlanCapture,
        Func<RunRequest, string, string, IBenchmarkAdapter> factory) =>
        new(new(workloadId, workloadVersion, adapter, physicalForm, providers.ToArray(), nativePlanCapture), factory);

    private sealed record AdapterRegistration(
        AdapterRegistrationDescriptor Descriptor,
        Func<RunRequest, string, string, IBenchmarkAdapter> Factory);
}

internal enum NativePlanCaptureKind
{
    Unsupported,
    Routeless,
    Complete,
    PartialBlocked,
    CorrectnessOnly
}

internal sealed record AdapterRegistrationDescriptor(
    string WorkloadId,
    string WorkloadVersion,
    string Adapter,
    string PhysicalForm,
    IReadOnlyList<string> Providers,
    NativePlanCaptureKind NativePlanCapture)
{
    public bool Matches(RunRequest request) =>
        string.Equals(WorkloadId, request.WorkloadId, StringComparison.Ordinal) &&
        string.Equals(WorkloadVersion, request.WorkloadVersion, StringComparison.Ordinal) &&
        string.Equals(Adapter, request.Adapter, StringComparison.Ordinal) &&
        string.Equals(PhysicalForm, request.PhysicalForm, StringComparison.Ordinal) &&
        Providers.Contains(request.Provider, StringComparer.Ordinal);
}
