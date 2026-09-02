using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>A single absolute performance bound. These values are supplied by an independently
/// reviewed policy; the harness deliberately has no diagnostics defaults.</summary>
public sealed record AbsoluteBudget(
    double MaxP95Milliseconds,
    double MaxP99Milliseconds,
    double MinThroughputPerSecond);

/// <summary>
/// A no-comparand policy for one workload and provider. Budgets are keyed by exact frozen operation
/// identifiers, or by reviewed operation classes referenced by <see cref="OperationClasses"/>. An
/// omitted operation cannot accidentally pass as unbounded.
/// </summary>
public sealed record AbsoluteBudgetPolicy(
    int SchemaVersion,
    string WorkloadId,
    string WorkloadVersion,
    string Provider,
    IReadOnlyDictionary<string, AbsoluteBudget> Budgets,
    GateReview Review)
{
    /// <summary>Optional reviewed operation-to-class mapping. When present, each measured operation must
    /// resolve to exactly one class budget, or to the explicit NotHotPath class.</summary>
    public IReadOnlyDictionary<string, string>? OperationClasses { get; init; }

    public static AbsoluteBudgetPolicy Create(
        string workloadId,
        string workloadVersion,
        string provider,
        IReadOnlyDictionary<string, AbsoluteBudget> budgets,
        GateReview review) => new(1, workloadId, workloadVersion, provider, budgets, review);
}

public static class AbsoluteBudgetPolicyFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true
    };

    public static AbsoluteBudgetPolicy Load(string path, string workloadId, string workloadVersion, string provider)
    {
        var bytes = File.ReadAllBytes(path);
        using (var document = JsonDocument.Parse(bytes)) ArtifactStore.RejectDuplicateProperties(document.RootElement);

        AbsoluteBudgetPolicy policy;
        try
        {
            policy = JsonSerializer.Deserialize<AbsoluteBudgetPolicy>(bytes, Options)
                ?? throw new PerformanceContractException("Absolute budget policy is invalid.");
        }
        catch (JsonException exception)
        {
            throw new PerformanceContractException($"Absolute budget policy JSON is invalid: {exception.Message}");
        }

        ValidateIdentity(policy, workloadId, workloadVersion, provider);
        return policy;
    }

    public static string Hash(string path) => ArtifactStore.HashFile(path);

    internal static void ValidateIdentity(AbsoluteBudgetPolicy policy, string workloadId, string workloadVersion, string provider)
    {
        if (policy.SchemaVersion != 1 ||
            !string.Equals(policy.WorkloadId, workloadId, StringComparison.Ordinal) ||
            !string.Equals(policy.WorkloadVersion, workloadVersion, StringComparison.Ordinal) ||
            !string.Equals(policy.Provider, provider, StringComparison.Ordinal))
            throw new PerformanceContractException("Absolute budget policy does not match the measured workload, version, or provider.");
        if (policy.Review is null ||
            !string.Equals(policy.Review.WorkloadId, workloadId, StringComparison.Ordinal) ||
            !string.Equals(policy.Review.WorkloadVersion, workloadVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(policy.Review.ProposedBy) ||
            string.IsNullOrWhiteSpace(policy.Review.ReviewedBy) ||
            string.Equals(policy.Review.ProposedBy, policy.Review.ReviewedBy, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(policy.Review.ReviewReference) ||
            !DateTimeOffset.TryParse(policy.Review.ReviewedAtUtc, out _))
            throw new PerformanceContractException("An absolute budget policy requires an independent review for the exact workload/version.");
        if (policy.Budgets is null || policy.Budgets.Count == 0)
            throw new PerformanceContractException("An absolute budget policy must declare at least one operation budget.");
        foreach (var (operation, budget) in policy.Budgets)
        {
            if (!OperationIdentity.IsValid(operation) || budget is null ||
                !PositiveFinite(budget.MaxP95Milliseconds) ||
                !PositiveFinite(budget.MaxP99Milliseconds) ||
                !PositiveFinite(budget.MinThroughputPerSecond))
                throw new PerformanceContractException("Absolute operation budgets must have valid operation identities and finite positive p95, p99, and throughput bounds.");
        }
    }

    private static bool PositiveFinite(double value) => value > 0 && double.IsFinite(value);
}

/// <summary>One complete measured target with no oracle or ratio comparand.</summary>
public sealed record MeasurementResult(
    int SchemaVersion,
    string ArtifactManifestSha256,
    string WorkloadId,
    string WorkloadVersion,
    string Provider,
    string Scale,
    string Target,
    bool Complete,
    bool CorrectnessValid,
    IReadOnlyList<ProcessAggregate> Operations,
    string? BlockReason)
{
    public SecretProviderConcurrencyEvidence? ProviderConcurrency { get; init; }

    [JsonIgnore]
    internal MeasurementAdmission? Admission { get; init; }
}

internal sealed class MeasurementAdmission
{
    private readonly string snapshot;

    private MeasurementAdmission(MeasurementResult result) => snapshot = Serialize(result);

    internal static MeasurementAdmission Create(MeasurementResult result) => new(result);

    internal static bool TryValidate(MeasurementResult result, out string reason)
    {
        if (result.Admission is null)
        {
            reason = "The measurement result was not produced by the admitted no-comparand artifact path; operation evidence is not admitted.";
            return false;
        }

        if (!string.Equals(result.Admission.snapshot, Serialize(result), StringComparison.Ordinal))
        {
            reason = "The measurement result's manifest, identities, or raw operation evidence was changed after artifact admission.";
            return false;
        }

        if (!result.Complete || !result.CorrectnessValid)
        {
            reason = "";
            return true;
        }

        if (result.SchemaVersion != 1 ||
            result.ArtifactManifestSha256 is not { Length: 64 } hash ||
            hash.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            !OperationIdentity.IsValid(result.WorkloadId) ||
            !OperationIdentity.IsValid(result.WorkloadVersion) ||
            !OperationIdentity.IsValid(result.Provider) ||
            !OperationIdentity.IsValid(result.Scale) ||
            !OperationIdentity.IsValid(result.Target))
        {
            reason = "The admitted measurement has an invalid schema, manifest, or identity.";
            return false;
        }

        reason = "";
        return true;
    }

    private static string Serialize(MeasurementResult result) =>
        JsonSerializer.Serialize(result with { Admission = null }, ArtifactStore.JsonOptions);
}

public static class Measurement
{
    public static MeasurementResult Measure(string outputDirectory, WorkloadCatalog catalog)
    {
        var artifactSet = ArtifactStore.ReadAll(outputDirectory);
        var cohorts = artifactSet.Artifacts.Select(item => item.Request.ComparisonCohortId).Distinct(StringComparer.Ordinal).ToArray();
        var sets = artifactSet.Artifacts.GroupBy(item => item.Request.MeasurementSetId, StringComparer.Ordinal).ToArray();
        if (artifactSet.Artifacts.Count != 4 || cohorts.Length != 1 || sets.Length != 1 || sets[0].Count() != 4)
            return Blocked(artifactSet.ManifestSha256, null, "A no-comparand measurement requires exactly one complete four-process measurement set; a two-target comparison belongs to the comparison gate.");

        var validation = Comparison.ValidateSetForAbsoluteMeasurement(artifactSet.Artifacts, catalog, outputDirectory);
        if (!validation.Valid)
            return Blocked(artifactSet.ManifestSha256, validation.Anchor, validation.Error ?? "The measurement target is incomplete.");

        var anchor = validation.Anchor!;
        var nativePlan = validation.Correctness!.NativePlan;
        if (string.Equals(anchor.WorkloadId, DiagnosticsDurableHistoryWorkload.WorkloadId, StringComparison.Ordinal) &&
            (nativePlan.RouteContract != "provider-native-routes" || (nativePlan.BlockedRoutes ?? []).Count != 0 ||
             nativePlan.Routes.Count != catalog.Workloads[anchor.WorkloadId].RequiredNativeRoutes.Count))
            return Blocked(artifactSet.ManifestSha256, anchor, "Diagnostics absolute measurement requires a complete provider-native plan with every required route admitted; blocked routes remain non-timed evidence.");
        var result = new MeasurementResult(
            1,
            artifactSet.ManifestSha256,
            anchor.WorkloadId,
            anchor.WorkloadVersion,
            anchor.Provider,
            anchor.Scale,
            Comparison.Target(artifactSet.Artifacts[0]),
            true,
            true,
            Comparison.Aggregate(artifactSet.Artifacts),
            null)
        {
            ProviderConcurrency = validation.Correctness!.NativePlan.ProviderConcurrency
        };
        return result with { Admission = MeasurementAdmission.Create(result) };
    }

    private static MeasurementResult Blocked(string manifestHash, RunRequest? request, string reason)
    {
        var result = new MeasurementResult(1, manifestHash, request?.WorkloadId ?? "", request?.WorkloadVersion ?? "", request?.Provider ?? "", request?.Scale ?? "", request is null ? "" : $"{request.Provider}/{request.Adapter}/{request.PhysicalForm}", false, false, [], reason);
        return result with { Admission = MeasurementAdmission.Create(result) };
    }
}

public sealed record AbsoluteBudgetRow(
    string Operation,
    double? P95Milliseconds,
    double? MaxP95Milliseconds,
    bool P95Pass,
    double? P99Milliseconds,
    double? MaxP99Milliseconds,
    bool P99Pass,
    double? ThroughputPerSecond,
    double? MinThroughputPerSecond,
    bool ThroughputPass,
    bool Pass,
    string? OperationClass = null,
    PerformanceVerdict Verdict = PerformanceVerdict.Pass);

public sealed record AbsoluteBudgetResult(
    int SchemaVersion,
    string ArtifactManifestSha256,
    string WorkloadId,
    string WorkloadVersion,
    string Provider,
    string Scale,
    string Target,
    GateReview? Review,
    PerformanceVerdict Verdict,
    string Reason,
    IReadOnlyList<AbsoluteBudgetRow> Rows);

public static class AbsoluteBudgetEvaluator
{
    public static AbsoluteBudgetResult Evaluate(AbsoluteBudgetPolicy policy, MeasurementResult measurement)
    {
        if (policy is null) return Blocked("An absolute budget policy is required.");
        if (measurement is null) return Blocked("A no-comparand measurement is required.");
        if (!MeasurementAdmission.TryValidate(measurement, out var admissionReason)) return Blocked(admissionReason, measurement);
        if (!measurement.Complete || !measurement.CorrectnessValid) return Blocked(measurement.BlockReason ?? "Measurement is incomplete.", measurement);

        try { AbsoluteBudgetPolicyFile.ValidateIdentity(policy, measurement.WorkloadId, measurement.WorkloadVersion, measurement.Provider); }
        catch (PerformanceContractException exception) { return Blocked(exception.Message, measurement); }

        var operations = measurement.Operations?.OrderBy(operation => operation.Operation, StringComparer.Ordinal).ToArray();
        if (operations is null || operations.Length == 0 || operations.Any(operation => !OperationIdentity.IsValid(operation.Operation)) ||
            operations.Select(operation => operation.Operation).Distinct(StringComparer.Ordinal).Count() != operations.Length)
            return Blocked("A measurement must contain a non-empty unique operation set.", measurement);
        var expected = operations.Select(operation => operation.Operation).ToHashSet(StringComparer.Ordinal);
        if (policy.OperationClasses is not null && policy.OperationClasses.Any(pair =>
                !OperationIdentity.IsValid(pair.Key) ||
                (!string.Equals(pair.Value, "NotHotPath", StringComparison.Ordinal) && !OperationIdentity.IsValid(pair.Value))))
            return Blocked("Absolute budget operation classes must have valid operation and class identities.", measurement);
        var resolutions = new Dictionary<string, (AbsoluteBudget? Budget, string? Class, string Key)>(StringComparer.Ordinal);
        foreach (var operation in expected)
        {
            var mapped = policy.OperationClasses?.TryGetValue(operation, out var operationClass) == true ? operationClass : null;
            var direct = policy.Budgets.TryGetValue(operation, out var directBudget) ? directBudget : null;
            if (mapped is not null && direct is not null)
                return Blocked("An absolute budget operation cannot have both a direct budget and a class mapping.", measurement);
            if (string.Equals(mapped, "NotHotPath", StringComparison.Ordinal))
            {
                if (direct is not null) return Blocked("NotHotPath operations cannot also declare a numeric budget.", measurement);
                resolutions[operation] = (null, mapped, "NotHotPath");
            }
            else if (mapped is not null && policy.Budgets.TryGetValue(mapped, out var classBudget))
                resolutions[operation] = (classBudget, mapped, mapped);
            else if (mapped is not null)
                return Blocked("Every mapped budget-bearing operation must resolve to one declared class budget.", measurement);
            else if (direct is not null)
                resolutions[operation] = (direct, null, operation);
            else
                return Blocked("An absolute budget policy must declare exactly one budget for every measured operation; omitted operations are blocked.", measurement);
        }
        var usedBudgetKeys = resolutions.Values.Where(value => value.Budget is not null)
            .Select(value => value.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (policy.OperationClasses?.Keys.Any(key => !expected.Contains(key)) == true)
            return Blocked("An absolute budget operation-class map may mention only measured operations.", measurement);
        if (policy.Budgets.Keys.Any(key => !usedBudgetKeys.Contains(key)))
            return Blocked("An absolute budget policy must reject unused or extra budget entries.", measurement);

        var rows = new List<AbsoluteBudgetRow>(operations.Length);
        foreach (var operation in operations)
        {
            var resolution = resolutions[operation.Operation];
            if (resolution.Budget is null)
            {
                rows.Add(new(operation.Operation, null, null, true, null, null, true, null, null, true, true, resolution.Class, PerformanceVerdict.NotHotPath));
                continue;
            }
            if (!Complete(operation)) return Blocked("Every absolute-budget verdict requires exactly three complete measured process runs and raw samples per operation.", measurement);
            var budget = resolution.Budget;
            var p95 = Statistics.Median(operation.P95Milliseconds);
            var p99 = Statistics.Median(operation.P99Milliseconds);
            var throughput = Statistics.Median(operation.ThroughputPerSecond);
            var p95Pass = p95 <= budget.MaxP95Milliseconds;
            var p99Pass = p99 <= budget.MaxP99Milliseconds;
            var throughputPass = throughput >= budget.MinThroughputPerSecond;
            var pass = p95Pass && p99Pass && throughputPass;
            rows.Add(new(operation.Operation, p95, budget.MaxP95Milliseconds, p95Pass, p99, budget.MaxP99Milliseconds, p99Pass, throughput, budget.MinThroughputPerSecond, throughputPass, pass, resolution.Class ?? operation.Operation, pass ? PerformanceVerdict.Pass : PerformanceVerdict.Redesign));
        }

        var verdict = rows.All(row => row.Pass) ? PerformanceVerdict.Pass : PerformanceVerdict.Redesign;
        var reason = verdict == PerformanceVerdict.Pass
            ? "All independently reviewed absolute performance budgets passed without a comparand."
            : "One or more independently reviewed absolute performance budgets failed.";
        return new AbsoluteBudgetResult(1, measurement.ArtifactManifestSha256, measurement.WorkloadId, measurement.WorkloadVersion, measurement.Provider, measurement.Scale, measurement.Target, policy.Review, verdict, reason, rows);
    }

    internal static AbsoluteBudgetResult BlockedForContractFailure(string reason) => Blocked(reason);

    internal static AbsoluteBudgetResult BlockedForIncompleteMeasurement(MeasurementResult measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        return Blocked(measurement.BlockReason ?? "Measurement is incomplete.", measurement);
    }

    private static AbsoluteBudgetResult Blocked(string reason, MeasurementResult? measurement = null) =>
        new(1, measurement?.ArtifactManifestSha256 ?? "", measurement?.WorkloadId ?? "", measurement?.WorkloadVersion ?? "", measurement?.Provider ?? "", measurement?.Scale ?? "", measurement?.Target ?? "", null, PerformanceVerdict.Blocked, reason, []);

    private static bool Complete(ProcessAggregate operation) =>
        operation is not null && operation.P50Milliseconds is not null && operation.P95Milliseconds is not null && operation.P99Milliseconds is not null && operation.ThroughputPerSecond is not null && operation.RawLatenciesByProcess is not null && operation.P50Milliseconds.Count == 3 && operation.P95Milliseconds.Count == 3 && operation.P99Milliseconds.Count == 3 && operation.ThroughputPerSecond.Count == 3 && operation.RawLatenciesByProcess.Keys.Order().SequenceEqual(new[] { 1, 2, 3 }) && operation.RawLatenciesByProcess.Values.All(samples => samples is not null && samples.Count >= 100 && samples.All(value => value > 0 && double.IsFinite(value))) && operation.P50Milliseconds.Concat(operation.P95Milliseconds).Concat(operation.P99Milliseconds).Concat(operation.ThroughputPerSecond).All(value => value > 0 && double.IsFinite(value));

}
