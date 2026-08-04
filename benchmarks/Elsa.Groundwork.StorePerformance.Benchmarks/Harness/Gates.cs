using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

public enum GateClass { RuntimeHotPath, OrdinaryStore }
public enum PerformanceVerdict { Pass, Redesign, Blocked, NotHotPath }
public sealed record GateReview(string WorkloadId, string WorkloadVersion, string ProposedBy, string ReviewedBy, string ReviewReference, string ReviewedAtUtc);
/// <summary>
/// Ratio gates plus an optional absolute p95 ceiling. The ceiling exists for rows with no comparand to
/// form a ratio against — the runtime family has never had an EF implementation — and is a
/// catastrophic-regression backstop, not a precision instrument. Ratified 2026-08-04; see
/// specs/094-harden-groundwork-stores/contracts/runtime-absolute-budget-basis.md.
/// </summary>
public sealed record GatePolicy(GateClass GateClass, double MaxP95Ratio, double MinThroughputRatio, double MaxP99Ratio, GateReview? Review, double? MaxP95Milliseconds = null)
{
    public static GatePolicy DefaultFor(GateClass gateClass) => gateClass == GateClass.RuntimeHotPath ? new(gateClass, 1.10, .90, 2.0, null) : new(gateClass, 1.25, .80, 2.0, null);
    public static GatePolicy Replacement(GateClass gateClass, double maxP95Ratio, double minThroughputRatio, double maxP99Ratio, GateReview review)
    {
        if (review is null || string.IsNullOrWhiteSpace(review.WorkloadId) || string.IsNullOrWhiteSpace(review.WorkloadVersion) || string.IsNullOrWhiteSpace(review.ReviewReference) || !DateTimeOffset.TryParse(review.ReviewedAtUtc, out _) || string.Equals(review.ProposedBy, review.ReviewedBy, StringComparison.OrdinalIgnoreCase)) throw new PerformanceContractException("A workload-specific gate replacement requires an independent reviewed record; self-authored amendments are rejected.");
        if (maxP95Ratio <= 0 || minThroughputRatio <= 0 || maxP99Ratio <= 0) throw new PerformanceContractException("Gate ratios must be positive.");
        return new GatePolicy(gateClass, maxP95Ratio, minThroughputRatio, maxP99Ratio, review);
    }
}
public sealed record ReviewedGateReplacement(int SchemaVersion, string WorkloadId, string WorkloadVersion, GateClass GateClass, double MaxP95Ratio, double MinThroughputRatio, double MaxP99Ratio, GateReview Review);
/// <summary>Per-operation metrics across the three measured processes. Raw samples remain keyed by
/// process index so hierarchical bootstrap calculations preserve process independence.</summary>
public sealed record ProcessAggregate(string Operation, IReadOnlyList<double> P50Milliseconds, IReadOnlyList<double> P95Milliseconds, IReadOnlyList<double> P99Milliseconds, IReadOnlyList<double> ThroughputPerSecond, IReadOnlyDictionary<int, IReadOnlyList<double>> RawLatenciesByProcess);
public sealed record RatioConfidenceInterval(double Low, double High);
public sealed record GateRow(string Operation, double P50Ratio, double P95Ratio, double ThroughputRatio, double P99Ratio, bool Pass, RatioConfidenceInterval P50RatioCi, RatioConfidenceInterval P95RatioCi, RatioConfidenceInterval P99RatioCi, double? P95Milliseconds = null, double? MaxP95Milliseconds = null);
public sealed record GateResult(int SchemaVersion, string ArtifactManifestSha256, string WorkloadId, string WorkloadVersion, string Provider, string Scale, string OracleTarget, string Target, GateClass GateClass, GateReview? ReplacementReview, PerformanceVerdict Verdict, string Reason, IReadOnlyList<GateRow> Rows);

public static class GatePolicyFile
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static GatePolicy Load(string path, string workloadId, string workloadVersion)
    {
        var bytes = File.ReadAllBytes(path);
        using (var json = JsonDocument.Parse(bytes)) ArtifactStore.RejectDuplicateProperties(json.RootElement);
        ReviewedGateReplacement document;
        try { document = JsonSerializer.Deserialize<ReviewedGateReplacement>(bytes, Options) ?? throw new PerformanceContractException("Reviewed replacement gate input is invalid."); }
        catch (JsonException exception) { throw new PerformanceContractException($"Reviewed replacement gate JSON is invalid: {exception.Message}"); }
        if (document.SchemaVersion != 1 || document.Review is null || document.WorkloadId != workloadId || document.WorkloadVersion != workloadVersion || document.Review.WorkloadId != workloadId || document.Review.WorkloadVersion != workloadVersion) throw new PerformanceContractException("Reviewed replacement gate does not match the comparison workload/version.");
        return GatePolicy.Replacement(document.GateClass, document.MaxP95Ratio, document.MinThroughputRatio, document.MaxP99Ratio, document.Review);
    }
    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}

public static class GateEvaluator
{
    public static GateResult Evaluate(GatePolicy policy, ComparisonResult comparison)
    {
        if (BenchmarkAdapterAdmission.TryGetComparisonBlockedReason(comparison, out var adapterBlockedReason))
            return Blocked(
                policy,
                comparison,
                $"Workload '{comparison.WorkloadId}' adapter/form is blocked from benchmark gating: {adapterBlockedReason}.");
        if (ReproducibleWorkloadScenarioCatalog.TryGetBlockedReason(
                comparison.WorkloadId,
                out var blockedReason))
            return Blocked(
                policy,
                comparison,
                $"Workload '{comparison.WorkloadId}' is blocked from benchmark gating: {blockedReason}.");
        if (!comparison.Complete || !comparison.CorrectnessEqual) return Blocked(policy, comparison, comparison.BlockReason ?? "Comparison is incomplete.");
        var oracleOperations = comparison.OracleOperations.OrderBy(operation => operation.Operation, StringComparer.Ordinal).ToArray();
        var targetOperations = comparison.TargetOperations.OrderBy(operation => operation.Operation, StringComparer.Ordinal).ToArray();
        if (!oracleOperations.Select(operation => operation.Operation).SequenceEqual(targetOperations.Select(operation => operation.Operation), StringComparer.Ordinal)) return Blocked(policy, comparison, "Oracle and target operation sets differ; no operation may be ignored.");
        var rows = new List<GateRow>();
        foreach (var candidate in targetOperations)
        {
            var baseline = oracleOperations.Single(operation => operation.Operation == candidate.Operation);
            if (!Complete(baseline) || !Complete(candidate)) return Blocked(policy, comparison, "Every verdict requires exactly three complete measured process runs and raw samples per target/operation.");
            var p50Ratio = Statistics.Median(candidate.P50Milliseconds) / Statistics.Median(baseline.P50Milliseconds);
            var p95Ratio = Statistics.Median(candidate.P95Milliseconds) / Statistics.Median(baseline.P95Milliseconds);
            var throughputRatio = Statistics.Median(candidate.ThroughputPerSecond) / Statistics.Median(baseline.ThroughputPerSecond);
            var p99Ratio = Statistics.Median(candidate.P99Milliseconds) / Statistics.Median(baseline.P99Milliseconds);
            // The absolute ceiling is evaluated alongside the ratios, never instead of them: a run can be
            // within budget and still be a regression against its own previous generation.
            var candidateP95 = Statistics.Median(candidate.P95Milliseconds);
            var withinCeiling = policy.MaxP95Milliseconds is not { } ceiling || candidateP95 <= ceiling;
            var pass = p95Ratio <= policy.MaxP95Ratio && throughputRatio >= policy.MinThroughputRatio && p99Ratio <= policy.MaxP99Ratio && withinCeiling;
            rows.Add(new GateRow(candidate.Operation, p50Ratio, p95Ratio, throughputRatio, p99Ratio, pass, RatioCi(baseline.RawLatenciesByProcess, candidate.RawLatenciesByProcess, 50), RatioCi(baseline.RawLatenciesByProcess, candidate.RawLatenciesByProcess, 95), RatioCi(baseline.RawLatenciesByProcess, candidate.RawLatenciesByProcess, 99), candidateP95, policy.MaxP95Milliseconds));
        }
        var verdict = rows.All(row => row.Pass) ? PerformanceVerdict.Pass : PerformanceVerdict.Redesign;
        return new GateResult(1, comparison.ArtifactManifestSha256, comparison.WorkloadId, comparison.WorkloadVersion, comparison.Provider, comparison.Scale, comparison.OracleTarget, comparison.Target, policy.GateClass, policy.Review, verdict, verdict == PerformanceVerdict.Pass ? "All default or independently reviewed gates passed." : rows.Any(row => row is { Pass: false, MaxP95Milliseconds: not null } failed && failed.P95Milliseconds > failed.MaxP95Milliseconds) ? "One or more absolute p95 ceilings were exceeded." : "One or more ratio gates failed.", rows);
    }

    private static bool Complete(ProcessAggregate operation) => operation.P50Milliseconds.Count == 3 && operation.P95Milliseconds.Count == 3 && operation.P99Milliseconds.Count == 3 && operation.ThroughputPerSecond.Count == 3 && operation.RawLatenciesByProcess.Keys.Order().SequenceEqual(new[] { 1, 2, 3 }) && operation.RawLatenciesByProcess.Values.All(samples => samples.Count >= 100 && samples.All(value => value > 0 && double.IsFinite(value))) && operation.P50Milliseconds.Concat(operation.P95Milliseconds).Concat(operation.P99Milliseconds).Concat(operation.ThroughputPerSecond).All(value => value > 0 && double.IsFinite(value));
    private static RatioConfidenceInterval RatioCi(IReadOnlyDictionary<int, IReadOnlyList<double>> oracle, IReadOnlyDictionary<int, IReadOnlyList<double>> target, double percentile)
    {
        var interval = Statistics.BootstrapPercentileRatioCi(oracle, target, percentile);
        return new RatioConfidenceInterval(interval.Low, interval.High);
    }
    private static GateResult Blocked(GatePolicy policy, ComparisonResult comparison, string reason) => new(1, comparison.ArtifactManifestSha256, comparison.WorkloadId, comparison.WorkloadVersion, comparison.Provider, comparison.Scale, comparison.OracleTarget, comparison.Target, policy.GateClass, policy.Review, PerformanceVerdict.Blocked, reason, []);
}
