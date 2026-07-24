using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;

/// <summary>
/// The closed #646 input boundary. The values below deliberately duplicate the versioned #645 handoff:
/// a changed workload is a new reviewed handoff, not an input the harness may silently reinterpret.
/// </summary>
public sealed class WorkloadCatalog
{
    private static readonly string[] RequiredFiles = ["runtime.json", "iam-secrets.json", "distributed-runtime.json"];
    private static readonly string[] Providers = ["sqlite", "sqlserver", "postgresql", "mongodb"];
    private static readonly Regex Slug = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Sha256 = new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SemVer = new("^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private WorkloadCatalog(IReadOnlyDictionary<string, PerformanceWorkload> workloads) => Workloads = workloads;

    public IReadOnlyDictionary<string, PerformanceWorkload> Workloads { get; }

    public static WorkloadCatalog Load(string repositoryRoot)
    {
        var workloadDirectory = Path.Combine(repositoryRoot, "specs", "094-harden-groundwork-stores", "workloads");
        if (!Directory.Exists(workloadDirectory))
            throw new WorkloadContractException($"The required Spec 094 workload directory was not found: {workloadDirectory}");

        var discovered = Directory.EnumerateFiles(workloadDirectory, "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        if (!discovered.SequenceEqual(RequiredFiles.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new WorkloadContractException("The workload directory must contain exactly runtime.json, iam-secrets.json, and distributed-runtime.json.");

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
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(), ExpectedSourceDigests[file], StringComparison.Ordinal))
                throw new WorkloadContractException($"{file} does not match the frozen Spec 094 #646 source contract.");
        }

        if (workloads.Count != Expected.Count || !workloads.Keys.Order(StringComparer.Ordinal).SequenceEqual(Expected.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new WorkloadContractException("The #646 catalog must contain exactly the twelve frozen Spec 094 workload definitions.");
        foreach (var (id, expected) in Expected)
            ValidateFrozenContract(workloads[id], expected);
        return new WorkloadCatalog(workloads);
    }

    private static PerformanceWorkload ParseWorkload(JsonElement value, string source)
    {
        RequireObject(value, source);
        RequireClosedProperties(value,
        ["id", "version", "scenarioId", "owner", "handoffTarget", "publicOperation", "coverageRows", "input", "operationSequence", "requiredProviders", "requiredNativeRoutes", "requiredProviderEvidence", "correctness", "efContractBaseline", "physicalFormsFor646", "artifactRetention"], source);

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
        var physicalForms = RequireStrings(RequireArray(value, "physicalFormsFor646", source), source, "physicalFormsFor646", allowEmpty: false);
        var retention = RequireStrings(RequireArray(value, "artifactRetention", source), source, "artifactRetention", allowEmpty: false);

        return new PerformanceWorkload(id, version, RequireString(value, "scenarioId", source), RequireString(value, "owner", source), RequireString(value, "publicOperation", source), coverageRows, input, operationSequence, requiredProviders, routes, providerEvidence, correctness, baseline, physicalForms, retention);
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

    private static void ValidateFrozenContract(PerformanceWorkload actual, ExpectedWorkload expected)
    {
        if (actual.Version != expected.Version || actual.Input.FingerprintSha256 != expected.InputFingerprint || actual.Correctness.ResultDigestSha256 != expected.ResultDigest ||
            !actual.CoverageRows.SequenceEqual(expected.CoverageRows, StringComparer.Ordinal) || !actual.PhysicalFormsFor646.SequenceEqual(expected.PhysicalForms, StringComparer.Ordinal))
            throw new WorkloadContractException($"The workload '{actual.Id}' does not match its frozen Spec 094 #646 handoff contract.");
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

    private sealed record ExpectedWorkload(string Version, string InputFingerprint, string ResultDigest, string[] CoverageRows, string[] PhysicalForms);
    private static readonly IReadOnlyDictionary<string, string> ExpectedSourceDigests = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["runtime.json"] = "ec28c995f70a98a1f57b34e3c3b9b657ce090c5bd0dc4c81b10f5dd7476d6a82",
        ["iam-secrets.json"] = "f62c83caca2bfbf2f0989b835d39f8a4a80860ad4b8342d9d051aade4b6c12a6",
        ["distributed-runtime.json"] = "b82f0c370859c6ba06eca13bad125d7c31f4a2f74b4f8b6d9f5f60e21217e01a"
    };
    private static readonly IReadOnlyDictionary<string, ExpectedWorkload> Expected = new Dictionary<string, ExpectedWorkload>(StringComparer.Ordinal)
    {
        ["checkpoint-commit"] = new("1.0.0", "f59eef8b9359dc3623bbb42ce07c531f0f027170dc6d33e1788b1bd80dcdab93", "abaa23e9e4f3c9285f50a07f33d7569696ec4cfe1ac496575c12b45dbe78042a", ["runtime-activity-execution-state", "runtime-checkpoint-commit", "runtime-durable-value-state", "runtime-workflow-executable"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "checkpoint-unit-of-work-with-linked-outbox"]),
        ["bookmark-lookup"] = new("1.0.0", "c1b8a142e22e7c47449edc25c79cc2a83c5edb6dbbe4a884a730751038f3ae9a", "9f3d29edc4c3e64409f3fb9b64b4ec3e7d5e5064d8233be8afd92215ec3d680e", ["runtime-bookmark-state"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables"]),
        ["trigger-binding-stimulus-lookup"] = new("1.0.0", "cbd570ed8c80f996554853b1143fc34f634138b005858322d1a669dde2113b9a", "3c10eab69da70eccacc648780781ef57ad6499b91cb012465d154d6b1b7e9294", ["runtime-executable-source-reference", "runtime-trigger-binding"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "linked-executable-source-reference-index"]),
        ["recovery-scan"] = new("1.0.0", "7284c110669aaa3db7587893e9e31005af8c807d8323609aeb80cfd948d82b48", "06033b0de6f4784abc87772b63f5f9a561a5bfc40bc18b3f429b4e318fccd785", ["runtime-execution-liveness", "runtime-incident-state", "runtime-scheduler-state", "runtime-workflow-execution-state", "runtime-workflow-hold-state"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "recovery-candidate-index"]),
        ["queue-drain"] = new("1.0.0", "21d5dabec0cb604dae214af9bc20835b09ab5f87cfd3d697b946d8adb31d20fa", "bf82d193b202ff0c9e3be211009a3f10401cf1411aaac607db64a199657fb630", ["runtime-scheduler-poison", "runtime-scheduler-work-queue"], ["dedicated-scheduler-work-documents", "dedicated-scheduler-poison-documents", "shared-documents-with-linked-index-tables"]),
        ["outbox-drain"] = new("1.0.0", "3a6f44fea2a5905e3df316bd3585b13f6080588c75c6eab9598cced26c184eef", "0f4f678c6250ce1c951ad1e14218a1c38c61d6b15b947fa724c50859a4339934", ["runtime-post-commit-outbox"], ["dedicated-post-commit-outbox-documents", "shared-documents-with-linked-index-tables", "due-order-index"]),
        ["due-timer-selection"] = new("1.0.0", "86bef68c844d10b3cb02c8f65da33ba46ec4c27b9ba0090b9783cd5036f1ab0e", "002fe0f7e4808d7ec2b85f267f8188981c3fd8ed4beca7ad11100ddd6c8d2002", ["runtime-durable-timer"], ["dedicated-durable-timer-documents", "shared-documents-with-linked-index-tables", "due-order-index"]),
        ["recurring-schedule-selection"] = new("1.0.0", "ab6e1c276995da9e564f55cee00f243a13e16334c58c0d19cc146f2f757b3b5e", "af1d9aecbc7604ce39e33c553d9c1014be2377d81300aa4905e700beffcf7b17", ["runtime-publication-projection-state", "runtime-recurring-trigger-schedule"], ["dedicated-recurring-schedule-documents", "publication-projection-documents", "shared-documents-with-linked-index-tables"]),
        ["iam-normalized-lookup-update"] = new("1.1.0", "5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9", "32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc", ["iam-application", "iam-claim-mapping", "iam-credential", "iam-external-identity", "iam-provider-configuration-tenant", "iam-role", "iam-tenant-membership", "iam-user"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "entity-type-specific-physical-tables-current-identity-shape"]),
        ["secret-create-read-list"] = new("1.0.0", "339a6adc9ba6c34e85ce43eafd3e0b8b7b74f7ccbb7d52bd34efe1fbe394014c", "615f7bbd8e160dd34d38180d5def0e99d0b4225822e6ebee5ea31ed21bbabcdb", ["secrets-repository"], ["shared-documents-with-linked-index-tables", "document-type-specific-tables", "entity-type-specific-physical-tables"]),
        ["placement-takeover"] = new("1.0.0", "9599391db271c63a41cced1409754b0bcb4e2bd8c316b70efa4d1583c310c92b", "25885819c5cb186adab6196a46d8e369e7b26992e0733e9525a3ca1eb2bf07c1", ["distributed-execution-placement"], ["dedicated-placement-lease-documents", "shared-documents-with-linked-index-tables", "placement-owner-expiry-index"]),
        ["command-send-lease-ack"] = new("1.0.0", "cb50baabaf83d0826dbb19d259be9d8fca9b4c8eaa9aea6ba7354a54c1835493", "9f8fa582159e0a796ecbe2d7bfb655cbebee428f9490fa83d4228e8e64f924eb", ["distributed-command-transport"], ["dedicated-command-transport-documents", "stream-head-documents", "shared-documents-with-linked-index-tables", "visibility-order-index"])
    };
}

public sealed record PerformanceWorkload(string Id, string Version, string ScenarioId, string Owner, string PublicOperation, IReadOnlyList<string> CoverageRows, WorkloadInput Input, IReadOnlyList<string> OperationSequence, IReadOnlyList<string> RequiredProviders, IReadOnlyList<string> RequiredNativeRoutes, IReadOnlyDictionary<string, string> RequiredProviderEvidence, CorrectnessContract Correctness, EfContractBaseline EfContractBaseline, IReadOnlyList<string> PhysicalFormsFor646, IReadOnlyList<string> ArtifactRetention);
public sealed record WorkloadInput(string Seed, string FingerprintSha256, JsonElement Values);
public sealed record CorrectnessContract(string ResultDigestSha256, IReadOnlyList<string> Invariants, string TimingGate);
public sealed record EfContractBaseline(string BaselineIdentity, string ExecutionStatus, string ExecutionOwner, string Purpose);
public sealed class WorkloadContractException(string message) : Exception(message);
