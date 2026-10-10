"""Focused fixtures for the helpers used by Packages and Docker."""

import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest

import workflow_guards as guards


SCRIPT = Path(guards.__file__)
SHA = "a" * 40


def run_helper(command: str, env: dict[str, str], payload: str | None = None) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(SCRIPT), command],
        env=os.environ | env,
        input=payload,
        capture_output=True,
        text=True,
        check=False,
    )


def packages_run(run_id: int, event: str, started: str, **overrides: object) -> dict[str, object]:
    return {
        "id": run_id,
        "event": event,
        "conclusion": "success",
        "head_branch": "main",
        "head_sha": SHA,
        "run_started_at": started,
        **overrides,
    }


class WorkflowGuardTests(unittest.TestCase):
    def test_release_matrix_and_event_text_as_data(self) -> None:
        cases = (
            ("prereleased", "true", "contributor-preview-2026-10-07.1", True),
            ("published", "true", "contributor-preview-2026-10-07.2", True),
            ("published", "true", "v4.0.0-preview", False),
            ("published", "false", "contributor-preview-2026-10-07.1", False),
            ("prereleased", "true", "contributor-preview-2026-10-07.0", False),
            ("prereleased", "true", "contributor-preview-2026-10-07.01", False),
            ("prereleased", "true", "contributor-preview-2026-99-07.1", False),
            ("released", "true", "contributor-preview-2026-10-07.1", False),
            ("", "true", "contributor-preview-2026-10-07.1", False),
        )
        for action, prerelease, tag, expected in cases:
            with self.subTest(action=action, prerelease=prerelease, tag=tag):
                self.assertEqual(expected, guards.is_metadata_only_checkpoint(action, prerelease, tag))

        with tempfile.TemporaryDirectory() as temporary:
            marker = Path(temporary) / "unexpected"
            refused = run_helper("release", {
                "RELEASE_ACTION": "published",
                "RELEASE_PRERELEASE": "true",
                "RELEASE_TAG_NAME": f"$(touch {marker})",
            })
            accepted = run_helper("release", {
                "RELEASE_ACTION": "published",
                "RELEASE_PRERELEASE": "true",
                "RELEASE_TAG_NAME": "contributor-preview-2026-10-07.1",
            })
            ordinary = run_helper("release", {
                "RELEASE_ACTION": "published",
                "RELEASE_PRERELEASE": "false",
                "RELEASE_TAG_NAME": "v4.0.0",
            })
            self.assertEqual(1, refused.returncode)
            self.assertFalse(marker.exists())
            self.assertEqual(0, accepted.returncode)
            self.assertEqual(1, ordinary.returncode)
            self.assertIn("Packages from a GitHub Release are not published", ordinary.stdout)
            self.assertIn("#2085", ordinary.stdout)

    def test_docker_admission_cli_matrix_preserves_direct_paths(self) -> None:
        cases = [("workflow_run", event, result, event in {"push", "workflow_dispatch"} and result == "success")
                 for event in ("push", "workflow_dispatch", "release")
                 for result in ("success", "failure", "skipped")]
        cases += [("pull_request", "", "", True), ("workflow_dispatch", "", "", True), ("push", "", "success", False)]
        for name, source, conclusion, expected in cases:
            with self.subTest(name=name, source=source, conclusion=conclusion), tempfile.TemporaryDirectory() as temporary:
                output = Path(temporary) / "github-output"
                result = run_helper("docker-admission", {
                    "GITHUB_EVENT_NAME": name,
                    "WORKFLOW_RUN_EVENT": source,
                    "WORKFLOW_RUN_CONCLUSION": conclusion,
                    "GITHUB_OUTPUT": str(output),
                })
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual(expected, output.read_text().strip() == "allowed=true")

    def test_manual_resolver_selects_newest_exact_sha_eligible_run(self) -> None:
        runs = [
            packages_run(100, "push", "2026-10-06T10:00:00Z"),
            packages_run(200, "release", "2026-10-07T10:00:00Z"),
            packages_run(150, "workflow_dispatch", "2026-10-06T12:00:00Z"),
            packages_run(201, "push", "2026-10-07T11:00:00Z", head_sha="b" * 40),
            packages_run(202, "push", "2026-10-07T12:00:00Z", conclusion="failure"),
            packages_run(203, "push", "2026-10-07T13:00:00Z", head_branch="feature"),
        ]
        result = run_helper("resolve-packages-run", {"REQUESTED_COMMIT": SHA}, json.dumps(runs))
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("150", result.stdout.strip())

        none = run_helper("resolve-packages-run", {"REQUESTED_COMMIT": SHA}, json.dumps([runs[1]]))
        invalid_sha = run_helper("resolve-packages-run", {"REQUESTED_COMMIT": "main"}, "[]")
        self.assertEqual(1, none.returncode)
        self.assertEqual(1, invalid_sha.returncode)

    def test_workflow_wiring_and_removal_mutant(self) -> None:
        repo = SCRIPT.parents[2]
        packages = (repo / ".github/workflows/packages.yml").read_text()
        docker = (repo / ".github/workflows/docker.yml").read_text()
        ci = (repo / ".github/workflows/ci.yml").read_text()
        self.assertIn("run: python3 tools/contributor-checkpoints/workflow_guards.py release", packages)
        self.assertIn("RELEASE_TAG_NAME: ${{ github.event.release.tag_name }}", packages)
        self.assertIn("if: ${{ github.event_name != 'release' }}", packages)
        self.assertIn("github.event_name == 'push' || github.event_name == 'workflow_dispatch'", packages)
        self.assertIn("run: python3 tools/contributor-checkpoints/workflow_guards.py docker-admission", docker)
        self.assertIn("tools/contributor-checkpoints/**", docker)
        self.assertIn("event=push&head_sha=$commit", docker)
        self.assertIn("event=workflow_dispatch&head_sha=$commit", docker)
        self.assertIn("format('ineligible-{0}', github.run_id)", docker)
        self.assertIn("python3 tools/contributor-checkpoints/test_workflow_guards.py", ci)

        contract = lambda text: (
            "needs: admission" in text
            and "if: needs.admission.outputs.allowed == 'true'" in text
            and len(re.findall(r"(?m)^    needs: versions$", text)) == 2
        )
        self.assertTrue(contract(docker))
        self.assertFalse(contract(docker.replace("if: needs.admission.outputs.allowed == 'true'", "if: true", 1)))
        self.assertFalse(contract(docker.replace("    needs: versions\n", "    needs: admission\n", 1)))


if __name__ == "__main__":
    unittest.main()
