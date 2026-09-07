using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// Admission boundary for the capture-plan command. The command accepts a JSON request and an output
/// path, so both the frozen workload contract and the artifact safety rules must run before a provider is
/// opened or the output directory is touched.
/// </summary>
internal static class CapturePlanAdmission
{
    public static string Ensure(RunRequest request, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(request);
        var repositoryRoot = SourceProvenance.FindRepositoryRoot();
        var admittedOutput = ArtifactOutputAdmission.RequireExternal(outputDirectory, repositoryRoot);
        var catalog = WorkloadCatalog.Load(repositoryRoot);
        var workload = catalog.Workloads.TryGetValue(request.WorkloadId, out var candidate)
            ? candidate
            : throw new PerformanceContractException($"Workload '{request.WorkloadId}' is not in the frozen catalog.");
        if (string.Equals(request.WorkloadId, DiagnosticsDurableHistoryWorkload.WorkloadId, StringComparison.Ordinal))
            ArtifactAdmission.ValidateEvidenceRequest(workload, request);
        else
            ArtifactAdmission.ValidateRequest(workload, request);
        ProviderPackageProvenance.RequireExactCurrent(
            repositoryRoot,
            request.Adapter,
            request.Provider,
            request.PackageVersions);
        var registration = BenchmarkAdapterRegistry.Describe().SingleOrDefault(candidate => candidate.Matches(request));
        if (registration is null)
            throw new PerformanceContractException(
                $"No adapter is registered for capture-plan target '{request.WorkloadId}/{request.WorkloadVersion}/{request.Adapter}/{request.PhysicalForm}/{request.Provider}'.");
        if (registration.NativePlanCapture == NativePlanCaptureKind.CorrectnessReadyNativePlanBlocked)
            throw new PerformanceContractException(
                $"Native-plan capture is blocked for '{request.WorkloadId}/{request.Adapter}/{request.PhysicalForm}/{request.Provider}': " +
                $"{BenchmarkAdapterRegistry.MongoRuntimeNativePlanBlockedReason}; the correctness path remains admitted, " +
                "but MongoDB's descriptive observer command cannot be admitted as a bounded distinct plan, " +
                "so no provider will be opened and no native-plan evidence will be written.");
        if (registration.NativePlanCapture == NativePlanCaptureKind.Unsupported)
            throw new PerformanceContractException(
                $"Native-plan capture is not implemented for '{request.WorkloadId}/{request.Adapter}/{request.PhysicalForm}'; " +
                "the matrix remains blocked and no zero-route evidence will be synthesized.");

        var expectedReference = NativePlanEvidenceStaging.ReferenceFor(
            request.WorkloadId,
            request.Provider,
            request.MeasurementSetId);
        if (!string.Equals(request.NativePlanEvidenceReference, expectedReference, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Native-plan evidence must use '{expectedReference}' as --native-plan-evidence; received '{request.NativePlanEvidenceReference}'.");
        return admittedOutput;
    }
}
