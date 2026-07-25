using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;

/// <summary>
/// The closed #646 input boundary. Historical hashes remain frozen while reviewed v1.1 successors
/// must reproduce their benchmark-owned contract vectors; neither may be silently reinterpreted.
/// </summary>
public sealed class WorkloadCatalog
{
    private static readonly string[] RequiredFiles =
        ["runtime.json", "iam-secrets.json", "distributed-runtime.json", "diagnostics.json"];
    private static readonly string[] Providers = ["sqlite", "sqlserver", "postgresql", "mongodb"];
    private static readonly Regex Slug = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Sha256 = new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SemVer = new("^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private WorkloadCatalog(IReadOnlyDictionary<string, PerformanceWorkload> workloads) => Workloads = workloads;

    public IReadOnlyDictionary<string, PerformanceWorkload> Workloads { get; }

    public static WorkloadCatalog Load(string repositoryRoot) =>
        Load(repositoryRoot, ExpectedSourceDigests);

    internal static WorkloadCatalog Load(
        string repositoryRoot,
        IReadOnlyDictionary<string, string> expectedSourceDigests)
    {
        var workloadDirectory = Path.Combine(repositoryRoot, "specs", "094-harden-groundwork-stores", "workloads");
        if (!Directory.Exists(workloadDirectory))
            throw new WorkloadContractException($"The required Spec 094 workload directory was not found: {workloadDirectory}");

        var discovered = Directory.EnumerateFiles(workloadDirectory, "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        if (!discovered.SequenceEqual(RequiredFiles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new WorkloadContractException(
                "The workload directory must contain exactly runtime.json, iam-secrets.json, distributed-runtime.json, and diagnostics.json.");

        var workloads = new Dictionary<string, PerformanceWorkload>(StringComparer.Ordinal);
        foreach (var file in RequiredFiles)
        {
            var path = Path.Combine(workloadDirectory, file);
            var source = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(source);
            var root = document.RootElement;
            RequireObject(root, file);
            RequireClosedProperties(root, ["schemaVersion", "workloads"], file);
            if (RequireInt(root, "schemaVersion", file) != 1)
                throw new WorkloadContractException($"{file} must have schemaVersion 1.");
            var entries = RequireArray(root, "workloads", file);
            if (entries.GetArrayLength() == 0)
                throw new WorkloadContractException($"{file} must contain at least one workload.");
            foreach (var value in entries.EnumerateArray())
            {
                var workload = ParseWorkload(value, file);
                if (!workloads.TryAdd(workload.Id, workload))
                    throw new WorkloadContractException($"Duplicate workload id '{workload.Id}'.");
            }
            if (!expectedSourceDigests.TryGetValue(file, out var expectedSourceDigest) ||
                !string.Equals(Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(), expectedSourceDigest, StringComparison.Ordinal))
                throw new WorkloadContractException($"{file} does not match the frozen Spec 094 #646 source contract.");
        }

        if (workloads.Count != Expected.Count || !workloads.Keys.Order(StringComparer.Ordinal).SequenceEqual(Expected.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new WorkloadContractException("The #646 catalog must contain exactly the thirteen reviewed Spec 094 workload definitions.");
        foreach (var (id, expected) in Expected)
            ValidateFrozenContract(workloads[id], expected);
        return new WorkloadCatalog(workloads);
    }

    private static PerformanceWorkload ParseWorkload(JsonElement value, string source)
    {
        RequireObject(value, source);
        RequireClosedProperties(value,
        ["id", "version", "scenarioId", "owner", "handoffTarget", "publicOperation", "coverageRows", "input", "operationSequence", "requiredProviders", "requiredNativeRoutes", "requiredProviderEvidence", "correctness", "efContractBaseline", "benchmarkAdmission", "physicalFormsFor646", "artifactRetention"], source);

        var id = RequireString(value, "id", source);
        var version = RequireString(value, "version", source);
        if (!Slug.IsMatch(id) || !SemVer.IsMatch(version) || !Slug.IsMatch(RequireString(value, "scenarioId", source)))
            throw new WorkloadContractException($"{source} contains an invalid workload identity or version.");
        if (!Regex.IsMatch(RequireString(value, "owner", source), "^#[0-9]+$", RegexOptions.CultureInvariant) || RequireString(value, "handoffTarget", source) != "#646")
            throw new WorkloadContractException($"{source}/{id} has an invalid ownership handoff.");

        var inputValue = RequireProperty(value, "input", source);
        RequireObject(inputValue, source);
        var input = new WorkloadInput(RequireString(inputValue, "seed", source), RequireSha(inputValue, "fingerprintSha256", source), inputValue.Clone());
        var coverageRows = RequireSlugs(RequireArray(value, "coverageRows", source), source, "coverageRows", allowEmpty: false);
        var operationSequence = RequireSlugs(RequireArray(value, "operationSequence", source), source, "operationSequence", allowEmpty: false);
        var requiredProviders = RequireStrings(RequireArray(value, "requiredProviders", source), source, "requiredProviders", allowEmpty: false);
        if (!requiredProviders.SequenceEqual(Providers, StringComparer.Ordinal))
            throw new WorkloadContractException($"{source}/{id} must require SQLite, SQL Server, PostgreSQL, and MongoDB in contract order.");
        var routes = RequireSlugs(RequireArray(value, "requiredNativeRoutes", source), source, "requiredNativeRoutes", allowEmpty: true);
        var providerEvidence = ParseProviderEvidence(RequireProperty(value, "requiredProviderEvidence", source), source);
        var correctness = ParseCorrectness(RequireProperty(value, "correctness", source), source);
        var baseline = ParseEfBaseline(RequireProperty(value, "efContractBaseline", source), source);
        var admission = ParseBenchmarkAdmission(RequireProperty(value, "benchmarkAdmission", source), source);
        var physicalForms = RequireStrings(RequireArray(value, "physicalFormsFor646", source), source, "physicalFormsFor646", allowEmpty: false);
        var retention = RequireStrings(RequireArray(value, "artifactRetention", source), source, "artifactRetention", allowEmpty: false);

        return new PerformanceWorkload(id, version, RequireString(value, "scenarioId", source), RequireString(value, "owner", source), RequireString(value, "publicOperation", source), coverageRows, input, operationSequence, requiredProviders, routes, providerEvidence, correctness, baseline, admission, physicalForms, retention);
    }

    private static IReadOnlyDictionary<string, string> ParseProviderEvidence(JsonElement value, string source)
    {
        RequireObject(value, source);
        RequireClosedProperties(value, Providers, source);
        return Providers.ToDictionary(provider => provider, provider => RequireString(value, provider, source), StringComparer.Ordinal);
    }

    private static CorrectnessContract ParseCorrectness(JsonElement value, string source)
    {
        RequireObject(value, source);
        RequireClosedProperties(value, ["resultDigestSha256", "invariants", "timingGate"], source);
        return new CorrectnessContract(RequireSha(value, "resultDigestSha256", source), RequireStrings(RequireArray(value, "invariants", source), source, "invariants", allowEmpty: false), RequireString(value, "timingGate", source));
    }

    private static EfContractBaseline ParseEfBaseline(JsonElement value, string source)
    {
        RequireObject(value, source);
        RequireClosedProperties(value, ["baselineIdentity", "executionStatus", "executionOwner", "purpose"], source);
        var status = RequireString(value, "executionStatus", source);
        var owner = RequireString(value, "executionOwner", source);
        if (status != "not-executed" || owner != "#646")
            throw new WorkloadContractException($"{source} has an invalid EF contract baseline declaration.");
        return new EfContractBaseline(RequireString(value, "baselineIdentity", source), status, owner, RequireString(value, "purpose", source));
    }

    private static BenchmarkAdmission ParseBenchmarkAdmission(JsonElement value, string source)
    {
        RequireObject(value, source);
        RequireClosedProperties(value, ["status", "reason"], source);
        var status = RequireString(value, "status", source);
        var reason = RequireString(value, "reason", source);
        if (status is not ("ready" or "blocked") ||
            !Regex.IsMatch(reason, "^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant))
            throw new WorkloadContractException($"{source} has an invalid benchmark admission declaration.");
        return new BenchmarkAdmission(status, reason);
    }

    private static void ValidateFrozenContract(PerformanceWorkload actual, ExpectedWorkload expected)
    {
        if (!actual.CoverageRows.SequenceEqual(expected.CoverageRows, StringComparer.Ordinal) ||
            !actual.PhysicalFormsFor646.SequenceEqual(expected.PhysicalForms, StringComparer.Ordinal))
            throw new WorkloadContractException($"The workload '{actual.Id}' does not match its frozen Spec 094 #646 handoff contract.");

        if (!ReproducibleWorkloadScenarioCatalog.GoldenVectors.TryGetValue(actual.Id, out var golden) ||
            actual.Input.FingerprintSha256 != golden.InputFingerprint ||
            actual.Correctness.ResultDigestSha256 != golden.ResultDigest)
            throw new WorkloadContractException($"The workload '{actual.Id}' does not match its independent frozen input/result golden vector.");

        if (ReproducibleWorkloadScenarioCatalog.Successors.TryGetValue(actual.Id, out var successor))
        {
            if (actual.Version != successor.Version ||
                actual.ScenarioId != successor.ScenarioId ||
                actual.Input.Seed != successor.Seed ||
                successor.ComputeInputFingerprint() != golden.InputFingerprint ||
                successor.ComputeResultDigest() != golden.ResultDigest ||
                !SemanticInputMatches(actual.Input.Values, successor.Parameters) ||
                !actual.OperationSequence.SequenceEqual(successor.OperationSequence, StringComparer.Ordinal))
                throw new WorkloadContractException($"The workload '{actual.Id}' does not match its reproducible v1.1 contract vector, including every semantic input field.");
            if (ReproducibleWorkloadScenarioCatalog.TryGetBlockedReason(actual.Id, out var blockedReason))
                RequireAdmission(actual, "blocked", blockedReason);
            else
                RequireAdmission(actual, "ready", ReproducibleWorkloadScenarioCatalog.ReadyReasonCode);
            return;
        }

        if (actual.Id == IamNormalizedLookupWorkload.WorkloadId)
        {
            if (actual.Version != "1.1.0" ||
                actual.ScenarioId != IamNormalizedLookupWorkload.ScenarioId ||
                actual.Input.Seed != IamNormalizedLookupWorkload.Seed ||
                IamNormalizedLookupWorkload.ComputeInputFingerprint() != golden.InputFingerprint ||
                IamNormalizedLookupWorkload.ExpectedResultDigest != golden.ResultDigest ||
                !SemanticInputMatches(actual.Input.Values, IamSemanticInputParameters()) ||
                !actual.OperationSequence.SequenceEqual(IamNormalizedLookupWorkload.OperationSequence, StringComparer.Ordinal))
                throw new WorkloadContractException(
                    $"The workload '{actual.Id}' does not match its independent Identity v1.1 golden vector, including every semantic input field.");
            RequireAdmission(actual, "ready", ReproducibleWorkloadScenarioCatalog.ReadyReasonCode);
            return;
        }

        if (actual.Id != ReproducibleWorkloadScenarioCatalog.BlockedWorkloadId ||
            actual.Version != ReproducibleWorkloadScenarioCatalog.BlockedVersion ||
            ReproducibleWorkloadScenarioCatalog.BlockedInputFingerprint != golden.InputFingerprint ||
            ReproducibleWorkloadScenarioCatalog.BlockedResultDigest != golden.ResultDigest)
            throw new WorkloadContractException($"The workload '{actual.Id}' is not the explicitly blocked Secret comparator contract.");
        RequireAdmission(actual, "blocked", ReproducibleWorkloadScenarioCatalog.BlockedReasonCode);
    }

    private static IReadOnlyDictionary<string, object> IamSemanticInputParameters()
    {
        var input = IamNormalizedLookupWorkload.InputDefinition;
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["tenantCount"] = input.TenantCount,
            ["canonicalUserCount"] = input.CanonicalUserCount,
            ["noiseUserCount"] = input.NoiseUserCount,
            ["roleCount"] = input.RoleCount,
            ["userRoleLinkCount"] = input.UserRoleLinkCount,
            ["concurrentContenders"] = input.ConcurrentContenders
        };
    }

    private static bool SemanticInputMatches(
        JsonElement input,
        IReadOnlyDictionary<string, object> parameters)
    {
        var inputProperties = input.EnumerateObject().ToArray();
        if (inputProperties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() !=
            inputProperties.Length)
            return false;

        var semanticProperties = inputProperties
            .Where(property => property.Name is not ("seed" or "fingerprintSha256"))
            .ToDictionary(property => property.Name, property => property.Value, StringComparer.Ordinal);
        if (!semanticProperties.Keys.Order(StringComparer.Ordinal)
            .SequenceEqual(parameters.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            return false;

        return parameters.All(pair =>
            pair.Value switch
            {
                int expected => semanticProperties[pair.Key].ValueKind == JsonValueKind.Number &&
                                semanticProperties[pair.Key].TryGetInt32(out var actual) &&
                                actual == expected,
                string expected => semanticProperties[pair.Key].ValueKind == JsonValueKind.String &&
                                   semanticProperties[pair.Key].GetString() == expected,
                bool expected => semanticProperties[pair.Key].ValueKind is JsonValueKind.True or JsonValueKind.False &&
                                 semanticProperties[pair.Key].GetBoolean() == expected,
                _ => false
            });
    }

    private static void RequireAdmission(PerformanceWorkload workload, string status, string reason)
    {
        if (workload.BenchmarkAdmission.Status != status ||
            workload.BenchmarkAdmission.Reason != reason)
            throw new WorkloadContractException(
                $"The workload '{workload.Id}' does not match its reviewed benchmark admission status/reason.");
    }

    private static JsonElement RequireProperty(JsonElement value, string name, string source) => value.TryGetProperty(name, out var property) ? property : throw new WorkloadContractException($"{source} is missing required property '{name}'.");
    private static string RequireString(JsonElement value, string name, string source)
    {
        var property = RequireProperty(value, name, source);
        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
            throw new WorkloadContractException($"{source}.{name} must be a non-empty string.");
        return property.GetString()!;
    }
    private static int RequireInt(JsonElement value, string name, string source) => RequireProperty(value, name, source).ValueKind == JsonValueKind.Number && RequireProperty(value, name, source).TryGetInt32(out var number) ? number : throw new WorkloadContractException($"{source}.{name} must be an integer.");
    private static string RequireSha(JsonElement value, string name, string source)
    {
        var hash = RequireString(value, name, source);
        if (!Sha256.IsMatch(hash)) throw new WorkloadContractException($"{source}.{name} must be a lowercase SHA-256 hash.");
        return hash;
    }
    private static JsonElement RequireArray(JsonElement value, string name, string source)
    {
        var property = RequireProperty(value, name, source);
        if (property.ValueKind != JsonValueKind.Array) throw new WorkloadContractException($"{source}.{name} must be an array.");
        return property;
    }
    private static IReadOnlyList<string> RequireStrings(JsonElement value, string source, string name, bool allowEmpty)
    {
        var values = value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : null).ToArray();
        if ((!allowEmpty && values.Length == 0) || values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new WorkloadContractException($"{source}.{name} must contain unique non-empty strings.");
        return values!;
    }
    private static IReadOnlyList<string> RequireSlugs(JsonElement value, string source, string name, bool allowEmpty)
    {
        var values = RequireStrings(value, source, name, allowEmpty);
        if (values.Any(item => !Slug.IsMatch(item))) throw new WorkloadContractException($"{source}.{name} must contain slugs.");
        return values;
    }
    private static void RequireObject(JsonElement value, string source) { if (value.ValueKind != JsonValueKind.Object) throw new WorkloadContractException($"{source} must be an object."); }
    private static void RequireClosedProperties(JsonElement value, IEnumerable<string> names, string source)
    {
        var allowed = names.ToHashSet(StringComparer.Ordinal);
        var properties = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (properties.Any(property => !allowed.Contains(property))) throw new WorkloadContractException($"{source} contains an unknown contract property.");
        if (properties.Distinct(StringComparer.Ordinal).Count() != properties.Length) throw new WorkloadContractException($"{source} contains a duplicate contract property.");
        if (!allowed.SetEquals(properties)) throw new WorkloadContractException($"{source} is missing one or more required contract properties.");
    }

    private sealed record ExpectedWorkload(string[] CoverageRows, string[] PhysicalForms);
    private static readonly IReadOnlyDictionary<string, string> ExpectedSourceDigests = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["runtime.json"] = "1b81a63d8a2acfe5ceea9e9a7e458de21c0fae8069506be5e94258198eff7d41",
        ["iam-secrets.json"] = "b5681de1cb1cf5fa9e671770df0cc78f026103293889d86d0c9ea63fcc4ee364",
        ["distributed-runtime.json"] = "e03a5db9ddbdbfe4c854632fadc00b2674546d0925e65b0af198ada75910d837",
        ["diagnostics.json"] = "16ba05d98250fa5917baf40116b454a1106a3247cde2a94444a97cd49b53f8ad"
    };
    private static readonly IReadOnlyDictionary<string, ExpectedWorkload> Expected = new Dictionary<string, ExpectedWorkload>(StringComparer.Ordinal)
    {
        ["checkpoint-commit"] = new(["runtime-activity-execution-state", "runtime-checkpoint-commit", "runtime-durable-value-state", "runtime-workflow-executable"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "checkpoint-unit-of-work-with-linked-outbox"]),
        ["bookmark-lookup"] = new(["runtime-bookmark-state"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables"]),
        ["trigger-binding-stimulus-lookup"] = new(["runtime-executable-source-reference", "runtime-trigger-binding"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "linked-executable-source-reference-index"]),
        ["recovery-scan"] = new(["runtime-execution-liveness", "runtime-incident-state", "runtime-scheduler-state", "runtime-workflow-execution-state", "runtime-workflow-hold-state"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "recovery-candidate-index"]),
        ["queue-drain"] = new(["runtime-scheduler-poison", "runtime-scheduler-work-queue"], ["dedicated-scheduler-work-documents", "dedicated-scheduler-poison-documents", "shared-documents-with-linked-index-tables"]),
        ["outbox-drain"] = new(["runtime-post-commit-outbox"], ["dedicated-post-commit-outbox-documents", "shared-documents-with-linked-index-tables", "due-order-index"]),
        ["due-timer-selection"] = new(["runtime-durable-timer"], ["dedicated-durable-timer-documents", "shared-documents-with-linked-index-tables", "due-order-index"]),
        ["recurring-schedule-selection"] = new(["runtime-publication-projection-state", "runtime-recurring-trigger-schedule"], ["dedicated-recurring-schedule-documents", "publication-projection-documents", "shared-documents-with-linked-index-tables"]),
        ["iam-normalized-lookup-update"] = new(["iam-application", "iam-claim-mapping", "iam-credential", "iam-external-identity", "iam-provider-configuration-tenant", "iam-role", "iam-tenant-membership", "iam-user"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "entity-type-specific-physical-tables-current-identity-shape"]),
        ["secret-create-read-list"] = new(["secrets-repository"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "entity-type-specific-physical-tables"]),
        ["placement-takeover"] = new(["distributed-execution-placement"], ["dedicated-placement-lease-documents", "shared-documents-with-linked-index-tables", "placement-owner-expiry-index"]),
        ["command-send-lease-ack"] = new(["distributed-command-transport"], ["dedicated-command-transport-documents", "stream-head-documents", "shared-documents-with-linked-index-tables", "visibility-order-index"]),
        ["diagnostics-durable-history"] = new(
            ["diagnostics-open-telemetry-store", "diagnostics-structured-log-store"],
            ["specialized-diagnostic-record-streams-with-shared-document-catalogs", "specialized-diagnostic-record-streams-with-dedicated-document-catalogs"])
    };
}

public sealed record PerformanceWorkload(string Id, string Version, string ScenarioId, string Owner, string PublicOperation, IReadOnlyList<string> CoverageRows, WorkloadInput Input, IReadOnlyList<string> OperationSequence, IReadOnlyList<string> RequiredProviders, IReadOnlyList<string> RequiredNativeRoutes, IReadOnlyDictionary<string, string> RequiredProviderEvidence, CorrectnessContract Correctness, EfContractBaseline EfContractBaseline, BenchmarkAdmission BenchmarkAdmission, IReadOnlyList<string> PhysicalFormsFor646, IReadOnlyList<string> ArtifactRetention);
public sealed record WorkloadInput(string Seed, string FingerprintSha256, JsonElement Values);
public sealed record CorrectnessContract(string ResultDigestSha256, IReadOnlyList<string> Invariants, string TimingGate);
public sealed record EfContractBaseline(string BaselineIdentity, string ExecutionStatus, string ExecutionOwner, string Purpose);
public sealed record BenchmarkAdmission(string Status, string Reason)
{
    public bool IsReady => Status == "ready";
}
public sealed class WorkloadContractException(string message) : Exception(message);
