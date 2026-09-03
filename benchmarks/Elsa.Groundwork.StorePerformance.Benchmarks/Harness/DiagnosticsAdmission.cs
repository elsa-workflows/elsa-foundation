using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>Execution phase used when applying workload admission.</summary>
public enum BenchmarkPhase
{
    NativePlan,
    Correctness,
    Measurement,
    Comparison,
    RatioGate,
    AbsoluteBudgetGate
}

/// <summary>
/// Keeps the program-owner-ratified diagnostics first-measurement exception phase-specific.
/// Diagnostics can produce provenance-bound, ungraded evidence for deriving a future policy, but the
/// blocked workload is never promoted to a comparison or ratio-gate candidate by this exception.
/// </summary>
public static class DiagnosticsAdmission
{
    public const string UngradedMeasurementStatus = "ungraded";
    public const string UngradedMeasurementReasonCode = "measurement.diagnostics.ungraded";
    public const string EfCorrectnessOnlyMeasurementReasonCode = "measurement.diagnostics.ef-correctness-only";

    internal static bool Allows(BenchmarkPhase phase) =>
        phase is BenchmarkPhase.NativePlan or BenchmarkPhase.Correctness or BenchmarkPhase.Measurement;

    internal static bool IsUngradedMeasurementAllowed(PerformanceWorkload workload)
    {
        ArgumentNullException.ThrowIfNull(workload);

        if (!string.Equals(workload.Id, ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId, StringComparison.Ordinal) ||
            !ReproducibleWorkloadScenarioCatalog.TryGetBlockedReason(workload.Id, out var blockedReason) ||
            !string.Equals(blockedReason, ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode, StringComparison.Ordinal) ||
            !string.Equals(workload.BenchmarkAdmission.Status, "blocked", StringComparison.Ordinal) ||
            !string.Equals(workload.BenchmarkAdmission.Reason, blockedReason, StringComparison.Ordinal))
            return false;

        // The source-bound successor vector is the minimum identity needed to distinguish the frozen
        // diagnostics v1.3 contract from a caller-forged workload that happens to carry the block code.
        var expected = ReproducibleWorkloadScenarioCatalog.Get(workload.Id);
        return workload.Version == expected.Version &&
               workload.ScenarioId == expected.ScenarioId &&
               workload.Input.Seed == expected.Seed &&
               workload.Input.FingerprintSha256 == expected.ComputeInputFingerprint() &&
               workload.Correctness.ResultDigestSha256 == expected.ComputeResultDigest() &&
               workload.OperationSequence.SequenceEqual(expected.OperationSequence, StringComparer.Ordinal);
    }

    internal static bool HasCompleteProviderNativePlan(
        PerformanceWorkload workload,
        NativePlanEvidence nativePlan)
    {
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(nativePlan);
        if (!string.Equals(nativePlan.RouteContract, "provider-native-routes", StringComparison.Ordinal) ||
            nativePlan.BlockedRoutes.Count != 0)
            return false;

        var admittedIdentities = nativePlan.Routes.Select(route => route.RouteIdentity)
            .Concat(nativePlan.TraceDetailConstituents.Count == 0 ? [] : ["trace-detail"])
            .ToArray();
        return admittedIdentities.Length == workload.RequiredNativeRoutes.Count &&
               admittedIdentities.Distinct(StringComparer.Ordinal).Count() == admittedIdentities.Length &&
               admittedIdentities.Order(StringComparer.Ordinal)
                   .SequenceEqual(workload.RequiredNativeRoutes.Order(StringComparer.Ordinal), StringComparer.Ordinal);
    }
}
