using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>
/// Keeps executable adapter/form selection separate from the immutable workload contract. A workload can
/// describe candidate physical forms without authorizing any production implementation to run them.
/// </summary>
public static class BenchmarkAdapterAdmission
{
    public const string IamWorkloadId = "iam-normalized-lookup-update";
    public const string IamMappingRequiredReason = "iam.adapter-form.ratification-required";

    // Deliberately empty: Spec 094 names candidate forms, not EF/Groundwork adapter bindings. A mapping
    // may only be added with the separate IAM admission decision; no adapter identity is inferred here.
    private static readonly IReadOnlySet<AdapterFormMapping> RatifiedIamProductionMappings =
        new HashSet<AdapterFormMapping>();

    public static void RequireAdmitted(PerformanceWorkload workload, string adapter, string physicalForm)
    {
        ArgumentNullException.ThrowIfNull(workload);
        if (TryGetBlockedReason(workload.Id, workload.Version, adapter, physicalForm, out var reason))
            throw new PerformanceContractException(
                $"Workload '{workload.Id}/{workload.Version}' adapter/form '{adapter}/{physicalForm}' is blocked from benchmark execution: {reason}.");
    }

    public static bool TryGetBlockedReason(string workloadId, string workloadVersion, string adapter, string physicalForm, out string reason)
    {
        if (string.Equals(workloadId, IamWorkloadId, StringComparison.Ordinal) &&
            !RatifiedIamProductionMappings.Contains(new AdapterFormMapping(workloadId, workloadVersion, adapter, physicalForm)))
        {
            reason = IamMappingRequiredReason;
            return true;
        }

        reason = "";
        return false;
    }

    public static bool TryGetComparisonBlockedReason(ComparisonResult comparison, out string reason)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        return TryGetTargetBlockedReason(comparison.WorkloadId, comparison.WorkloadVersion, comparison.OracleTarget, out reason) ||
               TryGetTargetBlockedReason(comparison.WorkloadId, comparison.WorkloadVersion, comparison.Target, out reason);
    }

    private static bool TryGetTargetBlockedReason(string workloadId, string workloadVersion, string target, out string reason)
    {
        if (!string.Equals(workloadId, IamWorkloadId, StringComparison.Ordinal))
        {
            reason = "";
            return false;
        }

        var segments = target.Split('/', StringSplitOptions.None);
        if (segments.Length != 3 || segments.Any(string.IsNullOrWhiteSpace))
        {
            reason = "benchmark.target.identity-invalid";
            return true;
        }

        return TryGetBlockedReason(workloadId, workloadVersion, segments[1], segments[2], out reason);
    }

    private sealed record AdapterFormMapping(string WorkloadId, string WorkloadVersion, string Adapter, string PhysicalForm);
}
