using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class GroundworkPerformanceHandoffTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedLedgerMapping =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["bookmark-lookup"] = ["runtime-bookmark-state"],
            ["checkpoint-commit"] =
            [
                "runtime-activity-execution-state",
                "runtime-checkpoint-commit",
                "runtime-durable-value-state",
                "runtime-workflow-executable"
            ],
            ["command-send-lease-ack"] = ["distributed-command-transport"],
            ["diagnostics-durable-history"] =
            ["diagnostics-open-telemetry-store", "diagnostics-structured-log-store"],
            ["diagnostic-settings-owned-by-660-and-646"] = ["runtime-diagnostics-settings"],
            ["due-timer-selection"] = ["runtime-durable-timer"],
            ["iam-normalized-lookup-update"] =
            [
                "iam-application",
                "iam-claim-mapping",
                "iam-credential",
                "iam-external-identity",
                "iam-provider-configuration-tenant",
                "iam-role",
                "iam-tenant-membership",
                "iam-user"
            ],
            ["not-hot-path"] =
            [
                "iam-provider-configuration-global",
                "runtime-activity-execution-inspection",
                "runtime-workflow-alteration"
            ],
            ["outbox-drain"] = ["runtime-post-commit-outbox"],
            ["placement-takeover"] = ["distributed-execution-placement"],
            ["queue-drain"] = ["runtime-scheduler-poison", "runtime-scheduler-work-queue"],
            ["recovery-scan"] =
            [
                "runtime-execution-liveness",
                "runtime-incident-state",
                "runtime-scheduler-state",
                "runtime-workflow-execution-state",
                "runtime-workflow-hold-state"
            ],
            ["recurring-schedule-selection"] =
            ["runtime-publication-projection-state", "runtime-recurring-trigger-schedule"],
            ["secret-create-read-list"] = ["secrets-repository"],
            ["trigger-binding-stimulus-lookup"] =
            ["runtime-executable-source-reference", "runtime-trigger-binding"]
        };

    private static readonly IReadOnlyDictionary<string, string[]> ExpectedNativeRoutes =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["bookmark-lookup"] = ["list-by-stimulus-and-type", "list-by-stimulus-type"],
            ["checkpoint-commit"] = [],
            ["command-send-lease-ack"] =
            ["lease-visible-commands-by-execution", "list-visible-command-executions", "count-pending-commands-by-execution"],
            ["diagnostics-durable-history"] =
            [
                "resources-by-last-seen", "resources-by-status", "resources-by-service",
                "traces-by-last-seen", "trace-detail", "metrics-by-last-seen", "logs-by-last-seen",
                "structured-log-recent", "structured-log-replay"
            ],
            ["due-timer-selection"] = ["list-due"],
            ["iam-normalized-lookup-update"] =
            ["find-user-by-normalized-name", "find-user-by-normalized-email", "find-role-by-normalized-name", "list-user-roles", "list-role-users"],
            ["outbox-drain"] = ["list-claimable"],
            ["placement-takeover"] = ["list-owned-live-placements"],
            ["queue-drain"] = ["list-pending-scheduler-workflow-executions", "list-by-workflow-execution"],
            ["recovery-scan"] =
            ["list-recovery-detected", "list-recovery-by-lease-expiry", "list-recovery-by-lease-acquisition", "list-recovery-by-heartbeat"],
            ["recurring-schedule-selection"] = ["list-due", "page-by-publication"],
            ["secret-create-read-list"] = ["list-filtered"],
            ["trigger-binding-stimulus-lookup"] =
            ["list-by-stimulus-and-type", "list-by-stimulus-type", "page-live-by-scope"]
        };

    [Fact]
    public void Workload_schema_is_a_closed_versioned_handoff_contract()
    {
        var schema = ReadJson(WorkloadSchemaPath);
        var definitions = schema["$defs"]!.AsObject();
        var workload = definitions["workload"]!.AsObject();

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema["$schema"]!.GetValue<string>());
        Assert.Equal(1, schema["properties"]!["schemaVersion"]!["const"]!.GetValue<int>());
        Assert.False(workload["additionalProperties"]!.GetValue<bool>());
        Assert.Equal(
            [
                "id", "version", "scenarioId", "owner", "handoffTarget", "publicOperation",
                "coverageRows", "input", "operationSequence", "requiredProviders", "requiredNativeRoutes",
                "requiredProviderEvidence", "correctness", "efContractBaseline", "benchmarkAdmission",
                "physicalFormsFor646", "artifactRetention"
            ],
            workload["required"]!.AsArray().Select(value => value!.GetValue<string>()));
        Assert.Equal("#646", workload["properties"]!["handoffTarget"]!["const"]!.GetValue<string>());
        Assert.Equal("not-executed", definitions["efContractBaseline"]!["properties"]!["executionStatus"]!["const"]!.GetValue<string>());
        Assert.Contains(
            "BoundedQueryDeclaration.Identity",
            workload["properties"]!["requiredNativeRoutes"]!["$comment"]!.GetValue<string>());
        Assert.Null(workload["properties"]!["requiredNativeRoutes"]!["minItems"]);
        Assert.Equal(
            ["sqlite", "sqlserver", "postgresql", "mongodb"],
            definitions["providers"]!["prefixItems"]!.AsArray()
                .Select(value => value!["const"]!.GetValue<string>()));
    }

    [Fact]
    public void Ledger_maps_the_additive_35_row_denominator_to_reviewed_workload_lanes()
    {
        var ledger = ReadJson(LedgerPath);
        var actual = ledger["entries"]!.AsArray()
            .Select(entry => entry!.AsObject())
            .GroupBy(entry => entry["performanceWorkload"]!.GetValue<string>(), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry["id"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        Assert.Equal(ExpectedLedgerMapping.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (workload, expectedRows) in ExpectedLedgerMapping)
            Assert.Equal(expectedRows.Order(StringComparer.Ordinal), actual[workload]);
        Assert.Equal(35, actual.SelectMany(pair => pair.Value).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Workloads_name_exact_manifest_native_routes_and_match_their_ledger_rows()
    {
        var documents = WorkloadPaths.Select(ReadJson).ToArray();
        Assert.All(documents, document => Assert.Equal(1, document["schemaVersion"]!.GetValue<int>()));

        var workloads = documents
            .SelectMany(document => document["workloads"]!.AsArray())
            .Select(workload => workload!.AsObject())
            .GroupBy(workload => workload["id"]!.GetValue<string>(), StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(workload => workload["version"]!.GetValue<string>(), StringComparer.Ordinal).First())
            .ToDictionary(workload => workload["id"]!.GetValue<string>(), StringComparer.Ordinal);

        Assert.Equal(ExpectedNativeRoutes.Keys.Order(StringComparer.Ordinal), workloads.Keys.Order(StringComparer.Ordinal));
        foreach (var (id, expectedRoutes) in ExpectedNativeRoutes)
        {
            var workload = workloads[id];

            Assert.Equal(expectedRoutes, workload["requiredNativeRoutes"]!.AsArray().Select(route => route!.GetValue<string>()));
            Assert.Equal(
                ExpectedLedgerMapping[id].Order(StringComparer.Ordinal),
                workload["coverageRows"]!.AsArray().Select(row => row!.GetValue<string>()).Order(StringComparer.Ordinal));
            Assert.Equal(
                ["sqlite", "sqlserver", "postgresql", "mongodb"],
                workload["requiredProviders"]!.AsArray().Select(provider => provider!.GetValue<string>()));
            Assert.Equal(
                ["mongodb", "postgresql", "sqlite", "sqlserver"],
                workload["requiredProviderEvidence"]!.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal));
            Assert.Equal("not-executed", workload["efContractBaseline"]!["executionStatus"]!.GetValue<string>());
            Assert.Equal("#646", workload["efContractBaseline"]!["executionOwner"]!.GetValue<string>());
            var admission = workload["benchmarkAdmission"]!.AsObject();
            var expectedBlockedReason = id switch
            {
                "diagnostics-durable-history" => "gate.diagnostics.absolute-budget-required",
                _ => null
            };
            Assert.Equal(
                expectedBlockedReason is null ? "ready" : "blocked",
                admission["status"]!.GetValue<string>());
            Assert.Equal(
                expectedBlockedReason ?? "benchmark.ready",
                admission["reason"]!.GetValue<string>());
        }
    }

    [Fact]
    public void Current_identity_workload_has_a_deterministic_correctness_digest_and_all_provider_prerequisites()
    {
        var document = ReadJson(IdentityWorkloadPath);
        var workload = document["workloads"]!.AsArray()
            .Select(candidate => candidate!.AsObject())
            .Single(candidate => candidate["id"]!.GetValue<string>() == "iam-normalized-lookup-update");
        var ledgerRows = ExpectedLedgerMapping["iam-normalized-lookup-update"];

        Assert.Equal(1, document["schemaVersion"]!.GetValue<int>());
        Assert.Equal("iam-normalized-lookup-update", workload["id"]!.GetValue<string>());
        Assert.Equal("1.1.0", workload["version"]!.GetValue<string>());
        Assert.Equal("#646", workload["handoffTarget"]!.GetValue<string>());
        Assert.Equal(
            ledgerRows.Order(StringComparer.Ordinal),
            workload["coverageRows"]!.AsArray().Select(row => row!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.All(
            workload["coverageRows"]!.AsArray().Select(row => row!.GetValue<string>()),
            row => Assert.Contains(row, ledgerRows));
        Assert.Equal(
            "5713ce9b09b68d368d7448041cf513907a648e53df61ccfc307a91381199a8e9",
            workload["input"]!["fingerprintSha256"]!.GetValue<string>());
        Assert.Equal(
            "32b62d5597e8b03715d606be9de81af9a363fe05aa2c7bf6d3f3e4cd185ddbbc",
            workload["correctness"]!["resultDigestSha256"]!.GetValue<string>());
        Assert.Equal(
            ["sqlite", "sqlserver", "postgresql", "mongodb"],
            workload["requiredProviders"]!.AsArray().Select(provider => provider!.GetValue<string>()));
        Assert.Equal(
            ["mongodb", "postgresql", "sqlite", "sqlserver"],
            workload["requiredProviderEvidence"]!.AsObject().Select(pair => pair.Key).Order(StringComparer.Ordinal));
        Assert.Equal("not-executed", workload["efContractBaseline"]!["executionStatus"]!.GetValue<string>());
        Assert.Equal("#646", workload["efContractBaseline"]!["executionOwner"]!.GetValue<string>());
        Assert.Equal("ready", workload["benchmarkAdmission"]!["status"]!.GetValue<string>());
        Assert.Equal("benchmark.ready", workload["benchmarkAdmission"]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public void Operator_runner_uses_the_live_registry_and_keeps_evidence_phases_separate()
    {
        var runner = File.ReadAllText(Path.Combine(RepoRoot, "tools", "groundwork", "run-e3-medium-baseline.py"));

        Assert.Contains("describe-matrix", runner, StringComparison.Ordinal);
        Assert.Contains("AdapterHostRevision", runner, StringComparison.Ordinal);
        Assert.Contains("HarnessRevision", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("WORKLOADS =", runner, StringComparison.Ordinal);
        foreach (var command in new[] { "capture", "correctness", "measure" })
            Assert.Contains($"commands.add_parser(\"{command}\"", runner, StringComparison.Ordinal);
        Assert.Contains("for name in (\"compare\", \"gate\")", runner, StringComparison.Ordinal);
        Assert.Contains("Dry run only", runner, StringComparison.Ordinal);
        Assert.Contains("require_idle_host()", runner, StringComparison.Ordinal);
        Assert.Contains("def process_pid(", runner, StringComparison.Ordinal);
        Assert.Contains("next(csv.reader([stripped]))", runner, StringComparison.Ordinal);
        Assert.Contains("stripped.split(maxsplit=1)[0]", runner, StringComparison.Ordinal);
        Assert.Contains("process_pid(line, windows=windows) != own_pid", runner, StringComparison.Ordinal);
        Assert.DoesNotContain("own_pid not in line", runner, StringComparison.Ordinal);

        var targetContext = runner.IndexOf("def target_context(", StringComparison.Ordinal);
        Assert.True(targetContext >= 0);
        Assert.True(
            runner.IndexOf("require_phase(registration, phase)", targetContext, StringComparison.Ordinal) <
            runner.IndexOf("probe_provider(root", targetContext, StringComparison.Ordinal));
        foreach (var phase in new[] { "capture", "correctness", "measure" })
        {
            var method = runner.IndexOf($"def {phase}(", StringComparison.Ordinal);
            var pathAdmission = runner.IndexOf("ensure_external(", method, StringComparison.Ordinal);
            var contextAdmission = runner.IndexOf($"target_context(args, \"{phase}\"", method, StringComparison.Ordinal);
            Assert.True(method >= 0 && pathAdmission >= 0 && contextAdmission >= 0);
            Assert.True(pathAdmission < contextAdmission);
        }

        var adapterHost = File.ReadAllText(Path.Combine(
            RepoRoot,
            "benchmarks",
            "Elsa.Groundwork.StorePerformance.AdapterHost",
            "Program.cs"));
        var harness = File.ReadAllText(Path.Combine(
            RepoRoot,
            "benchmarks",
            "Elsa.Groundwork.StorePerformance.Benchmarks",
            "Program.cs"));
        Assert.Contains("RequireCleanCurrentBuild", adapterHost, StringComparison.Ordinal);
        Assert.Contains("RequireCleanCurrentBuild", harness, StringComparison.Ordinal);
        Assert.Contains("RequireWithin", harness, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("blocked")]
    [InlineData("redesign")]
    public void Missing_blocked_or_redesign_verdict_cannot_advance_a_lane(string? outcome)
    {
        var ledger = ReadJson(LedgerPath);
        var entry = Entry(ledger, "runtime-bookmark-state");
        entry["status"] = "performance-complete";
        if (outcome is not null)
        {
            entry["performanceVerdict"] = new JsonObject
            {
                ["outcome"] = outcome,
                ["acceptedShape"] = "specialized-path",
                ["evidence"] = "#646 verdict fixture"
            };
        }

        var baselineEntryIds = ledger["entries"]!.AsArray()
            .Select(candidate => candidate!["id"]!.GetValue<string>())
            .ToArray();
        var findings = new GroundworkCoverageLedgerValidator(LedgerSchemaPath, baselineEntryIds).Validate(ledger);

        Assert.Contains(
            outcome is null
                ? "runtime-bookmark-state: status 'performance-complete' requires a #646 performance verdict."
                : "runtime-bookmark-state: status 'performance-complete' requires a passing or reviewed not-hot-path #646 verdict; " +
                  $"found '{outcome}'.",
            findings);
    }

    private static JsonObject Entry(JsonObject ledger, string id) => ledger["entries"]!.AsArray()
        .Select(entry => entry!.AsObject())
        .Single(entry => entry["id"]!.GetValue<string>() == id);

    private static JsonObject ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static string WorkloadSchemaPath => Path.Combine(
        RepoRoot,
        "specs",
        "094-harden-groundwork-stores",
        "contracts",
        "performance-workload.schema.json");

    private static string LedgerSchemaPath => Path.Combine(
        RepoRoot,
        "specs",
        "094-harden-groundwork-stores",
        "contracts",
        "coverage-ledger.schema.json");

    private static string LedgerPath => Path.Combine(
        RepoRoot,
        "specs",
        "094-harden-groundwork-stores",
        "coverage-ledger.json");

    private static string IdentityWorkloadPath => Path.Combine(
        RepoRoot,
        "specs",
        "094-harden-groundwork-stores",
        "workloads",
        "iam-secrets.json");

    private static IReadOnlyList<string> WorkloadPaths =>
    [
        Path.Combine(RepoRoot, "specs", "094-harden-groundwork-stores", "workloads", "runtime.json"),
        Path.Combine(RepoRoot, "specs", "094-harden-groundwork-stores", "workloads", "distributed-runtime.json"),
        Path.Combine(RepoRoot, "specs", "094-harden-groundwork-stores", "workloads", "diagnostics.json"),
        Path.Combine(RepoRoot, "specs", "094-harden-groundwork-stores", "workloads", "diagnostics-durable-history-v1.2.json"),
        Path.Combine(RepoRoot, "specs", "094-harden-groundwork-stores", "workloads", "recovery-scan-v1.2.json"),
        IdentityWorkloadPath,
        Path.Combine(RepoRoot, "specs", "094-harden-groundwork-stores", "workloads", "secret-create-read-list-v1.1.json")
    ];

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
