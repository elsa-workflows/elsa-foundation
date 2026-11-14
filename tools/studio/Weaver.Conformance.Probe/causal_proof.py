"""Eleven concrete, serial source controls for #3831/T044. Not a consumer witness.

Run only in a disposable clean checkout with its .NET graph restored and Studio
dependencies installed. Raw reports/logs are private evidence; no environment
values are recorded. Each recipe builds/runs baseline, mutation, and restoration.
The normalization control covers ordinal declared-code identity only; it does not
qualify every owner callback, forwarding/retention path, or typed channel.
The two Copilot auth controls are source-authored; their causal runtime evidence
must be established separately on the exact candidate, within the same budgets.
"""
from __future__ import annotations

import argparse
from collections import Counter
from contextlib import contextmanager
from dataclasses import dataclass
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import subprocess
import time
import xml.etree.ElementTree as ET


CORE = "Extensions/Weaver/src/Elsa.Weaver/Services/"
TESTS = "Extensions/Weaver/tests/Tests/"
PERSISTENCE = "Extensions/Weaver/tests/Persistence/Tests/"
CLIENT = "Studio/src/essentials/Elsa.Studio.Hosting/Client/"
WEB = CLIENT + "src/app/weaver/"
INVENTORY_TEST = "withdraws a %s origin before nested callbacks and preserves a healthy resource owner"
INVENTORY_NAMES = tuple("prompt-time Studio support discovery withdraws a " + state
    + " origin before nested callbacks and preserves a healthy resource owner"
    for state in ("absent", "disabled", "failed", "incompatible"))
COMMAND_SECONDS = 360
TOTAL_SECONDS = 2400  # Leave five minutes of the initial 45-minute CI allocation for setup/upload.


@dataclass(frozen=True)
class Recipe:
    id: str
    scope: str
    source: str
    before: str
    after: str
    test: str
    rows: tuple[str, ...]
    failures: tuple[str, ...]
    assertion_file: str
    assertion_line: int
    assertion_text: str
    cause: str
    project: str = TESTS + "Elsa.Weaver.Tests.csproj"
    owner_dll: str = "Elsa.Weaver.dll"

    def names(self) -> tuple[str, ...]:
        return self.rows if self.scope == "studio" else tuple(self.test + row for row in self.rows)

    def failed_names(self) -> tuple[str, ...]:
        return self.failures if self.scope == "studio" else tuple(self.test + row for row in self.failures)


RECEIPT_FAULTS = ("declaration", "client-scope", "after-revision", "after-id", "after-type", "after-module", "before",
    "invocation", "call", "tool", "payload", "evidence", "undefined-evidence", "unsuccessful", "null-after", "null-result",
    "error-with-receipt", "throw", "cancel")
WRITE_PATHS = ("prepared-classes", "performed-proposal", "proposal-result-code", "proposal-result-scope", "execution-status",
    "execution-classes", "execution-result", "execution-error", "message-error", "message-result-code", "message-result-scope",
    "message-history", "paused-results", "paused-history", "snapshot-code", "snapshot-scope", "empty-code",
    "proposal-result-error", "execution-result-error", "message-result-error", "message-history-error", "paused-results-error",
    "paused-history-error", "snapshot-error")
WRITE_FAILURES = ("proposal-result-code", "execution-result", "message-result-code", "message-history", "paused-results",
    "paused-history", "empty-code")
OPTIONAL_ROWS = tuple(f'(failure: "{failure}", expired: {expired})' for expired in ("False", "True")
    for failure in ("refresh-throws", "refresh-stalls", "snapshot-unavailable", "snapshot-throws"))
COPILOT_FACTORY_ROWS = tuple(f"(useLoggedInUser: {logged_in}, supplyGitHubToken: {token})"
    for logged_in in ("False", "True") for token in ("False", "True"))
COPILOT_AUTH_ROWS = (
    '(external: True, providerAuth: "configured", expectedMode: "external-runtime")',
    '(external: True, providerAuth: "environment", expectedMode: "external-runtime")',
    '(external: True, providerAuth: "logged-in", expectedMode: "external-runtime")',
    '(external: True, providerAuth: "all", expectedMode: "external-runtime")',
    '(external: False, providerAuth: "configured", expectedMode: "configured-token")',
    '(external: False, providerAuth: "environment", expectedMode: "environment")',
    '(external: False, providerAuth: "logged-in", expectedMode: "logged-in-user")',
)

# These are exact source fragments, exact selected rows, and the intended assertion.
# A moved assertion/changed test contract requires source review and a recipe update.
RECIPES = (
    Recipe("policy", "dotnet", CORE + "WeaverPrivilegePolicy.cs",
        " || activation && !current.Activation", "",
        "Elsa.Weaver.Tests.WeaverPrivilegeLifecycleTests.A_declared_class_requires_its_current_host_grant_independently_of_other_classes",
        tuple(f"(required: {kind}, enabled: {enabled})" for kind in ("Administrative", "Irrecoverable", "Activation") for enabled in ("False", "True")),
        ("(required: Activation, enabled: False)",), TESTS + "WeaverPrivilegeLifecycleTests.cs", 32,
        "Assert.Equal(enabled ? WeaverToolDispatchOutcome.ProposalCreated : WeaverToolDispatchOutcome.Denied, dispatch.Outcome);",
        r"Assert\.Equal\(\).*Expected:\s*Denied\s+Actual:\s*ProposalCreated"),
    Recipe("receipt-integrity", "dotnet", CORE + "WeaverInvocationIntegrity.cs",
        "receipt.InvocationId == invocation.InvocationId && receipt.Scope == scope", "receipt.Scope == scope",
        "Elsa.Weaver.Tests.WeaverPerformedTests.The_direct_default_boundary_rejects_wrong_success_or_interruption_after_an_action_ran",
        tuple(f'(fault: "{fault}")' for fault in RECEIPT_FAULTS), ('(fault: "invocation")',),
        TESTS + "WeaverPerformedTests.cs", 119, 'Assert.Equal("uncertain", execution.Value?.Status);',
        r'Assert\.Equal\(\).*Expected:\s*"uncertain".*Actual:\s*"performed"'),
    Recipe("module-code-normalization", "dotnet", CORE + "WeaverModuleCodes.cs",
        "codes.Contains(value, StringComparer.Ordinal)", "codes.Contains(value, StringComparer.OrdinalIgnoreCase)",
        "Elsa.Weaver.Tests.WeaverModuleCodeTests.Undeclared_codes_and_valid_declared_messages_keep_their_compatibility_and_identity",
        ("",), ("",), TESTS + "WeaverModuleCodeTests.cs", 103,
        'Assert.Equal(WeaverModuleCodes.UnsupportedCode, WeaverModuleCodes.Error(valid, ["Native.ok"], ownerOrigin: true).Code);',
        r'Assert\.Equal\(\).*Expected:\s*"module\.code\.unsupported".*Actual:\s*"native\.ok"'),
    Recipe("write-admission", "dotnet", "Extensions/Weaver/src/Persistence/EntityFrameworkCore/Stores/WeaverWriteVersion.cs",
        "receipt.ModuleCode is not null || receipt.Scope == WeaverEffectScope.NoPersistedEffect",
        "receipt.Scope == WeaverEffectScope.NoPersistedEffect",
        "Elsa.Weaver.Persistence.Tests.WeaverSchemaWriteAdmissionTests.Held_legacy_create_and_update_refuse_each_new_representation_without_changing_any_record",
        tuple(f'(path: "{path}")' for path in WRITE_PATHS), tuple(f'(path: "{path}")' for path in WRITE_FAILURES),
        PERSISTENCE + "WeaverSchemaWriteAdmissionTests.cs", 46,
        "var create = await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => store.CreateAsync(record, Owner));",
        r"Assert\.Throws\(\).*No exception was thrown.*EfSchemaWriteRefusedException",
        PERSISTENCE + "Elsa.Weaver.Persistence.Tests.csproj", "Elsa.Weaver.Persistence.EntityFrameworkCore.dll"),
    Recipe("support-retirement", "studio", WEB + "weaverSupportInventory.ts",
        'if (projected.completeness === "complete") {', 'if (projected.completeness === "incomplete") {',
        INVENTORY_TEST, INVENTORY_NAMES, INVENTORY_NAMES, WEB + "__tests__/weaverSupportInventory.test.ts", 53,
        "expect(removed).not.toHaveBeenCalled(); expect(healthy).toHaveBeenCalledTimes(1);",
        r'AssertionError:.*expected.*not.*called.*(?:1 times?|once)'),
    Recipe("retired-origin", "studio", WEB + "weaverRegistrationOrigins.ts",
        'origins.size === 1 && [...origins][0].status === "loaded"',
        'origins.size === 1 && [...origins][0].status !== "registering"',
        INVENTORY_TEST, INVENTORY_NAMES, INVENTORY_NAMES, WEB + "__tests__/weaverSupportInventory.test.ts", 53,
        "expect(removed).not.toHaveBeenCalled(); expect(healthy).toHaveBeenCalledTimes(1);",
        r'AssertionError:.*expected.*not.*called.*(?:1 times?|once)'),
    Recipe("r15-refusal-admission", "dotnet", CORE + "WeaverToolInvoker.cs",
        "var unavailable = await effects.CheckResultAsync(cancellationToken);",
        "await Task.CompletedTask; WeaverError? unavailable = null;",
        "Elsa.Weaver.Tests.WeaverTypedResultAdmissionTests.A_declaring_refusal_rechecks_admission_after_the_awaited_module_callback",
        ("(duringExecution: False)", "(duringExecution: True)"), ("(duringExecution: False)", "(duringExecution: True)"),
        TESTS + "WeaverTypedResultAdmissionTests.cs", 25,
        'Assert.Equal("schema-version-not-finalized", dispatch.Error?.Code);',
        r'Assert\.Equal\(\).*Expected:\s*"schema-version-not-finalized".*Actual:\s*"test\.refused"'),
    Recipe("r15-bare-disclosure", "dotnet", CORE + "WeaverTurnServices.cs",
        "return result.Error is null ? null : await effects.CheckResultAsync(token);",
        "return await Task.FromResult<WeaverError?>(null);",
        "Elsa.Weaver.Tests.WeaverBareErrorDisclosureTests.Bare_history_uses_new_Error_presence_without_tool_metadata_and_preserves_legacy_compatibility",
        ("(typed: False)", "(typed: True)"), ("(typed: True)",), TESTS + "WeaverBareErrorDisclosureTests.cs", 135,
        'Assert.Equal("weaver.effects.not_enabled", Assert.Single(streamed.Events, item => item.Kind == WeaverStreamEventKind.Error).Error?.Code);',
        r"Assert\.Single\(\).*(?:did not contain (?:a|any) matching item|contained 0 matching items)"),
    Recipe("r15-optional-representation", "dotnet", CORE + "WeaverEffectsAdmission.cs",
        "return checks.Length == 1 && checks[0].Evaluate(selection.Requirements).IsAvailable;", "return checks.Length == 1;",
        "Elsa.Weaver.Tests.WeaverTypedResultAdmissionTests.Optional_error_enrichment_cannot_prevent_a_core_veto_or_legacy_expiry",
        OPTIONAL_ROWS, tuple(row for row in OPTIONAL_ROWS if '"snapshot-' in row), TESTS + "WeaverTypedResultAdmissionTests.cs", 121,
        "else Assert.Null(result?.Error); // Unknown optional representation keeps truthful old core state.",
        r"Assert\.Null\(\).*(?:Value|Actual):\s*WeaverError"),
    Recipe("copilot-external-constructor-auth", "dotnet", "Extensions/Weaver/src/GitHubCopilot/Services/GitHubCopilotSdkClientFactory.cs",
        "options.Connection = RuntimeConnection.ForUri(request.RuntimeUrl, request.RuntimeConnectionToken);",
        "options.UseLoggedInUser = request.UseLoggedInUser; options.Connection = RuntimeConnection.ForUri(request.RuntimeUrl, request.RuntimeConnectionToken);",
        "Elsa.Weaver.Tests.GitHubCopilotServerStateTests.External_runtime_client_constructs_with_connection_auth_without_local_provider_auth",
        COPILOT_FACTORY_ROWS, COPILOT_FACTORY_ROWS, TESTS + "GitHubCopilotServerStateTests.cs", 51,
        "await using var client = factory.Create(request);",
        r"^\s*System\.ArgumentException\s*:\s*GitHubToken and UseLoggedInUser cannot be combined with RuntimeConnection\.ForUri \(the existing runtime manages its own auth\)\.",
        owner_dll="Elsa.Weaver.GitHubCopilot.dll"),
    Recipe("copilot-external-auth-mode", "dotnet", "Extensions/Weaver/src/GitHubCopilot/Services/WeaverGitHubCopilotProvider.cs",
        '        if (!string.IsNullOrWhiteSpace(value.RuntimeUrl))\n        {\n            authMode = "external-runtime";\n            return null;\n        }\n\n', "",
        "Elsa.Weaver.Tests.WeaverGitHubCopilotProviderTests.Diagnostics_external_runtime_auth_precedes_unused_provider_auth_and_preserves_local_modes",
        COPILOT_AUTH_ROWS, COPILOT_AUTH_ROWS[:4], TESTS + "WeaverGitHubCopilotProviderTests.cs", 131,
        'Assert.Equal(expectedMode == "environment" ? "environment:" + environmentName : expectedMode, diagnostics.Metadata["authMode"]);',
        r'Assert\.Equal\(\).*Expected:\s*"external-runtime".*Actual:\s*"(?:configured-token"|environment:|logged-in-user")',
        owner_dll="Elsa.Weaver.GitHubCopilot.dll"),
)


class ProofFailure(RuntimeError):
    pass


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def write_json(path: Path, value) -> None:
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def retained(path: Path) -> dict:
    return {"path": str(path), "sha256": digest(path.read_bytes()), "bytes": path.stat().st_size}


def git(root: Path, *arguments: str) -> str:
    return subprocess.check_output(["git", "-c", "core.fsmonitor=false", *arguments], cwd=root, timeout=30).decode().strip()


def require_clean(root: Path, head: str) -> None:
    if git(root, "rev-parse", "HEAD") != head or git(root, "status", "--porcelain", "--untracked-files=normal"):
        raise ProofFailure("The exact checkout is no longer clean; no further source mutation is authorized.")


def require_expected_head(head: str, expected: str | None) -> None:
    if expected is not None and (not re.fullmatch(r"[0-9a-f]{40}", expected) or head != expected):
        raise ProofFailure("The checkout does not match the valid expected causal-proof source identity.")


@contextmanager
def mutation(root: Path, recipe: Recipe, original: bytes, state: dict):
    path = root / recipe.source
    applied = False
    try:
        before, after = recipe.before.encode(), recipe.after.encode()
        if path.is_symlink() or path.read_bytes() != original or original.count(before) != 1:
            raise ProofFailure("Mutation requires one exact uniquely matched source fragment.")
        changed = original.replace(before, after, 1)
        applied = True
        path.write_bytes(changed)
        state.update(mutatedSourceSha256=digest(changed), mutationApplied=True)
        yield
    finally:
        # Restore original bytes even when validation, build, report parsing, or a signal fails.
        if applied:
            path.write_bytes(original)
        state["originalBytesRestored"] = path.read_bytes() == original
        if not state["originalBytesRestored"]:
            raise ProofFailure("Original source bytes could not be restored.")


def parse_trx(path: Path, recipe: Recipe) -> tuple[list[dict], dict]:
    tree = ET.parse(path).getroot()
    definitions = {}
    for unit in tree.findall("./{*}TestDefinitions/{*}UnitTest"):
        method = unit.find("{*}TestMethod")
        if method is None or unit.get("id") in definitions:
            raise ProofFailure("Missing or duplicate TRX test definition.")
        definitions[unit.get("id")] = (method.get("className", "") + "." + method.get("name", ""), method.get("codeBase"))
    rows, execution_ids, binaries = [], set(), set()
    for result in tree.findall("./{*}Results/{*}UnitTestResult"):
        execution_id = result.get("executionId")
        definition = definitions.get(result.get("testId"))
        if not execution_id or execution_id in execution_ids or not definition or definition[0] != recipe.test:
            raise ProofFailure("Foreign, duplicate, or undefined TRX execution.")
        execution_ids.add(execution_id)
        binaries.add(definition[1])
        error = result.find("{*}Output/{*}ErrorInfo")
        rows.append({"name": result.get("testName"), "status": result.get("outcome", "").lower(),
            "message": "" if error is None else error.findtext("{*}Message", ""),
            "stack": "" if error is None else error.findtext("{*}StackTrace", "")})
    counters = tree.find("./{*}ResultSummary/{*}Counters")
    if counters is None or int(counters.get("total", "-1")) != len(rows) or int(counters.get("executed", "-1")) != len(rows):
        raise ProofFailure("TRX counts do not reconcile with observed executions.")
    observed = Counter(row["status"] for row in rows)
    if any(int(counters.get(kind, "0")) for kind in ("error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
            "notRunnable", "notExecuted", "disconnected", "inProgress", "pending")):
        raise ProofFailure("TRX contains unfinished, skipped, or infrastructure outcomes.")
    if any(info.get("outcome", "").lower() in ("error", "failed", "aborted")
            for info in tree.findall("./{*}ResultSummary/{*}RunInfos/{*}RunInfo")):
        raise ProofFailure("TRX contains a run-level failure in addition to assertions.")
    for kind in ("passed", "failed"):
        if int(counters.get(kind, "-1")) != observed[kind]:
            raise ProofFailure("TRX verdict counters disagree with executions.")
    if len(definitions) != len(rows) or len(binaries) != 1 or not next(iter(binaries), None):
        raise ProofFailure("TRX definitions or test binary identity are incomplete.")
    return rows, {"rawCounters": dict(counters.attrib), "testBinary": next(iter(binaries))}


def parse_vitest(path: Path, recipe: Recipe) -> tuple[list[dict], dict]:
    report = json.loads(path.read_text())
    rows = []
    for suite in report.get("testResults", []):
        # Vitest's name-filtered report includes pending unselected cases. Retain but never count them as executions.
        for assertion in suite.get("assertionResults", []):
            name, status = assertion.get("fullName"), assertion.get("status")
            if name not in recipe.names():
                if status not in ("pending", "skipped", "todo"):
                    raise ProofFailure("Vitest executed a foreign test.")
                continue
            if Path(suite.get("name", "")).name != Path(recipe.assertion_file).name:
                raise ProofFailure("Vitest selected test belongs to a foreign file.")
            failure = "\n".join(assertion.get("failureMessages", []))
            rows.append({"name": name, "status": status, "message": failure, "stack": failure})
    if report.get("numRuntimeErrorTestSuites", 0) or report.get("testExecError"):
        raise ProofFailure("Vitest reported a setup/runtime suite error.")
    observed = Counter(row["status"] for row in rows)
    if report.get("numPassedTests") != observed["passed"] or report.get("numFailedTests") != observed["failed"]:
        raise ProofFailure("Vitest verdict counters disagree with selected executions.")
    return rows, {"rawCounters": {key: value for key, value in report.items() if key.startswith("num")}}


def assess(recipe: Recipe, phase: str, exit_code: int, rows: list[dict]) -> dict:
    names = [row["name"] for row in rows]
    if Counter(names) != Counter(recipe.names()):
        raise ProofFailure("Zero, missing, duplicate, or foreign selected tests; this is not a causal bite.")
    if any(row["status"] not in ("passed", "failed") for row in rows):
        raise ProofFailure("A selected test was skipped or did not execute.")
    failures = {row["name"] for row in rows if row["status"] == "failed"}
    expected = set(recipe.failed_names()) if phase == "mutated" else set()
    if failures != expected or (phase == "mutated" and exit_code != 1) or (phase != "mutated" and exit_code != 0):
        raise ProofFailure("Command exit or exact failing theory rows differ from the recipe; not a bite.")
    frame = re.compile(re.escape(Path(recipe.assertion_file).name) + r"(?::line\s+|:)" + str(recipe.assertion_line) + r"(?:\D|$)")
    for row in rows:
        if row["status"] == "failed" and (not re.search(recipe.cause, row["message"], re.S | re.I) or not frame.search(row["stack"])):
            raise ProofFailure("Wrong assertion cause or source frame; not a causal bite.")
    return {"executed": len(rows), "passed": len(rows) - len(failures), "failed": len(failures), "skipped": 0,
        "failedNames": sorted(failures), "intendedAssertionConfirmed": phase == "mutated"}


def command(recipe: Recipe, output: Path) -> tuple[list[str], str, Path]:
    if recipe.scope == "dotnet":
        report = output / "results.trx"
        return ["dotnet", "test", recipe.project, "--configuration", "Release", "--no-restore", "--disable-build-servers",
            "-p:UseSharedCompilation=false", "-p:WarningsNotAsErrors=NU1603", "-nodeReuse:false", "--filter", "FullyQualifiedName=" + recipe.test,
            "--logger", "trx;LogFileName=results.trx", "--results-directory", str(output)], ".", report
    report = output / "vitest.json"
    return ["pnpm", "exec", "vitest", "run", "src/app/weaver/__tests__/weaverSupportInventory.test.ts",
        "-t", "withdraws a .* origin before nested callbacks and preserves a healthy resource owner",
        "--reporter=json", "--outputFile=" + str(report)], CLIENT, report


def terminate_group(process: subprocess.Popen) -> dict:
    """Signal only the POSIX process group created by this invocation, including after its leader exits."""
    def exists():
        try:
            os.killpg(process.pid, 0)
            return True
        except ProcessLookupError:
            return False
    state = {"ownedProcessGroup": process.pid, "termSent": False, "killSent": False}
    if exists():
        try:
            os.killpg(process.pid, signal.SIGTERM)
            state["termSent"] = True
        except ProcessLookupError:
            pass
        until = time.monotonic() + 5
        while exists() and time.monotonic() < until:
            process.poll()
            time.sleep(0.05)
        if exists():
            try:
                os.killpg(process.pid, signal.SIGKILL)
                state["killSent"] = True
            except ProcessLookupError:
                pass
    process.wait(timeout=10)
    state["groupStillObserved"] = exists()
    state["detachedDescendantsExcluded"] = False  # setsid/fork escape is not proven by process-group ownership.
    return state


def execute(root: Path, recipe: Recipe, phase: str, output: Path, deadline: float) -> dict:
    output.mkdir()
    argv, cwd, report = command(recipe, output)
    remaining = min(COMMAND_SECONDS, deadline - time.monotonic())
    if remaining <= 0:
        raise ProofFailure("The bounded driver budget is exhausted.")
    data = {"phase": phase, "command": argv, "cwd": str(root / cwd), "timeoutSeconds": remaining, "selectedTest": recipe.test,
        "status": "running", "source": retained(root / recipe.source)}
    metadata = output / "command.json"
    write_json(metadata, data)
    process = None
    try:
        environment = os.environ.copy()
        environment.update(DOTNET_CLI_USE_MSBUILD_SERVER="0", MSBUILDDISABLENODEREUSE="1")
        with (output / "command.log").open("wb") as log:
            try:
                process = subprocess.Popen(argv, cwd=root / cwd, env=environment, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
                data["pid"] = process.pid
                write_json(metadata, data)
                data["exitCode"] = process.wait(timeout=remaining)
            finally:
                if process is not None:
                    data["cleanup"] = {"ownedProcessGroup": process.pid, "status": "unproven"}
                    data["cleanup"] = terminate_group(process)
        if data["cleanup"]["groupStillObserved"]:
            raise ProofFailure("The owned process group is still observed after cleanup.")
        data["rawReport"] = retained(report)
        rows, report_metadata = parse_trx(report, recipe) if recipe.scope == "dotnet" else parse_vitest(report, recipe)
        data.update(report_metadata, observedRows=rows)
        if recipe.scope == "dotnet":
            binary = Path(data["testBinary"]).resolve()
            expected = (root / recipe.project).parent / "bin" / "Release" / "net10.0" / (Path(recipe.project).stem + ".dll")
            if binary != expected.resolve() or not binary.is_relative_to(root):
                raise ProofFailure("TRX does not identify the selected checkout's exact test binary.")
            data["binaries"] = [retained(binary), retained(binary.parent / recipe.owner_dll)]
        data["verdict"] = assess(recipe, phase, data["exitCode"], rows)
        data["status"] = "passed"
    except BaseException as failure:
        data.update(status="failed", failureType=type(failure).__name__)
        if isinstance(failure, ProofFailure):
            data["failure"] = str(failure)
        # Compile/setup errors and missing reports remain raw failed attempts, never expected-red successes.
        if not isinstance(failure, Exception):
            raise
    finally:
        if process is not None:
            data.setdefault("exitCode", process.returncode)
        if report.is_file():
            data["rawReport"] = retained(report)
        if (output / "command.log").is_file():
            data["log"] = retained(output / "command.log")
        write_json(metadata, data)
    return data


def run_recipe(root: Path, head: str, recipe: Recipe, output: Path, deadline: float) -> dict:
    require_clean(root, head)
    output.mkdir()
    original = (root / recipe.source).read_bytes()
    committed = subprocess.check_output(["git", "-c", "core.fsmonitor=false", "show", f"{head}:{recipe.source}"], cwd=root, timeout=30)
    assertion_lines = (root / recipe.assertion_file).read_text().splitlines()
    if original != committed or original.count(recipe.before.encode()) != 1:
        raise ProofFailure("Recipe source differs from the exact clean commit or is ambiguous.")
    if assertion_lines[recipe.assertion_line - 1].strip() != recipe.assertion_text:
        raise ProofFailure("Recipe assertion has moved or changed; source review is required.")
    (output / "source.original").write_bytes(original)
    result = {"id": recipe.id, "status": "failed", "scope": recipe.scope, "source": recipe.source,
        "originalSource": retained(output / "source.original"), "before": recipe.before, "after": recipe.after,
        "test": recipe.test, "expectedNames": recipe.names(), "expectedFailures": recipe.failed_names(),
        "assertion": {"file": recipe.assertion_file, "line": recipe.assertion_line, "causePattern": recipe.cause},
        "testSource": retained(root / recipe.assertion_file), "phases": []}
    path = output / "recipe.json"
    try:
        baseline = execute(root, recipe, "baseline", output / "baseline", deadline)
        result["phases"].append(baseline)
        require_clean(root, head)
        if baseline["status"] != "passed":
            return result
        try:
            with mutation(root, recipe, original, result):
                if git(root, "diff", "--name-only") != recipe.source:
                    raise ProofFailure("Mutation is not confined to its one source file.")
                (output / "source.mutated").write_bytes((root / recipe.source).read_bytes())
                result["mutatedSource"] = retained(output / "source.mutated")
                result["phases"].append(execute(root, recipe, "mutated", output / "mutated", deadline))
        finally:
            require_clean(root, head)
        restored = execute(root, recipe, "restored", output / "restored", deadline)
        result["phases"].append(restored)
        require_clean(root, head)
        if len(result["phases"]) == 3 and all(phase["status"] == "passed" for phase in result["phases"]):
            if recipe.scope == "dotnet":
                identities = [phase["binaries"][1]["sha256"] for phase in result["phases"]]
                result["ownerBinaryHashes"] = identities
                if identities[0] == identities[1] or identities[1] == identities[2]:
                    raise ProofFailure("Mutated owner binary is unchanged; source-to-runtime causality is unproven.")
            result["status"] = "passed"
        return result
    finally:
        result["sourceBytesRestored"] = (root / recipe.source).read_bytes() == original
        write_json(path, result)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--scope", choices=("dotnet", "studio", "all"), default="all")
    parser.add_argument("--output", type=Path, required=True, help="Fresh absolute private evidence directory outside the checkout")
    args = parser.parse_args()
    if os.name != "posix":
        parser.error("This bounded driver requires POSIX owned process groups.")
    root = Path(__file__).resolve().parents[3]
    head = git(root, "rev-parse", "HEAD")
    try:
        require_expected_head(head, os.environ.get("WEAVER_CAUSAL_EXPECTED_HEAD"))
    except ProofFailure as failure:
        parser.error(str(failure))
    require_clean(root, head)
    output = args.output
    if not output.is_absolute() or output != output.resolve() or output.is_relative_to(root) or root.is_relative_to(output):
        parser.error("--output must be a canonical fresh absolute directory outside the checkout")
    output.mkdir(parents=True, exist_ok=False)
    report = {"status": "failed", "source": head, "tree": git(root, "rev-parse", "HEAD^{tree}"), "scope": args.scope,
        "evidenceCategory": "scoped-source-causal-controls", "liveProvider": False, "recipes": [],
        "limits": ["Not whole-suite, discovery, composed, package, live-provider, or resulting-main qualification.",
            "Detached descendants escaping the owned POSIX group are not proven absent.",
            "SIGKILL, host loss, or power loss can prevent Python finally restoration; use a disposable checkout."]}
    write_json(output / "owner.json", {"source": head, "pid": os.getpid(), "purpose": "weaver-3831-causal-proof"})
    lock = Path(git(root, "rev-parse", "--git-path", "weaver-causal-proof.lock"))
    if not lock.is_absolute():
        lock = root / lock
    token = f"{os.getpid()}:{head}:{output}"
    acquired = False
    old_term = signal.getsignal(signal.SIGTERM)
    def stop(_signum, _frame):
        raise KeyboardInterrupt("Causal proof received termination; restoring source.")
    try:
        with lock.open("x") as handle:
            handle.write(token)
        acquired = True
        signal.signal(signal.SIGTERM, stop)
        deadline = time.monotonic() + TOTAL_SECONDS
        selected = [recipe for recipe in RECIPES if args.scope in ("all", recipe.scope)]
        report["expectedRecipeIds"] = [recipe.id for recipe in selected]
        for recipe in selected:
            report["recipes"].append(run_recipe(root, head, recipe, output / recipe.id, deadline))
            write_json(output / "report.json", report)
        require_clean(root, head)
        report["status"] = "passed" if all(item["status"] == "passed" for item in report["recipes"]) else "failed"
    except BaseException as failure:
        report["failureType"] = type(failure).__name__
        if isinstance(failure, ProofFailure):
            report["failure"] = str(failure)
    finally:
        report["unfinishedRecipeIds"] = [identifier for identifier in report.get("expectedRecipeIds", [])
            if identifier not in {item["id"] for item in report["recipes"]}]
        report["sourceCleanAtExit"] = git(root, "rev-parse", "HEAD") == head and not git(root, "status", "--porcelain", "--untracked-files=normal")
        if not report["sourceCleanAtExit"]:
            report["status"] = "failed"
        write_json(output / "report.json", report)
        if acquired and lock.read_text() == token:
            lock.unlink()
        signal.signal(signal.SIGTERM, old_term)
    print(json.dumps({"status": report["status"], "report": str(output / "report.json"), "source": head}))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
