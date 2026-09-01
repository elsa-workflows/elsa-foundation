using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

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
    }
}
