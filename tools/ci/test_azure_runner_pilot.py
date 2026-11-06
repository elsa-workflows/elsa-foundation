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
            self.assertEqual(1, report["exit_code"])
            self.assertNotIn("unexpected_private_field", report["assignment"])

    def test_workflow_keeps_trust_and_failure_contract(self):
        text = (pilot.ROOT / ".github/workflows/ci-azure-pilot.yml").read_text()
        for required in ["  workflow_dispatch:", "github.repository == 'elsa-workflows/elsa-foundation'", "github.event_name == 'workflow_dispatch'", "github.ref == 'refs/heads/main'", "github.event.repository.fork == false", "ref: ${{ github.sha }}", "group: elsa-foundation-ci-pilot", "contents: read", "persist-credentials: false", "timeout-minutes: 10", "if-no-files-found: error", "if: always()", "actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803", "actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02"]:
            self.assertIn(required, text)
        for prohibited in ["pull_request:", "pull_request_target:", "push:", "schedule:", "secrets.", "contents: write", "continue-on-error", "Build & test", "Generated maps fresh", "name: Full tier"]:
            self.assertNotIn(prohibited, text)


if __name__ == "__main__":
    unittest.main()
