using System.Text.Json.Nodes;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using System.Text.RegularExpressions;
using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

public sealed class PerformanceWorkloadCorrectnessTests
{
    private static readonly string[] RequiredProviders = ["sqlite", "sqlserver", "postgresql", "mongodb"];

    private static readonly IReadOnlyDictionary<string, (string InputFingerprint, string ResultDigest)> ExpectedDigests =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["bookmark-lookup"] = ("d006e25e22dc8d9374d8931f03e27c6dc45c27314bfe2f819a4dd61b588062e8", "e723ae42c3fd4e970cff04d4a6e867fa40b8d6ea23b0305ab82bf80d3916d6a9"),
            ["checkpoint-commit"] = ("ee4cef346ca64739bbe7cfc84ee3f74e6acefec582f537c685991ca73c62ce13", "ebb92b59a7a331e863c813f7110272093be6a78794a9cc7a0d914103ab4c9c62"),
            ["command-send-lease-ack"] = ("a108e41c890af94ee37d610817e2c4d6339451cbfbbd0e33e0bd794d0d1af5b1", "86439fbc13d29102d02615ee98a5beb53e008e673f6523681e3ee2d926d3389f"),
            ["diagnostics-durable-history"] = ("448b4f1251861cc5629a6aed316a5ed2112ed14309da5b500838ad43f9513667", "d27a2436f75cf5bb44054e5e284631d4a00656223b5f2ba5ff0573e1fde4e7f7"),
            ["due-timer-selection"] = ("02cfb91f4f415fcfe8fe6cd64e7c056b88b908e068735d2ec91eb81e0ec8d5bd", "8f380d449eb3a8e88f1edbea73cf9a7ddfa7a7502cab3ac5a8fcfe3e175ffed3"),
            ["iam-normalized-lookup-update"] = ("5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9", "32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc"),
            ["outbox-drain"] = ("bc5c6ca1113e78fe948a61de35c66a644129c79028a198d9143dc316cea7bede", "7228f024095bc2fadc0649e0841d56259f3408b55368911ea402b7d96c8b2e71"),
            ["placement-takeover"] = ("17f22a7e7896b3842ebd771e604b13e859d1b480bc5b6093ce576f14a673e985", "3ad65cc7ff9287f9c20a68ec6cd267bc78fa083fb775dda36062c185706fb4b4"),
            ["queue-drain"] = ("15f2d5f9dc8d5814a1613156b7c686e59a150a35bd7e51787a145b6d7230d5e2", "7db639fdbfddc02973a7275d7c0e8835872b62449ca160e97e8086c0ca46eba4"),
            ["recovery-scan"] = ("36277c9b9c525d4cbb611c1a7e83c96a02eb3434fb85b6657ce2ede9b8a7a5e3", "3c7cae42737a2a995968852a862f769070a016b4e4a0289c7a9a5e7205e9eabf"),
            ["recurring-schedule-selection"] = ("384bcbf0fd72f306b63d78b71a8130c4e2e02de146cbd45d066ef581f4d78d17", "9728bad4f576c7e50c3f6210994524ffb1d77761c5258a71f27fe1cf1793cec4"),
            [ReproducibleWorkloadScenarioCatalog.BlockedWorkloadId] = ("339a6adc9ba6c34e85ce43eafd3e0b8b7b74f7ccbb7d52bd34efe1fbe394014c", "615f7bbd8e160dd34d38180d5def0e99d0b4225822e6ebee5ea31ed21bbabcdb"),
            ["trigger-binding-stimulus-lookup"] = ("4f2515dfa9549935712019f178283f79e6ac1cc9428e810524e733cfdea4cabc", "00b6651345cdb8b6724a205b094c712d383c7a19ef87dcce6fdf026bc7dd7c8a")
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

        Assert.Equal(35, actual.SelectMany(pair => pair.Value).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Workload_inputs_results_and_provider_prerequisites_are_deterministic_and_complete()
    {
        var workloads = LoadWorkloads();

        Assert.Equal(
            ExpectedDigests.Keys.Order(StringComparer.Ordinal),
            workloads.Keys.Order(StringComparer.Ordinal));
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
            Assert.Equal(
                ReproducibleWorkloadScenarioCatalog.TryGetBlockedReason(id, out _) ? "blocked" : "ready",
                workload["benchmarkAdmission"]!["status"]!.GetValue<string>());
        }

        foreach (var (id, scenario) in ReproducibleWorkloadScenarioCatalog.Successors)
        {
            Assert.Equal(ExpectedDigests[id].InputFingerprint, scenario.ComputeInputFingerprint());
            Assert.Equal(ExpectedDigests[id].ResultDigest, scenario.ComputeResultDigest());
        }

        var iam = workloads["iam-normalized-lookup-update"];
        Assert.Equal(AspNetCoreIdentityPerformanceWorkload.ExpectedInputFingerprint, iam["input"]!["fingerprintSha256"]!.GetValue<string>());
        Assert.Equal(AspNetCoreIdentityPerformanceWorkload.ExpectedResultDigest, iam["correctness"]!["resultDigestSha256"]!.GetValue<string>());
        Assert.Equal(AspNetCoreIdentityPerformanceWorkload.ExpectedInputFingerprint, AspNetCoreIdentityPerformanceWorkload.ComputeInputFingerprint());

        var secret = workloads[ReproducibleWorkloadScenarioCatalog.BlockedWorkloadId];
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.BlockedVersion, secret["version"]!.GetValue<string>());
        Assert.Equal(
            ReproducibleWorkloadScenarioCatalog.BlockedInputFingerprint,
            secret["input"]!["fingerprintSha256"]!.GetValue<string>());
        Assert.Equal(
            ReproducibleWorkloadScenarioCatalog.BlockedResultDigest,
            secret["correctness"]!["resultDigestSha256"]!.GetValue<string>());
        Assert.Equal(
            ReproducibleWorkloadScenarioCatalog.BlockedReasonCode,
            secret["benchmarkAdmission"]!["reason"]!.GetValue<string>());
        Assert.Contains("real EF Secret repository comparator", ReproducibleWorkloadScenarioCatalog.BlockedReason, StringComparison.Ordinal);

        var diagnostics = workloads[ReproducibleWorkloadScenarioCatalog.DiagnosticsWorkloadId];
        Assert.Equal(
            ReproducibleWorkloadScenarioCatalog.DiagnosticsBlockedReasonCode,
            diagnostics["benchmarkAdmission"]!["reason"]!.GetValue<string>());
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
        Path.Combine(WorkloadsDirectory, "distributed-runtime.json"),
        Path.Combine(WorkloadsDirectory, "diagnostics.json")
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
