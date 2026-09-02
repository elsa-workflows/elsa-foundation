using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Machine-readable control plane for #646. The operator runner consumes this document instead of
/// maintaining a second workload/version/adapter/form table in Python.
/// </summary>
internal static class MatrixCatalog
{
    public static MatrixCatalogDocument Build(string repositoryRoot)
    {
        var workloads = WorkloadCatalog.Load(repositoryRoot).Workloads;
        var registrations = BenchmarkAdapterRegistry.Describe()
            .Select(registration => Describe(registration, workloads))
            .OrderBy(item => item.WorkloadId, StringComparer.Ordinal)
            .ThenBy(item => item.Adapter, StringComparer.Ordinal)
            .ToArray();

        var duplicateTargets = registrations
            .SelectMany(item => item.Providers.Select(provider =>
                $"{item.WorkloadId}/{item.Adapter}/{item.PhysicalForm}/{provider}"))
            .GroupBy(target => target, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (duplicateTargets.Length > 0)
            throw new PerformanceContractException(
                $"The adapter registry contains duplicate exact target(s): {string.Join(", ", duplicateTargets)}.");

        var missing = workloads.Keys
            .Except(registrations.Select(item => item.WorkloadId), StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (missing.Length > 0)
            throw new PerformanceContractException(
                $"The adapter registry does not cover current workload(s): {string.Join(", ", missing)}.");

        return new MatrixCatalogDocument(
            2,
            new MatrixBuildDocument(
                SourceProvenance.AssemblyRevision(typeof(MatrixCatalog).Assembly),
                SourceProvenance.AssemblyRevision(typeof(SourceProvenance).Assembly)),
            registrations);
    }

    private static MatrixRegistrationDocument Describe(
        AdapterRegistrationDescriptor registration,
        IReadOnlyDictionary<string, PerformanceWorkload> workloads)
    {
        if (!workloads.TryGetValue(registration.WorkloadId, out var workload))
            throw new PerformanceContractException(
                $"Adapter registration '{registration.WorkloadId}/{registration.Adapter}' is not in the current workload catalog.");
        if (!string.Equals(registration.WorkloadVersion, workload.Version, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Adapter registration '{registration.WorkloadId}/{registration.Adapter}' targets version " +
                $"'{registration.WorkloadVersion}', but the current workload version is '{workload.Version}'.");
        if (!workload.PhysicalFormsFor646.Contains(registration.PhysicalForm, StringComparer.Ordinal))
            throw new PerformanceContractException(
                $"Adapter registration '{registration.WorkloadId}/{registration.Adapter}' uses physical form " +
                $"'{registration.PhysicalForm}', which is not admitted by the current workload.");
        if (registration.Providers.Except(workload.RequiredProviders, StringComparer.Ordinal).Any())
            throw new PerformanceContractException(
                $"Adapter registration '{registration.WorkloadId}/{registration.Adapter}' names a provider outside the current workload contract.");

        var capture = CaptureStatus(registration.NativePlanCapture);
        var correctnessReady = registration.NativePlanCapture != NativePlanCaptureKind.Unsupported;
        var timingReady = workload.BenchmarkAdmission.IsReady &&
                          registration.NativePlanCapture is NativePlanCaptureKind.Routeless or NativePlanCaptureKind.Complete;
        var timingReason = timingReady
            ? ReproducibleWorkloadScenarioCatalog.ReadyReasonCode
            : workload.BenchmarkAdmission.IsReady
                ? capture.Reason
                : workload.BenchmarkAdmission.Reason;

        return new MatrixRegistrationDocument(
            registration.WorkloadId,
            registration.WorkloadVersion,
            registration.Adapter,
            registration.PhysicalForm,
            registration.Providers,
            workload.RequiredNativeRoutes,
            workload.RequiredProviderEvidence
                .Where(pair => registration.Providers.Contains(pair.Key, StringComparer.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            registration.Providers.ToDictionary(
                provider => provider,
                provider => ProviderPackageProvenance.RequiredPackageNames(registration.Adapter, provider),
                StringComparer.Ordinal),
            workload.Input.Seed,
            workload.Input.FingerprintSha256,
            workload.BenchmarkAdmission.Status,
            workload.BenchmarkAdmission.Reason,
            capture.Status,
            capture.Reason,
            correctnessReady ? "ready" : "blocked",
            correctnessReady ? "correctness.ready" : capture.Reason,
            timingReady ? "ready" : "blocked",
            timingReason);
    }

    private static (string Status, string Reason) CaptureStatus(NativePlanCaptureKind kind) => kind switch
    {
        NativePlanCaptureKind.Routeless => ("routeless", "capture.native-plan.not-required"),
        NativePlanCaptureKind.Complete => ("complete", "capture.native-plan.ready"),
        NativePlanCaptureKind.PartialBlocked => ("partial-blocked", "capture.native-plan.partial-blocked"),
        NativePlanCaptureKind.CorrectnessOnly => ("correctness-only", "capture.native-plan.correctness-only"),
        NativePlanCaptureKind.CorrectnessReadyNativePlanBlocked =>
            ("correctness-ready-native-plan-blocked", BenchmarkAdapterRegistry.MongoRuntimeNativePlanBlockedReason),
        _ => ("unsupported", "capture.native-plan.not-implemented")
    };
}

internal sealed record MatrixCatalogDocument(
    int SchemaVersion,
    MatrixBuildDocument Build,
    IReadOnlyList<MatrixRegistrationDocument> Registrations);

internal sealed record MatrixBuildDocument(
    string AdapterHostRevision,
    string HarnessRevision);

internal sealed record MatrixRegistrationDocument(
    string WorkloadId,
    string WorkloadVersion,
    string Adapter,
    string PhysicalForm,
    IReadOnlyList<string> Providers,
    IReadOnlyList<string> RequiredNativeRoutes,
    IReadOnlyDictionary<string, string> RequiredProviderTopologies,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ProviderPackages,
    string Seed,
    string InputFingerprintSha256,
    string BenchmarkAdmissionStatus,
    string BenchmarkAdmissionReason,
    string CapturePlanStatus,
    string CapturePlanReason,
    string CorrectnessStatus,
    string CorrectnessReason,
    string TimingStatus,
    string TimingReason);
