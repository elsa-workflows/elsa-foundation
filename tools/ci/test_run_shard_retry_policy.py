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


if __name__ == "__main__":
    unittest.main()
