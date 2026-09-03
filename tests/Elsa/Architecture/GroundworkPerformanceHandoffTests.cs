using System.Diagnostics;
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
        Assert.Contains("next(csv.reader([stripped], strict=True))", runner, StringComparison.Ordinal);
        Assert.Contains("TASKLIST_CSV_ROW.fullmatch(stripped)", runner, StringComparison.Ordinal);
        Assert.Contains("raw_pid.isascii() and raw_pid.isdecimal()", runner, StringComparison.Ordinal);
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

        var measureMethod = runner.IndexOf("def measure(", StringComparison.Ordinal);
        var compareMethod = runner.IndexOf("def compare_or_gate(", measureMethod, StringComparison.Ordinal);
        var measureBody = runner[measureMethod..compareMethod];
        var outputAdmission = measureBody.IndexOf("ensure_measurement_output_admissible(output)", StringComparison.Ordinal);
        var measurementContextAdmission = measureBody.IndexOf("target_context(args, \"measure\"", StringComparison.Ordinal);
        Assert.True(outputAdmission >= 0 && measurementContextAdmission > outputAdmission);
        Assert.Contains(
            "measurement_command = [\"dotnet\", str(harness), \"measure\", \"--out\", str(output)]",
            measureBody,
            StringComparison.Ordinal);
        Assert.True(
            measureBody.IndexOf("subprocess.run(command,", StringComparison.Ordinal) <
            measureBody.IndexOf("subprocess.run(measurement_command,", StringComparison.Ordinal));

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

    [Fact]
    public void Diagnostics_performance_workflow_is_manual_ungraded_and_phase_separated()
    {
        var workflow = File.ReadAllText(Path.Combine(
            RepoRoot,
            ".github",
            "workflows",
            "http-workflow-performance.yml"));
        var jobStart = workflow.IndexOf("  groundwork-diagnostics:", StringComparison.Ordinal);
        var jobEnd = workflow.IndexOf("\n  alert:", jobStart, StringComparison.Ordinal);

        Assert.True(jobStart >= 0 && jobEnd > jobStart);
        var job = workflow[jobStart..jobEnd];

        Assert.Contains("suite=groundwork-diagnostics", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "if: ${{ github.event_name == 'workflow_dispatch' && inputs.suite == 'groundwork-diagnostics' }}",
            job,
            StringComparison.Ordinal);
        Assert.Contains("timeout-minutes: 300", job, StringComparison.Ordinal);
        Assert.Contains("matrix:\n        provider:\n          - sqlite\n          - postgresql\n          - sqlserver\n          - mongodb", job, StringComparison.Ordinal);
        Assert.Contains("run-e3-medium-baseline.py capture", job, StringComparison.Ordinal);
        Assert.Contains("run-e3-medium-baseline.py correctness", job, StringComparison.Ordinal);
        Assert.Contains("DIAGNOSTICS_CORRECTNESS_DIR:", job, StringComparison.Ordinal);
        Assert.Contains("/${{ matrix.provider }}/correctness", job, StringComparison.Ordinal);
        var correctnessStart = job.IndexOf("      - name: Verify diagnostics correctness", StringComparison.Ordinal);
        var correctnessEnd = job.IndexOf("      - name: Stop non-target containers before timing", correctnessStart, StringComparison.Ordinal);
        Assert.True(correctnessStart >= 0 && correctnessEnd > correctnessStart);
        var correctnessStep = job[correctnessStart..correctnessEnd];
        Assert.Contains("--out \"$DIAGNOSTICS_CORRECTNESS_DIR\"", correctnessStep, StringComparison.Ordinal);
        Assert.DoesNotContain("--out \"$DIAGNOSTICS_OUTPUT_DIR\"", correctnessStep, StringComparison.Ordinal);
        var measurementStart = job.IndexOf("      - name: Measure diagnostics evidence", correctnessEnd, StringComparison.Ordinal);
        var measurementEnd = job.IndexOf("      - name: Record provider diagnostics", measurementStart, StringComparison.Ordinal);
        Assert.True(measurementStart >= 0 && measurementEnd > measurementStart);
        var measurementStep = job[measurementStart..measurementEnd];
        Assert.Contains("--out \"$DIAGNOSTICS_OUTPUT_DIR\"", measurementStep, StringComparison.Ordinal);
        Assert.DoesNotContain("--out \"$DIAGNOSTICS_CORRECTNESS_DIR\"", measurementStep, StringComparison.Ordinal);
        Assert.Contains("pg_isready -h 127.0.0.1", job, StringComparison.Ordinal);
        Assert.Contains("psql -h 127.0.0.1", job, StringComparison.Ordinal);
        Assert.Contains("mongo_attestation=\"$(image_attestation \"$mongo_container\")\"", job, StringComparison.Ordinal);
        Assert.Contains("ELSA_BENCH_MONGO_CONTAINER_ATTESTATION", job, StringComparison.Ordinal);
        Assert.Contains(
            "User Id=sa;Pass" + "word=%s;TrustServerCertificate=True;Encrypt=False;Initial Catalog=%s",
            job,
            StringComparison.Ordinal);
        Assert.Contains("dotnet build-server shutdown", job, StringComparison.Ordinal);
        Assert.Contains("run-e3-medium-baseline.py measure", job, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", job, StringComparison.Ordinal);
        Assert.DoesNotContain("budget-gate", job, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("run-e3-medium-baseline.py compare", job, StringComparison.Ordinal);
        Assert.Contains("$DIAGNOSTICS_WORK_ROOT/run-summary.txt", job, StringComparison.Ordinal);
        Assert.DoesNotContain("$DIAGNOSTICS_OUTPUT_DIR/run-summary.txt", job, StringComparison.Ordinal);
    }

    [Fact]
    public void Operator_runner_rejects_unbound_preexisting_measurement_output_before_resolving_context()
    {
        var runnerPath = Path.Combine(RepoRoot, "tools", "groundwork", "run-e3-medium-baseline.py");
        const string assertions = """
import runpy
import sys
import tempfile
from pathlib import Path

module = runpy.run_path(sys.argv[1])
ensure_measurement_output_admissible = module["ensure_measurement_output_admissible"]

with tempfile.TemporaryDirectory() as directory:
    root = Path(directory)
    missing = root / "missing"
    ensure_measurement_output_admissible(missing)

    empty = root / "empty"
    empty.mkdir()
    ensure_measurement_output_admissible(empty)

    occupied = root / "occupied"
    occupied.mkdir()
    (occupied / "correctness.process.json").write_text("{}", encoding="utf-8")
    try:
        ensure_measurement_output_admissible(occupied)
    except ValueError as exception:
        assert "artifact-manifest.v2.json" in str(exception), exception
        assert "fresh directory" in str(exception), exception
    else:
        raise AssertionError("measurement accepted preexisting output without a manifest")

    resumable = root / "resumable"
    resumable.mkdir()
    (resumable / "artifact-manifest.v2.json").write_text("{}", encoding="utf-8")
    (resumable / "first.process.json").write_text("{}", encoding="utf-8")
    ensure_measurement_output_admissible(resumable)

    regular_file = root / "regular-file"
    regular_file.write_text("not a directory", encoding="utf-8")
    try:
        ensure_measurement_output_admissible(regular_file)
    except ValueError as exception:
        assert "not a directory" in str(exception), exception
    else:
        raise AssertionError("measurement accepted a regular file as its output directory")
""";

        var result = RunPython(assertions, runnerPath);

        Assert.True(result.ExitCode == 0, result.Error);
    }

    [Fact]
    public void Operator_runner_invalidates_evidence_when_capture_does_not_complete()
    {
        var runnerPath = Path.Combine(RepoRoot, "tools", "groundwork", "run-e3-medium-baseline.py");
        const string assertions = """
import runpy
import sys
import tempfile
from pathlib import Path

module = runpy.run_path(sys.argv[1])
begin_capture = module["begin_capture"]
complete_capture = module["complete_capture"]
require_capture_complete = module["require_capture_complete"]

with tempfile.TemporaryDirectory() as directory:
    evidence = Path(directory) / "evidence"
    evidence.mkdir()
    (evidence / "previous.native-plan.json").write_text("previous", encoding="utf-8")

    try:
        begin_capture(evidence)
    except ValueError as exception:
        assert "empty" in str(exception), exception
    else:
        raise AssertionError("capture reused a non-empty evidence directory")

    try:
        require_capture_complete(evidence)
    except ValueError as exception:
        assert "incomplete" in str(exception), exception
    else:
        raise AssertionError("failed capture left evidence admissible")

    marker = module["capture_marker"](evidence)
    marker.unlink()
    fresh = Path(directory) / "fresh-evidence"
    marker = begin_capture(fresh)
    try:
        require_capture_complete(fresh)
    except ValueError as exception:
        assert "incomplete" in str(exception), exception
    else:
        raise AssertionError("in-progress capture was not blocked")
    complete_capture(marker)
    require_capture_complete(fresh)
""";

        var result = RunPython(assertions, runnerPath);

        Assert.True(result.ExitCode == 0, result.Error);
    }

    [Fact]
    public void Operator_runner_process_guard_matches_exact_pids_and_fails_closed()
    {
        var runnerPath = Path.Combine(RepoRoot, "tools", "groundwork", "run-e3-medium-baseline.py");
        const string assertions = """
import runpy
import sys
from types import SimpleNamespace

module = runpy.run_path(sys.argv[1])
process_pid = module["process_pid"]
require_idle_host = module["require_idle_host"]
runner_globals = require_idle_host.__globals__

cases = [
    (" 1234 dotnet test", False, 1234),
    ("91234 dotnet test", False, 91234),
    ("+1234 dotnet test", False, None),
    ("1_234 dotnet test", False, None),
    ('"dotnet.exe","1234","Console","1","12,345 K"', True, 1234),
    ('"dotnet.exe","91234","Console","1","12,345 K"', True, 91234),
    ('"dotnet.exe","+1234","Console","1","12,345 K"', True, None),
    ('"dotnet.exe","1_234","Console","1","12,345 K"', True, None),
    ('"dotnet.exe","1234"', True, None),
    ('dotnet.exe,1234,Console,1,"12,345 K"', True, None),
    ('"dotnet.exe","not-a-pid","Console","1","12,345 K"', True, None),
    ("malformed dotnet row", False, None),
    ("", False, None),
]
for line, windows, expected in cases:
    actual = process_pid(line, windows=windows)
    assert actual == expected, (line, windows, expected, actual)

def run_guard(name, process_table):
    commands = []
    runner_globals["os"] = SimpleNamespace(name=name, getpid=lambda: 1234)
    runner_globals["repository_root"] = lambda: "."
    runner_globals["run_text"] = lambda command, cwd: commands.append(command) or process_table
    try:
        require_idle_host()
        result = None
    except ValueError as exception:
        result = str(exception)
    return result, commands

result, commands = run_guard("posix", " 1234 dotnet test\n5678 harmless-process")
assert result is None, result
assert commands == [["ps", "-Ao", "pid=,command="]], commands

result, _ = run_guard("posix", "91234 dotnet test")
assert "91234 dotnet test" in result, result
result, _ = run_guard("posix", "malformed dotnet row")
assert "malformed dotnet row" in result, result
for malformed_self_posix in ("+1234 dotnet test", "1_234 dotnet test"):
    result, _ = run_guard("posix", malformed_self_posix)
    assert malformed_self_posix in result, result

own_windows = '"dotnet.exe","1234","Console","1","12,345 K"'
result, commands = run_guard("nt", own_windows)
assert result is None, result
assert commands == [["tasklist", "/fo", "csv", "/nh"]], commands

collision_windows = '"dotnet.exe","91234","Console","1","12,345 K"'
result, _ = run_guard("nt", collision_windows)
assert collision_windows in result, result
malformed_windows = '"dotnet.exe","not-a-pid","Console","1","12,345 K"'
result, _ = run_guard("nt", malformed_windows)
assert malformed_windows in result, result
malformed_self_windows = '"dotnet.exe","1234","Console","1","12,345 K"junk'
result, _ = run_guard("nt", malformed_self_windows)
assert result is not None, "malformed Windows process row was treated as the current process"
assert malformed_self_windows in result, result
for malformed_self_windows in (
    '"dotnet.exe","+1234","Console","1","12,345 K"',
    '"dotnet.exe","1_234","Console","1","12,345 K"',
    '"dotnet.exe","1234"',
    'dotnet.exe,1234,Console,1,"12,345 K"',
):
    result, _ = run_guard("nt", malformed_self_windows)
    assert malformed_self_windows in result, result
""";

        var result = RunPython(assertions, runnerPath);

        Assert.True(result.ExitCode == 0, result.Error);
    }

    [Fact]
    public void Operator_runner_accounts_for_trace_detail_composite_evidence()
    {
        var runnerPath = Path.Combine(RepoRoot, "tools", "groundwork", "run-e3-medium-baseline.py");
        const string assertions = """
import json
import hashlib
import runpy
import sys
import tempfile
from pathlib import Path

validate_evidence = runpy.run_path(sys.argv[1])["validate_evidence"]
request = {
    "ComparisonCohortId": "cohort",
    "MeasurementSetId": "measurement",
    "WorkloadId": "diagnostics-durable-history",
    "WorkloadVersion": "1.3.0",
    "Provider": "sqlite",
    "Adapter": "groundwork-v2",
    "PhysicalForm": "ordinary-groundwork-diagnostics-units",
    "Scale": "medium",
    "CommitSha": "a" * 40,
    "HarnessAssemblySha256": "b" * 64,
    "CompositionFingerprint": "c" * 64,
    "HostFingerprintSha256": "d" * 64,
    "ProviderVersion": "3.50.4",
    "ProviderTopology": "file-backed-distinct-connections",
    "ProviderConfiguration": {},
    "Seed": "seed",
    "InputFingerprintSha256": "e" * 64,
    "NativePlanIdentity": "identity",
}
document = {
    "SchemaVersion": 2,
    **{name: value for name, value in request.items() if name != "NativePlanIdentity"},
    "Identity": request["NativePlanIdentity"],
    "Routes": [],
    "BlockedRoutes": [],
    "TraceDetailConstituents": [],
}
registration = {"RequiredNativeRoutes": ["trace-detail"]}
with tempfile.TemporaryDirectory() as directory:
    raw_plan = Path(directory) / "trace.raw.json"
    page_plan = Path(directory) / "trace-page.raw.json"
    log_plan = Path(directory) / "trace-log.raw.json"
    raw_plan.write_text('{"plan":"initial"}', encoding="utf-8")
    page_plan.write_text('{"plan":"continuation"}', encoding="utf-8")
    log_plan.write_text('{"plan":"log"}', encoding="utf-8")
    constituent = {
        "RouteIdentity": "trace-detail/spans-by-trace-key-start-id",
        "RawPlanReference": raw_plan.name,
        "RawPlanSha256": hashlib.sha256(raw_plan.read_bytes()).hexdigest(),
        "PlanClassification": "index-search",
        "PhysicalIndexName": "trace-index",
        "CommandText": "SELECT page",
        "PhysicalCardinality": 100_000,
        "HasStorageScopePredicate": True,
        "HasRoutePredicate": True,
        "FiniteLimit": 1,
        "PublicRowBound": 2,
        "MaterializedCandidateCount": 2,
        "ObservedCommandCount": 2,
        "MaxInvocationCount": 2,
        "Pages": [{
            "PageIndex": 1,
            "RawPlanReference": page_plan.name,
            "RawPlanSha256": hashlib.sha256(page_plan.read_bytes()).hexdigest(),
            "CommandText": "SELECT continuation page",
        }],
    }
    point_read = {
        **constituent,
        "RawPlanReference": "",
        "RawPlanSha256": "",
        "PlanClassification": "primary-key-read",
        "PhysicalIndexName": "",
        "FiniteLimit": 1,
        "PublicRowBound": 1,
        "MaterializedCandidateCount": 1,
        "ObservedCommandCount": 1,
        "MaxInvocationCount": 1,
        "Pages": None,
    }
    log_constituent = {
        **constituent,
        "RouteIdentity": "trace-detail/logs-by-trace-key-timestamp-id",
        "RawPlanReference": log_plan.name,
        "RawPlanSha256": hashlib.sha256(log_plan.read_bytes()).hexdigest(),
        "PublicRowBound": 1,
        "MaterializedCandidateCount": 1,
        "ObservedCommandCount": 1,
        "MaxInvocationCount": 1,
        "Pages": [],
    }
    document["TraceDetailConstituents"] = [
        {**point_read, "RouteIdentity": "trace-detail/summary-by-trace-key"},
        constituent,
        log_constituent,
        {**point_read, "RouteIdentity": "trace-detail/resources-by-id"},
    ]
    path = Path(directory) / "native-plan.json"

    def expect_invalid(candidate, message):
        path.write_text(json.dumps(candidate), encoding="utf-8")
        try:
            validate_evidence(path, request, registration, timing=True)
        except ValueError:
            return
        raise AssertionError(message)

    path.write_text(json.dumps(document), encoding="utf-8")
    validate_evidence(path, request, registration, timing=True)

    reserved_plan = Path(directory) / "Gate.v1.json"
    reserved_plan.write_text('{"plan":"reserved"}', encoding="utf-8")
    reserved = json.loads(json.dumps(document))
    reserved["TraceDetailConstituents"][1]["RawPlanReference"] = reserved_plan.name
    reserved["TraceDetailConstituents"][1]["RawPlanSha256"] = hashlib.sha256(reserved_plan.read_bytes()).hexdigest()
    expect_invalid(reserved, "mixed-case reserved result filename was accepted as raw-plan evidence")

    wrong_point_read = json.loads(json.dumps(document))
    wrong_point_read["TraceDetailConstituents"][0]["PlanClassification"] = "index-search"
    wrong_point_read["TraceDetailConstituents"][0]["PhysicalIndexName"] = "fake-index"
    expect_invalid(wrong_point_read, "indexed classification was accepted for a trace-detail point read")

    point_read_with_artifact = json.loads(json.dumps(document))
    point_read_with_artifact["TraceDetailConstituents"][0]["RawPlanReference"] = raw_plan.name
    point_read_with_artifact["TraceDetailConstituents"][0]["RawPlanSha256"] = hashlib.sha256(raw_plan.read_bytes()).hexdigest()
    expect_invalid(point_read_with_artifact, "raw-plan artifact was accepted for a trace-detail point read")

    wrong_indexed_query = json.loads(json.dumps(document))
    wrong_indexed_query["TraceDetailConstituents"][1]["PlanClassification"] = " "
    wrong_indexed_query["TraceDetailConstituents"][1]["PhysicalIndexName"] = ""
    expect_invalid(wrong_indexed_query, "blank indexed-query classification and index were accepted")

    missing_predicate = json.loads(json.dumps(document))
    missing_predicate["TraceDetailConstituents"][2]["HasRoutePredicate"] = False
    expect_invalid(missing_predicate, "trace-detail evidence without the route predicate was accepted")

    raw_plan.write_text("tampered", encoding="utf-8")
    expect_invalid(document, "tampered trace-detail raw plan was accepted")
    raw_plan.write_text('{"plan":"initial"}', encoding="utf-8")
    page_plan.write_text("tampered", encoding="utf-8")
    expect_invalid(document, "tampered trace-detail continuation plan was accepted")
    page_plan.write_text('{"plan":"continuation"}', encoding="utf-8")

    malformed = dict(document)
    malformed["TraceDetailConstituents"] = [{"RouteIdentity": "trace-detail/spans-by-trace-key-start-id"}]
    expect_invalid(malformed, "malformed trace-detail constituent was accepted")

    incomplete = dict(document)
    incomplete["TraceDetailConstituents"] = document["TraceDetailConstituents"][:-1]
    expect_invalid(incomplete, "incomplete trace-detail constituent set was accepted")

    document["TraceDetailConstituents"] = []
    expect_invalid(document, "empty trace-detail constituent evidence was accepted")
""";

        var result = RunPython(assertions, runnerPath);

        Assert.True(result.ExitCode == 0, result.Error);
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

    private static (int ExitCode, string Error) RunPython(string script, string runnerPath)
    {
        foreach (var executable in new[] { "python3", "python" })
        {
            try
            {
                var startInfo = new ProcessStartInfo(executable)
                {
                    WorkingDirectory = RepoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add(script);
                startInfo.ArgumentList.Add(runnerPath);
                using var process = Process.Start(startInfo);
                if (process is null)
                    continue;
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return (process.ExitCode, string.Join(Environment.NewLine, output, error));
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Try the next conventional Python executable name.
            }
        }

        return (-1, "Python is required to validate the Groundwork operator runner.");
    }

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
        Path.Combine(RepoRoot, "specs", "094-harden-groundwork-stores", "workloads", "diagnostics-durable-history-v1.3.json"),
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
