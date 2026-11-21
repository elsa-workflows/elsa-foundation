import copy
import json
import stat
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

import azure_runner_pilot as pilot


class PilotTests(unittest.TestCase):
    def setUp(self):
        self.sha = "a" * 40
        self.label = "elsa-ci-pilot-" + "b" * 32
        self.env = {"GITHUB_REPOSITORY": pilot.REPOSITORY, "GITHUB_REPOSITORY_ID": "1232994340", "GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_REF": "refs/heads/main", "GITHUB_WORKFLOW_REF": pilot.WORKFLOW, "GITHUB_RUN_ATTEMPT": "1", "RUNNER_ENVIRONMENT": "self-hosted", "GITHUB_SHA": self.sha, "GITHUB_WORKFLOW_SHA": self.sha, "PILOT_EXPECTED_SHA": self.sha, "PILOT_RUNNER_LABEL": self.label, "GITHUB_RUN_ID": "123"}
        self.event = {"repository": {"full_name": pilot.REPOSITORY, "id": 1232994340, "fork": False}, "ref": "refs/heads/main", "inputs": {"trusted_sha": self.sha, "runner_label": self.label}}
        self.assignment = {"sha": self.sha, "label": self.label, "run_id": "123"}

    def test_exact_assignment_passes(self):
        self.assertEqual([], pilot.context_errors(self.env, self.event, self.assignment, self.sha))

    def test_untrusted_contexts_rejected(self):
        for key, value in {"GITHUB_REPOSITORY": "attacker/fork", "GITHUB_REPOSITORY_ID": "1", "GITHUB_EVENT_NAME": "pull_request", "GITHUB_REF": "refs/pull/1/merge", "GITHUB_WORKFLOW_REF": pilot.WORKFLOW.replace("main", "branch"), "GITHUB_RUN_ATTEMPT": "2", "RUNNER_ENVIRONMENT": "github-hosted", "GITHUB_SHA": "c" * 40, "GITHUB_WORKFLOW_SHA": "d" * 40, "PILOT_EXPECTED_SHA": "e" * 40, "PILOT_RUNNER_LABEL": "self-hosted", "GITHUB_RUN_ID": "456"}.items():
            with self.subTest(key=key):
                env = dict(self.env, **{key: value})
                self.assertTrue(pilot.context_errors(env, self.event, self.assignment, self.sha))

    def test_fork_or_altered_dispatch_rejected(self):
        for field, value in [("fork", True), ("id", 9), ("full_name", "attacker/fork")]:
            event = copy.deepcopy(self.event)
            event["repository"][field] = value
            self.assertTrue(pilot.context_errors(self.env, event, self.assignment, self.sha))
        for field, value in [("ref", "refs/heads/branch"), ("inputs", {})]:
            event = dict(self.event, **{field: value})
            self.assertTrue(pilot.context_errors(self.env, event, self.assignment, self.sha))

    def test_checkout_and_external_assignment_are_required(self):
        self.assertTrue(pilot.context_errors(self.env, self.event, self.assignment, "f" * 40))
        for key in self.assignment:
            assignment = dict(self.assignment)
            assignment.pop(key)
            self.assertTrue(pilot.context_errors(self.env, self.event, assignment, self.sha))

    def test_context_failure_stops_all_guard_commands_and_retains_failure(self):
        with tempfile.TemporaryDirectory() as temporary, patch.object(pilot.subprocess, "check_output", return_value=self.sha), patch.object(pilot, "load_assignment", return_value=self.assignment), patch.object(pilot.json, "loads", return_value=self.event), patch.dict(pilot.os.environ, {"GITHUB_EVENT_PATH": __file__}), patch.object(pilot, "context_errors", return_value=["untrusted"]), patch.object(pilot.subprocess, "run") as run:
            directory = Path(temporary) / "proof"
            self.assertEqual(1, pilot.qualify(directory))
            run.assert_not_called()
            self.assertIn('"exit_code": 1', (directory / "proof.json").read_text())

    def test_assignment_rejects_non_root_writable_and_symlink_files(self):
        parent = SimpleNamespace(st_mode=stat.S_IFDIR | 0o755, st_uid=0)
        for uid, mode in [(501, stat.S_IFREG | 0o444), (0, stat.S_IFREG | 0o664), (0, stat.S_IFLNK | 0o777)]:
            file = SimpleNamespace(st_mode=mode, st_uid=uid)
            with self.subTest(uid=uid, mode=mode), patch.object(pilot.Path, "lstat", side_effect=[parent, file]):
                with self.assertRaises(ValueError):
                    pilot.load_assignment()
        with patch.object(pilot.Path, "lstat", return_value=SimpleNamespace(st_mode=stat.S_IFDIR | 0o777, st_uid=0)):
            with self.assertRaises(ValueError):
                pilot.load_assignment()
        file = SimpleNamespace(st_mode=stat.S_IFREG | 0o444, st_uid=0)
        with patch.object(pilot.Path, "lstat", side_effect=[parent, file]), patch.object(pilot.Path, "read_text", return_value=json.dumps(self.assignment)):
            self.assertEqual(self.assignment, pilot.load_assignment())

    def test_failed_command_stops_without_retry_and_retains_actual_exit_code(self):
        self.assignment["unexpected_private_field"] = "must-not-be-in-public-artifact"
        with tempfile.TemporaryDirectory() as temporary, patch.object(pilot.subprocess, "check_output", return_value=self.sha), patch.object(pilot, "load_assignment", return_value=self.assignment), patch.dict(pilot.os.environ, {"GITHUB_EVENT_PATH": __file__}), patch.object(pilot.Path, "read_text", return_value=json.dumps(self.event)), patch.object(pilot, "context_errors", return_value=[]), patch.object(pilot.subprocess, "run") as run:
            run.return_value.returncode = 4
            directory = Path(temporary) / "proof"
            self.assertEqual(1, pilot.qualify(directory))
            run.assert_called_once()
            # Bypass the input fixture's read_text patch to inspect the actual report.
            report = json.loads((directory / "proof.json").read_bytes())
            self.assertEqual(4, report["commands"][0]["exit_code"])
            self.assertEqual("failed", report["commands"][0]["status"])
            self.assertTrue(all(item["status"] == "not_run" for item in report["commands"][1:]))
            self.assertEqual(1, report["exit_code"])
            self.assertNotIn("unexpected_private_field", report["assignment"])

    def attempt_failure(self, failure):
        with tempfile.TemporaryDirectory() as temporary:
            event_path = Path(temporary) / "event.json"
            event_path.write_text(json.dumps(self.event))
            directory = Path(temporary) / "proof"
            env = dict(self.env, GITHUB_EVENT_PATH=str(event_path))
            with patch.object(pilot.subprocess, "check_output", return_value=self.sha), patch.object(pilot, "load_assignment", return_value=self.assignment), patch.dict(pilot.os.environ, env), patch.object(pilot.subprocess, "run", side_effect=failure) as run:
                self.assertEqual(1, pilot.qualify(directory))
                run.assert_called_once()
            report = json.loads((directory / "proof.json").read_text())
            self.assertEqual(len(pilot.COMMANDS), len(report["commands"]))
            self.assertEqual(pilot.COMMANDS[0], report["commands"][0]["argv"])
            self.assertGreaterEqual(report["commands"][0]["seconds"], 0)
            self.assertTrue(all(item["status"] == "not_run" for item in report["commands"][1:]))
            self.assertEqual({"0.stdout", "0.stderr"}, set(report["files"]))
            return report

    def test_timeout_records_attempt_and_leaves_later_commands_unrun(self):
        report = self.attempt_failure(pilot.subprocess.TimeoutExpired(pilot.COMMANDS[0], 60))
        self.assertEqual("timed_out", report["commands"][0]["status"])
        self.assertEqual(60, report["commands"][0]["timeout_seconds"])
        self.assertIsNone(report["commands"][0]["exit_code"])
        self.assertEqual(["TimeoutExpired"], report["errors"])

    def test_start_failure_records_attempt_without_inventing_exit_code(self):
        report = self.attempt_failure(FileNotFoundError("missing executable"))
        self.assertEqual("start_failed", report["commands"][0]["status"])
        self.assertIsNone(report["commands"][0]["exit_code"])
        self.assertEqual(["FileNotFoundError"], report["errors"])

    def test_workflow_keeps_trust_and_failure_contract(self):
        text = (pilot.ROOT / ".github/workflows/ci-azure-pilot.yml").read_text()
        self.assertEqual([], pilot.workflow_errors(text))

    def test_caller_checkout_rejected_despite_inert_immutable_ref_text(self):
        document = json.loads((pilot.ROOT / ".github/workflows/ci-azure-pilot.yml").read_text())
        checkout = document["jobs"]["pilot"]["steps"][0]
        checkout["with"]["ref"] = "${{ inputs.trusted_sha }}"
        checkout["name"] = "ref: ${{ github.sha }}"
        self.assertTrue(pilot.workflow_errors(json.dumps(document)))

    def test_workflow_comments_duplicate_keys_and_non_json_constants_rejected(self):
        text = (pilot.ROOT / ".github/workflows/ci-azure-pilot.yml").read_text()
        mutants = [
            text.replace('"ref": "${{ github.sha }}"', '# ref: ${{ github.sha }}\n "ref": "${{ inputs.trusted_sha }}"'),
            text.replace('"ref": "${{ github.sha }}"', '"ref": "${{ github.sha }}", "ref": "${{ inputs.trusted_sha }}"'),
            text.replace('"timeout-minutes": 10', '"timeout-minutes": NaN'),
        ]
        for mutant in mutants:
            self.assertNotEqual(text, mutant)
            self.assertTrue(pilot.workflow_errors(mutant))

    def test_structured_workflow_policy_mutations_rejected(self):
        original = json.loads((pilot.ROOT / ".github/workflows/ci-azure-pilot.yml").read_text())
        mutations = [
            (["on", "pull_request"], {}),
            (["permissions", "contents"], "write"),
            (["jobs", "untrusted"], {"runs-on": "self-hosted"}),
            (["jobs", "pilot", "if"], "true"),
            (["jobs", "pilot", "runs-on", "group"], "Default"),
            (["jobs", "pilot", "runs-on", "labels"], "self-hosted"),
            (["jobs", "pilot", "continue-on-error"], True),
            (["jobs", "pilot", "timeout-minutes"], 11),
            (["jobs", "pilot", "steps", 0, "with", "persist-credentials"], True),
            (["jobs", "pilot", "steps", 0, "uses"], "actions/checkout@main"),
            (["jobs", "pilot", "steps", 1, "run"], "true"),
            (["jobs", "pilot", "steps", 2, "if"], "success()"),
            (["jobs", "pilot", "steps", 2, "with", "if-no-files-found"], "warn"),
        ]
        for path, value in mutations:
            with self.subTest(path=path):
                document = copy.deepcopy(original)
                target = document
                for key in path[:-1]:
                    target = target[key]
                target[path[-1]] = value
                self.assertTrue(pilot.workflow_errors(json.dumps(document)))


if __name__ == "__main__":
    unittest.main()
