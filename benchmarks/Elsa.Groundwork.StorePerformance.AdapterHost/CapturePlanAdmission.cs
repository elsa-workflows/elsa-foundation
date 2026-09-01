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
    public static void Ensure(RunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var catalog = WorkloadCatalog.Load(SourceProvenance.FindRepositoryRoot());
        var workload = catalog.Workloads.TryGetValue(request.WorkloadId, out var candidate)
            ? candidate
            : throw new PerformanceContractException($"Workload '{request.WorkloadId}' is not in the frozen catalog.");
        ArtifactAdmission.ValidateRequest(workload, request);
        if (!string.Equals(request.WorkloadId, RuntimeCheckpointCommitWorkload.WorkloadId, StringComparison.Ordinal) &&
            !string.Equals(request.WorkloadId, IamNormalizedLookupWorkload.WorkloadId, StringComparison.Ordinal) &&
            !string.Equals(request.WorkloadId, SecretCreateReadListWorkload.WorkloadId, StringComparison.Ordinal) &&
            !string.Equals(request.WorkloadId, RuntimeRecoveryScanWorkload.WorkloadId, StringComparison.Ordinal))
            throw new PerformanceContractException(
                "The capture-plan command supports only checkpoint-commit, iam-normalized-lookup-update, secret-create-read-list, and recovery-scan.");

        var expectedReference = NativePlanEvidenceStaging.ReferenceFor(
            request.WorkloadId,
            request.Provider,
            request.MeasurementSetId);
        if (!string.Equals(request.NativePlanEvidenceReference, expectedReference, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"Native-plan evidence must use '{expectedReference}' as --native-plan-evidence; received '{request.NativePlanEvidenceReference}'.");
    }
}
