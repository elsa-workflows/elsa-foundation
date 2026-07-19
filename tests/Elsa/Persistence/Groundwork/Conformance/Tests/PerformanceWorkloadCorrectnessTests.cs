using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class PerformanceWorkloadCorrectnessTests
{
    private static readonly string[] RequiredProviders = ["sqlite", "sqlserver", "postgresql", "mongodb"];

    private static readonly IReadOnlyDictionary<string, (string InputFingerprint, string ResultDigest)> ExpectedDigests =
        new Dictionary<string, (string InputFingerprint, string ResultDigest)>(StringComparer.Ordinal)
        {
            ["bookmark-lookup"] = ("c1b8a142e22e7c47449edc25c79cc2a83c5edb6dbbe4a884a730751038f3ae9a", "9f3d29edc4c3e64409f3fb9b64b4ec3e7d5e5064d8233be8afd92215ec3d680e"),
            ["checkpoint-commit"] = ("f59eef8b9359dc3623bbb42ce07c531f0f027170dc6d33e1788b1bd80dcdab93", "abaa23e9e4f3c9285f50a07f33d7569696ec4cfe1ac496575c12b45dbe78042a"),
            ["command-send-lease-ack"] = ("cb50baabaf83d0826dbb19d259be9d8fca9b4c8eaa9aea6ba7354a54c1835493", "9f8fa582159e0a796ecbe2d7bfb655cbebee428f9490fa83d4228e8e64f924eb"),
            ["due-timer-selection"] = ("86bef68c844d10b3cb02c8f65da33ba46ec4c27b9ba0090b9783cd5036f1ab0e", "002fe0f7e4808d7ec2b85f267f8188981c3fd8ed4beca7ad11100ddd6c8d2002"),
            ["iam-normalized-lookup-update"] = ("5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9", "32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc"),
            ["outbox-drain"] = ("3a6f44fea2a5905e3df316bd3585b13f6080588c75c6eab9598cced26c184eef", "0f4f678c6250ce1c951ad1e14218a1c38c61d6b15b947fa724c50859a4339934"),
            ["placement-takeover"] = ("9599391db271c63a41cced1409754b0bcb4e2bd8c316b70efa4d1583c310c92b", "25885819c5cb186adab6196a46d8e369e7b26992e0733e9525a3ca1eb2bf07c1"),
            ["queue-drain"] = ("21d5dabec0cb604dae214af9bc20835b09ab5f87cfd3d697b946d8adb31d20fa", "bf82d193b202ff0c9e3be211009a3f10401cf1411aaac607db64a199657fb630"),
            ["recurring-schedule-selection"] = ("ab6e1c276995da9e564f55cee00f243a13e16334c58c0d19cc146f2f757b3b5e", "af1d9aecbc7604ce39e33c553d9c1014be2377d81300aa4905e700beffcf7b17"),
            ["recovery-scan"] = ("7284c110669aaa3db7587893e9e31005af8c807d8323609aeb80cfd948d82b48", "06033b0de6f4784abc87772b63f5f9a561a5bfc40bc18b3f429b4e318fccd785"),
            ["secret-create-read-list"] = ("339a6adc9ba6c34e85ce43eafd3e0b8b7b74f7ccbb7d52bd34efe1fbe394014c", "615f7bbd8e160dd34d38180d5def0e99d0b4225822e6ebee5ea31ed21bbabcdb"),
            ["trigger-binding-stimulus-lookup"] = ("cbd570ed8c80f996554853b1143fc34f634138b005858322d1a669dde2113b9a", "3c10eab69da70eccacc648780781ef57ad6499b91cb012465d154d6b1b7e9294")
        };

    [Fact]
    public void Workload_documents_conform_to_the_shared_json_schema()
    {
        var schema = ReadJson(WorkloadSchemaPath);

        Assert.Equal(
            WorkloadPaths.Select(Path.GetFileName).Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(WorkloadsDirectory, "*.json").Select(Path.GetFileName).Order(StringComparer.Ordinal));

        foreach (var path in WorkloadPaths)
        {
            var findings = new PerformanceWorkloadSchemaValidator(schema).Validate(ReadJson(path));
            Assert.True(findings.Count == 0, $"{Path.GetFileName(path)} schema findings:{Environment.NewLine}{string.Join(Environment.NewLine, findings)}");
        }
    }

    [Fact]
    public void Workload_documents_exactly_cover_the_ledger_lanes_that_require_workloads()
    {
        var workloads = LoadWorkloads();
        var actual = ReadJson(LedgerPath)["entries"]!.AsArray()
            .Select(entry => entry!.AsObject())
            .GroupBy(entry => entry["performanceWorkload"]!.GetValue<string>(), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry["id"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        Assert.Equal(
            actual.Keys.Where(workload => !ReviewedNonWorkloadLanes.Contains(workload, StringComparer.Ordinal)).Order(StringComparer.Ordinal),
            workloads.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            ["diagnostic-settings-owned-by-660-and-646", "not-hot-path"],
            actual.Keys.Where(workload => ReviewedNonWorkloadLanes.Contains(workload, StringComparer.Ordinal)).Order(StringComparer.Ordinal));

        foreach (var (id, workload) in workloads)
        {
            var coveredRows = workload["coverageRows"]!.AsArray()
                .Select(row => row!.GetValue<string>())
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(actual[id], coveredRows);
        }

        Assert.Equal(32, actual.SelectMany(pair => pair.Value).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Workload_inputs_results_and_provider_prerequisites_are_deterministic_and_complete()
    {
        var workloads = LoadWorkloads();

        Assert.Equal(ExpectedDigests.Keys.Order(StringComparer.Ordinal), workloads.Keys.Order(StringComparer.Ordinal));
        foreach (var (id, workload) in workloads)
        {
            var expected = ExpectedDigests[id];
            Assert.Equal(expected.InputFingerprint, workload["input"]!["fingerprintSha256"]!.GetValue<string>());
            Assert.Equal(expected.ResultDigest, workload["correctness"]!["resultDigestSha256"]!.GetValue<string>());
            Assert.Equal(RequiredProviders, workload["requiredProviders"]!.AsArray().Select(provider => provider!.GetValue<string>()));
            Assert.Equal(
                RequiredProviders.Order(StringComparer.Ordinal),
                workload["requiredProviderEvidence"]!.AsObject().Select(prerequisite => prerequisite.Key).Order(StringComparer.Ordinal));
            Assert.All(
                workload["requiredProviderEvidence"]!.AsObject(),
                prerequisite => Assert.False(string.IsNullOrWhiteSpace(prerequisite.Value!.GetValue<string>())));
        }

        var iam = workloads["iam-normalized-lookup-update"];
        Assert.Equal(AspNetCoreIdentityPerformanceWorkload.ExpectedInputFingerprint, iam["input"]!["fingerprintSha256"]!.GetValue<string>());
        Assert.Equal(AspNetCoreIdentityPerformanceWorkload.ExpectedResultDigest, iam["correctness"]!["resultDigestSha256"]!.GetValue<string>());
        Assert.Equal(AspNetCoreIdentityPerformanceWorkload.ExpectedInputFingerprint, AspNetCoreIdentityPerformanceWorkload.ComputeInputFingerprint());
    }

    [Fact]
    [Trait("Category", "Sqlite")]
    public async Task Sqlite_runs_the_existing_public_identity_acceptance_contract()
    {
        await using var driver = new SqliteGroundworkProviderDriver();
        var result = await new AspNetCoreIdentityProviderAcceptanceRunner(driver).RunAsync();

        Assert.Equal(
            AspNetCoreIdentityProviderAcceptanceCatalog.RequiredObjectiveIds.Order(StringComparer.Ordinal),
            result.CompletedObjectiveIds.Order(StringComparer.Ordinal));
        Assert.Equal(
            AspNetCoreIdentityProviderAcceptanceCatalog.ComputeObjectiveResultDigest(result.CompletedObjectiveIds),
            result.ObjectiveResultDigest);
    }

    [Fact]
    [Trait("Category", "Sqlite")]
    public Task Sqlite_runs_the_public_secret_repository_bounded_query_contract() =>
        new FoundationBoundedQueryContractTests()
            .Secret_filters_order_count_and_window_execute_before_materialization("sqlite");

    private static IReadOnlyDictionary<string, JsonObject> LoadWorkloads() => WorkloadPaths
        .SelectMany(path => ReadJson(path)["workloads"]!.AsArray().Select(workload => workload!.AsObject()))
        .ToDictionary(workload => workload["id"]!.GetValue<string>(), StringComparer.Ordinal);

    private static JsonObject ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static string[] WorkloadPaths =>
    [
        Path.Combine(WorkloadsDirectory, "runtime.json"),
        Path.Combine(WorkloadsDirectory, "iam-secrets.json"),
        Path.Combine(WorkloadsDirectory, "distributed-runtime.json")
    ];

    private static string WorkloadsDirectory => Path.Combine(RepoRoot, "specs", "094-harden-groundwork-stores", "workloads");

    private static string WorkloadSchemaPath => Path.Combine(RepoRoot, "specs", "094-harden-groundwork-stores", "contracts", "performance-workload.schema.json");

    private static string LedgerPath => Path.Combine(RepoRoot, "specs", "094-harden-groundwork-stores", "coverage-ledger.json");

    private static string[] ReviewedNonWorkloadLanes => ["diagnostic-settings-owned-by-660-and-646", "not-hot-path"];

    private static string RepoRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;
            }

            throw new InvalidOperationException("Could not locate the Elsa Foundation repository root.");
        }
    }
}

internal sealed class PerformanceWorkloadSchemaValidator(JsonObject schema)
{
    private readonly JsonObject rootSchema = schema;

    public IReadOnlyList<string> Validate(JsonNode instance)
    {
        var findings = new List<string>();
        Validate(rootSchema, instance, "$", findings);
        return findings;
    }

    private void Validate(JsonObject schema, JsonNode? instance, string path, ICollection<string> findings)
    {
        if (schema["$ref"]?.GetValue<string>() is { } reference)
        {
            Validate(Resolve(reference), instance, path, findings);
            return;
        }

        if (schema["const"] is { } constant && !string.Equals(instance?.ToJsonString(), constant.ToJsonString(), StringComparison.Ordinal))
            findings.Add($"{path}: expected constant {constant.ToJsonString()}.");

        var type = schema["type"]?.GetValue<string>();
        if (type is not null && !HasType(instance, type))
        {
            findings.Add($"{path}: expected {type}.");
            return;
        }

        if (instance is JsonObject valueObject)
            ValidateObject(schema, valueObject, path, findings);
        else if (instance is JsonArray valueArray)
            ValidateArray(schema, valueArray, path, findings);
        else if (instance is JsonValue value)
            ValidateValue(schema, value, path, findings);
    }

    private void ValidateObject(JsonObject schema, JsonObject instance, string path, ICollection<string> findings)
    {
        var properties = schema["properties"] as JsonObject;
        if (schema["required"] is JsonArray required)
        {
            foreach (var property in required.Select(value => value!.GetValue<string>()))
            {
                if (!instance.ContainsKey(property))
                    findings.Add($"{path}: missing required property '{property}'.");
            }
        }

        if (schema["additionalProperties"]?.GetValue<bool>() is false && properties is not null)
        {
            foreach (var property in instance.Select(pair => pair.Key).Where(property => !properties.ContainsKey(property)))
                findings.Add($"{path}: unexpected property '{property}'.");
        }

        if (properties is null)
            return;

        foreach (var (property, childSchema) in properties)
        {
            if (instance[property] is { } child)
                Validate(childSchema!.AsObject(), child, $"{path}.{property}", findings);
        }
    }

    private void ValidateArray(JsonObject schema, JsonArray instance, string path, ICollection<string> findings)
    {
        if (schema["minItems"]?.GetValue<int>() is { } minItems && instance.Count < minItems)
            findings.Add($"{path}: expected at least {minItems} item(s).");
        if (schema["maxItems"]?.GetValue<int>() is { } maxItems && instance.Count > maxItems)
            findings.Add($"{path}: expected at most {maxItems} item(s).");
        if (schema["uniqueItems"]?.GetValue<bool>() is true && instance.Where(value => value is not null).Select(value => value!.ToJsonString()).Distinct(StringComparer.Ordinal).Count() != instance.Count)
            findings.Add($"{path}: items must be unique.");

        var prefixItems = schema["prefixItems"] as JsonArray;
        if (prefixItems is not null)
        {
            foreach (var (item, index) in instance.Take(prefixItems.Count).Select((item, index) => (item, index)))
                Validate(prefixItems[index]!.AsObject(), item, $"{path}[{index}]", findings);
            if (schema["items"] is JsonValue itemsControl &&
                itemsControl.TryGetValue<bool>(out var allowsItemsAfterPrefix) &&
                !allowsItemsAfterPrefix &&
                instance.Count > prefixItems.Count)
                findings.Add($"{path}: does not allow items after prefixItems.");
        }

        if (schema["items"] is JsonObject itemSchema)
        {
            foreach (var (item, index) in instance.Select((item, index) => (item, index)))
                Validate(itemSchema, item, $"{path}[{index}]", findings);
        }
    }

    private static void ValidateValue(JsonObject schema, JsonValue instance, string path, ICollection<string> findings)
    {
        if (!instance.TryGetValue<string>(out var stringValue))
            return;

        if (schema["minLength"]?.GetValue<int>() is { } minLength && stringValue.Length < minLength)
            findings.Add($"{path}: expected a string with at least {minLength} character(s).");
        if (schema["pattern"]?.GetValue<string>() is { } pattern && !Regex.IsMatch(stringValue, pattern, RegexOptions.CultureInvariant))
            findings.Add($"{path}: does not match '{pattern}'.");
    }

    private JsonObject Resolve(string reference)
    {
        const string definitionsPrefix = "#/$defs/";
        if (!reference.StartsWith(definitionsPrefix, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported JSON schema reference '{reference}'.");

        return rootSchema["$defs"]![reference[definitionsPrefix.Length..]]!.AsObject();
    }

    private static bool HasType(JsonNode? value, string type) => type switch
    {
        "object" => value is JsonObject,
        "array" => value is JsonArray,
        "string" => value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out _),
        "integer" => value is JsonValue integerValue && integerValue.TryGetValue<int>(out _),
        _ => throw new InvalidOperationException($"Unsupported JSON schema type '{type}'.")
    };
}
