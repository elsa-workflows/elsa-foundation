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
    public const string SecretWorkloadId = "secret-create-read-list";
    public const string SecretMappingRequiredReason = "secret.adapter-form.ratification-required";
    public const string SecretEfProviderRequiredReason = "secret.ef.provider.sqlite-required";
    public const string DiagnosticsWorkloadId = "diagnostics-durable-history";
    public const string DiagnosticsMappingRequiredReason = "diagnostics.adapter-form.ratification-required";
    public const string DiagnosticsEfProviderRequiredReason = "diagnostics.ef.provider.sqlite-required";

    private static readonly IReadOnlySet<AdapterFormMapping> RatifiedIamProductionMappings =
        new HashSet<AdapterFormMapping>
        {
            new(IamWorkloadId, "1.1.0", "ef-aspnetcore-identity", "ef-identity-relational-schema"),
            new(IamWorkloadId, "1.1.0", "groundwork-aspnetcore-identity", "entity-type-specific-physical-tables-current-identity-shape")
        };

    private static readonly IReadOnlySet<AdapterFormMapping> RatifiedSecretProductionMappings =
        new HashSet<AdapterFormMapping>
        {
            new(SecretWorkloadId, "1.1.0", "ef-secret-repository", "entity-type-specific-physical-tables"),
            new(SecretWorkloadId, "1.1.0", "groundwork-secret-repository", "entity-type-specific-physical-tables")
        };

    private static readonly IReadOnlySet<AdapterFormMapping> RatifiedDiagnosticsProductionMappings =
        new HashSet<AdapterFormMapping>
        {
            new(DiagnosticsWorkloadId, "1.3.0", "groundwork-v2", "ordinary-groundwork-diagnostics-units"),
            new(DiagnosticsWorkloadId, "1.3.0", "ef-diagnostics-oracle", "efcore-diagnostics-relational-tables")
        };

    public static void RequireAdmitted(
        PerformanceWorkload workload,
        string provider,
        string adapter,
        string physicalForm)
    {
        ArgumentNullException.ThrowIfNull(workload);
        if (TryGetBlockedReason(workload.Id, workload.Version, provider, adapter, physicalForm, out var reason))
            throw new PerformanceContractException(
                $"Workload '{workload.Id}/{workload.Version}' provider/adapter/form '{provider}/{adapter}/{physicalForm}' is blocked from benchmark execution: {reason}.");
    }

    public static bool TryGetBlockedReason(
        string workloadId,
        string workloadVersion,
        string provider,
        string adapter,
        string physicalForm,
        out string reason)
    {
        if (string.Equals(workloadId, IamWorkloadId, StringComparison.Ordinal) &&
            !RatifiedIamProductionMappings.Contains(new AdapterFormMapping(workloadId, workloadVersion, adapter, physicalForm)))
        {
            reason = IamMappingRequiredReason;
            return true;
        }

        if (string.Equals(workloadId, SecretWorkloadId, StringComparison.Ordinal) &&
            !RatifiedSecretProductionMappings.Contains(new AdapterFormMapping(workloadId, workloadVersion, adapter, physicalForm)))
        {
            reason = SecretMappingRequiredReason;
            return true;
        }

        if (string.Equals(workloadId, DiagnosticsWorkloadId, StringComparison.Ordinal) &&
            !RatifiedDiagnosticsProductionMappings.Contains(new AdapterFormMapping(workloadId, workloadVersion, adapter, physicalForm)))
        {
            reason = DiagnosticsMappingRequiredReason;
            return true;
        }

        if (string.Equals(workloadId, DiagnosticsWorkloadId, StringComparison.Ordinal) &&
            string.Equals(adapter, "ef-diagnostics-oracle", StringComparison.Ordinal) &&
            !string.Equals(provider, "sqlite", StringComparison.Ordinal))
        {
            reason = DiagnosticsEfProviderRequiredReason;
            return true;
        }

        if (string.Equals(workloadId, SecretWorkloadId, StringComparison.Ordinal) &&
            string.Equals(adapter, "ef-secret-repository", StringComparison.Ordinal) &&
            !string.Equals(provider, "sqlite", StringComparison.Ordinal))
        {
            reason = SecretEfProviderRequiredReason;
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
        if (!string.Equals(workloadId, IamWorkloadId, StringComparison.Ordinal) &&
            !string.Equals(workloadId, SecretWorkloadId, StringComparison.Ordinal))
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

        return TryGetBlockedReason(workloadId, workloadVersion, segments[0], segments[1], segments[2], out reason);
    }

    private sealed record AdapterFormMapping(string WorkloadId, string WorkloadVersion, string Adapter, string PhysicalForm);
}
