"""Inert retry controls. Fake dotnet writes TRX; no provider process starts."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import run_shard
import test_results
import test_tiers


class RetryPolicyTests(unittest.TestCase):
    def run_case(self, policy=False, outcome="Failed", broken=None):
        suite = {"suite": "cli-acceptance", "project": "fake.csproj", "require": "FAKE_REQUIRED"}
        if policy is not None:
            suite["retryOnFailure"] = policy
        calls = []
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            manifest = root / "manifest.json"
            manifest.write_text(json.dumps({"projects": [], "quarantine": []}))
            argv = ["run_shard.py", "--entry", json.dumps({"shard": "fake", "suites": [suite]}),
                    "--out", str(root / "out"), "--head", "test-head", "--manifest", str(manifest),
                    "--parallel", "1"]

            def fake_dotnet(arguments, **kwargs):
                self.assertEqual(arguments[:2], ["dotnet", "test"])
                self.assertEqual(kwargs["env"]["FAKE_REQUIRED"], "1")
                self.assertIn("--no-build", arguments)
                calls.append(arguments)
                target = Path(arguments[arguments.index("--results-directory") + 1])
                if broken == "missing":
                    pass
                elif broken == "malformed":
                    target.mkdir(parents=True)
                    (target / "bad.trx").write_text("<broken")
                else:
                    actual = outcome if len(calls) == 1 else "Passed"
                    test_results._trx(str(target), [("Fake.Native", "Leg", actual, "00:00:01")],
                                      aborted=broken == "aborted")
                return subprocess.CompletedProcess(arguments, 0 if outcome == "Passed" else 1)

            with patch.object(sys, "argv", argv), patch.object(run_shard.subprocess, "run", fake_dotnet):
                result = run_shard.main()
            recorded = json.loads((root / "out" / "runs.json").read_text())
            self.assertNotIn("env", recorded[0])
            outcomes = json.loads((root / "out" / "test-outcomes.json").read_text())
            return result, calls, outcomes

    def test_native_failure_one_child_and_red(self):
        code, calls, result = self.run_case()
        self.assertEqual((code, len(calls)), (1, 1))
        self.assertNotIn("--filter", calls[0])
        self.assertEqual(result["failedTwice"], ["Fake.Native.Leg"])
        self.assertEqual(result["passedOnRetry"], [])

    def test_single_attempt_refuses_prior_results_before_any_child(self):
        for attempt in ("try1", "try2"):
            with self.subTest(attempt=attempt), tempfile.TemporaryDirectory() as directory:
                root = Path(directory)
                out = root / "out"
                test_results._trx(str(out / attempt / "0"),
                                  [("Fake.Native", "Leg", "Passed", "00:00:01")])
                entry = {"shard": "fake", "suites": [{"suite": "cli-acceptance",
                         "project": "fake.csproj", "retryOnFailure": False}]}
                argv = ["run_shard.py", "--entry", json.dumps(entry), "--out", str(out), "--head", "current"]
                with patch.object(sys, "argv", argv), patch.object(run_shard.subprocess, "run") as child:
                    with self.assertRaises(SystemExit) as refused:
                        run_shard.main()
                    self.assertEqual(refused.exception.code, 2)
                    child.assert_not_called()

    def test_native_pass_one_child(self):
        code, calls, _ = self.run_case(outcome="Passed")
        self.assertEqual((code, len(calls)), (0, 1))

    def test_omitted_and_true_keep_focused_retry(self):
        for policy in [None, True]:
            with self.subTest(policy=policy):
                code, calls, result = self.run_case(policy=policy)
                self.assertEqual((code, len(calls)), (0, 2))
                self.assertEqual(calls[1][-2:], ["--filter", "FullyQualifiedName=Fake.Native.Leg"])
                self.assertEqual(result["passedOnRetry"], ["Fake.Native.Leg"])

    def test_bad_policy_refuses_before_dotnet(self):
        for value in [0, 1, "false", None, [], {}]:
            with self.subTest(value=value), patch.object(run_shard.subprocess, "run") as child:
                with self.assertRaises(ValueError):
                    run_shard.runs_of({"suites": [{"suite": "cli", "project": "fake", "retryOnFailure": value}]})
                child.assert_not_called()

    def test_incomplete_primary_trx_stays_red(self):
        for broken in ["missing", "malformed", "aborted"]:
            with self.subTest(broken=broken):
                code, calls, result = self.run_case(outcome="Passed", broken=broken)
                self.assertEqual((code, len(calls)), (1, 1))
                self.assertTrue(result["aborted"])

    def test_actual_matrix_preserves_native_single_attempt(self):
        manifest = test_tiers.load(Path(__file__).with_name("test-tiers.json").read_text())
        entries = test_tiers.select(manifest, "full")["container"]["include"]
        native = [(entry, suite) for entry in entries for suite in entry["suites"]
                  if suite["suite"] == "cli-acceptance"]
        self.assertEqual(len(native), 1)
        entry, suite = native[0]
        self.assertIs(suite["retryOnFailure"], False)
        runs = run_shard.runs_of(entry)
        cli = next(run for run in runs if run["group"] == "cli-acceptance")
        self.assertEqual(cli["env"], {"ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX": "1"})
        for other in runs:
            if other is not cli:
                self.assertTrue(other.get("retryOnFailure", True))

    def test_synthetic_neighbor_has_its_own_required_mode(self):
        runs = run_shard.runs_of({"suites": [
            {"suite": "cli", "project": "cli", "require": "CLI_REQUIRED", "retryOnFailure": False},
            {"suite": "other", "project": "other", "require": "OTHER_REQUIRED"}]})
        self.assertEqual(runs[0]["env"], {"CLI_REQUIRED": "1"})
        self.assertEqual(runs[1]["env"], {"OTHER_REQUIRED": "1"})
        self.assertIs(runs[1]["retryOnFailure"], True)


class DiscoveryRetentionTests(unittest.TestCase):
    def run_case(self, entry, *, retain=True, discovery_exit=0, initial_outcome="Passed", start_error=False,
                 discovery_timeout=False):
        calls = []
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            out = root / "out"
            manifest = root / "manifest.json"
            manifest.write_text(json.dumps({"projects": [], "quarantine": []}))
            argv = ["run_shard.py", "--entry", json.dumps(entry), "--out", str(out),
                    "--head", "a" * 40, "--manifest", str(manifest), "--parallel", "1"]
            if retain:
                argv.append("--retain-discovery")

            def fake_dotnet(arguments, **kwargs):
                calls.append((arguments, kwargs["env"].copy()))
                self.assertIn("--no-build", arguments)
                if "--list-tests" in arguments:
                    self.assertEqual(kwargs["timeout"], run_shard.DISCOVERY_TIMEOUT_SECONDS)
                    if start_error:
                        raise FileNotFoundError("private-launch-path")
                    kwargs["stdout"].write(b"exact discovery\r\n\xff\n")
                    kwargs["stderr"].write(b"exact diagnostic\r\n")
                    if discovery_timeout:
                        raise subprocess.TimeoutExpired(arguments, kwargs["timeout"])
                    return subprocess.CompletedProcess(arguments, discovery_exit)
                target = Path(arguments[arguments.index("--results-directory") + 1])
                outcome = initial_outcome if target.parent.name == "try1" else "Passed"
                test_results._trx(str(target), [("Fake.Native", "Leg", outcome, "00:00:01")])
                return subprocess.CompletedProcess(arguments, 0 if outcome == "Passed" else 1)

            with patch.object(sys, "argv", argv), patch.object(run_shard.subprocess, "run", fake_dotnet), \
                    patch.dict(run_shard.os.environ, {"INHERITED_SECRET": "private-inherited-value",
                                                     run_shard.SECRETS_CONNECTION: "private-connection-value"}):
                code = run_shard.main()
            retained = []
            for receipt in sorted((out / "discovery").glob("*/receipt.json")):
                retained.append((json.loads(receipt.read_text()),
                                 receipt.with_name("stdout.log").read_bytes(),
                                 receipt.with_name("stderr.log").read_bytes()))
            outcomes = json.loads((out / "test-outcomes.json").read_text())
            trx_paths = sorted(str(path.relative_to(out)) for path in out.glob("try*/*/*.trx"))
            return code, calls, retained, outcomes, trx_paths

    def test_discovery_retains_exact_bytes_and_matches_each_initial_run_without_recording_environment_values(self):
        entry = {"shard": "fake", "split": [{"project": "picked.csproj", "group": "picked",
                 "filter": "FullyQualifiedName~Fake"}], "suites": [
                 {"suite": "native", "project": "native.csproj", "require": "FAKE_REQUIRED"}],
                 "secrets": True, "secretsProject": "secrets.csproj"}
        code, calls, retained, _, trx_paths = self.run_case(entry)
        self.assertEqual(code, 0)
        self.assertEqual((len(calls), len(retained), len(trx_paths)), (6, 3, 3))
        for index, (receipt, stdout, stderr) in enumerate(retained):
            discovery, discovery_env = calls[index]
            execution, execution_env = calls[index + 3]
            expected = execution[:7] + ["--list-tests"]
            if "--filter" in execution:
                expected += execution[execution.index("--filter"):]
            self.assertEqual(discovery, expected)
            self.assertEqual(discovery_env, execution_env)
            self.assertEqual(discovery_env["INHERITED_SECRET"], "private-inherited-value")
            self.assertEqual(receipt["command"], discovery)
            self.assertEqual(receipt["project"], execution[2])
            self.assertEqual(receipt["headSha"], "a" * 40)
            self.assertEqual((receipt["shard"], receipt["runIndex"], receipt["exitCode"]), ("fake", index, 0))
            self.assertEqual(stdout, b"exact discovery\r\n\xff\n")
            self.assertEqual(stderr, b"exact diagnostic\r\n")
            self.assertNotIn("private-", json.dumps(receipt))
            self.assertNotIn("env", receipt)
        self.assertEqual(retained[0][0]["filter"], "FullyQualifiedName~Fake")
        self.assertEqual(retained[1][0]["requiredVariableNames"], ["FAKE_REQUIRED"])
        self.assertEqual(calls[1][1]["FAKE_REQUIRED"], "1")
        self.assertEqual(retained[2][0]["requiredVariableNames"], [])
        self.assertEqual(calls[2][1][run_shard.SECRETS_CONNECTION], "private-connection-value")

    def test_failed_discovery_keeps_passing_tests_red_and_preserves_trx(self):
        code, calls, retained, outcomes, trx_paths = self.run_case(
            {"shard": "fake", "whole": ["fake.csproj"]}, discovery_exit=17)
        self.assertEqual((code, len(calls), len(trx_paths)), (1, 2, 1))
        self.assertEqual(retained[0][0]["exitCode"], 17)
        self.assertEqual(retained[0][1], b"exact discovery\r\n\xff\n")
        self.assertEqual(outcomes["failedTwice"], [])

    def test_launch_failure_retains_safe_receipt_and_fails_shard(self):
        code, _, retained, _, _ = self.run_case({"shard": "fake", "whole": ["fake.csproj"]}, start_error=True)
        self.assertEqual(code, 1)
        receipt, stdout, stderr = retained[0]
        self.assertIsNone(receipt["exitCode"])
        self.assertEqual(receipt["errorKind"], "FileNotFoundError")
        self.assertNotIn("private-launch-path", json.dumps(receipt))
        self.assertEqual((stdout, stderr), (b"", b""))

    def test_discovery_timeout_retains_partial_bytes_and_keeps_passing_tests_red(self):
        code, calls, retained, outcomes, trx_paths = self.run_case(
            {"shard": "fake", "whole": ["fake.csproj"]}, discovery_timeout=True)
        self.assertEqual((code, len(calls), len(trx_paths)), (1, 2, 1))
        receipt, stdout, stderr = retained[0]
        self.assertIsNone(receipt["exitCode"])
        self.assertEqual(receipt["errorKind"], "TimeoutExpired")
        self.assertEqual(receipt["timeoutSeconds"], 600)
        self.assertEqual(stdout, b"exact discovery\r\n\xff\n")
        self.assertEqual(stderr, b"exact diagnostic\r\n")
        self.assertEqual(outcomes["failedTwice"], [])

    def test_discovery_failure_is_not_cleared_by_successful_test_retry(self):
        code, calls, retained, outcomes, trx_paths = self.run_case(
            {"shard": "fake", "whole": ["fake.csproj"]}, discovery_exit=17, initial_outcome="Failed")
        self.assertEqual((code, len(calls), len(retained), len(trx_paths)), (1, 3, 1, 2))
        self.assertEqual(outcomes["passedOnRetry"], ["Fake.Native.Leg"])

    def test_discovery_does_not_repeat_for_focused_retry(self):
        code, calls, retained, outcomes, trx_paths = self.run_case(
            {"shard": "fake", "whole": ["fake.csproj"]}, initial_outcome="Failed")
        self.assertEqual((code, len(calls), len(retained), len(trx_paths)), (0, 3, 1, 2))
        self.assertEqual(calls[-1][0][-2:], ["--filter", "FullyQualifiedName=Fake.Native.Leg"])
        self.assertEqual(outcomes["passedOnRetry"], ["Fake.Native.Leg"])

    def test_discovery_is_opt_in(self):
        code, calls, retained, _, _ = self.run_case({"shard": "fake", "whole": ["fake.csproj"]}, retain=False)
        self.assertEqual((code, len(calls), retained), (0, 1, []))
        self.assertNotIn("--list-tests", calls[0][0])


if __name__ == "__main__":
    unittest.main()
