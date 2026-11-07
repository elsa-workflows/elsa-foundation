#!/usr/bin/env python3
"""Qualify one trusted, externally assigned runner; never report required CI checks."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time

REPOSITORY = "elsa-workflows/elsa-foundation"
WORKFLOW = f"{REPOSITORY}/.github/workflows/ci-azure-pilot.yml@refs/heads/main"
ASSIGNMENT = Path("/etc/elsa-ci-pilot/assignment.json")
ROOT = Path(__file__).resolve().parents[2]
COMMANDS = [
    [sys.executable, "-m", "unittest", "discover", "-s", "tools/ci", "-p", "test_azure_runner_pilot.py"],
    [sys.executable, "-m", "unittest", "discover", "-s", "tools/ci", "-p", "test_workflow_scope.py"],
    [sys.executable, "-m", "unittest", "discover", "-s", "tools/ci", "-p", "test_studio_gate.py"],
    [sys.executable, "tools/ci/test_tiers.py", "--self-test"],
    [sys.executable, "tools/ci/test_tiers.py", "check-workflows"],
    [sys.executable, "tools/ci/test_tiers.py", "check-docs"],
]


def workflow_errors(text: str) -> list[str]:
    """Check parsed workflow fields, accepting only the dependency-free JSON/YAML subset."""
    def unique_mapping(pairs):
        mapping = {}
        for key, value in pairs:
            if key in mapping:
                raise ValueError("Duplicate workflow key")
            mapping[key] = value
        return mapping

    def reject_constant(value):
        raise ValueError("Non-JSON constant")

    try:
        document = json.loads(text, object_pairs_hook=unique_mapping, parse_constant=reject_constant)
        job = document["jobs"]["pilot"]
        steps = [{key: value for key, value in step.items() if key != "name"} for step in job["steps"]]
        inputs = {key: {field: value for field, value in item.items() if field != "description"}
                  for key, item in document["on"]["workflow_dispatch"]["inputs"].items()}
        expected_steps = [
            {"uses": "actions/checkout@d23441a48e516b6c34aea4fa41551a30e30af803",
             "with": {"ref": "${{ github.sha }}", "persist-credentials": False}},
            {"env": {"PILOT_EXPECTED_SHA": "${{ inputs.trusted_sha }}", "PILOT_RUNNER_LABEL": "${{ inputs.runner_label }}"},
             "run": 'python3 tools/ci/azure_runner_pilot.py --report-dir "$RUNNER_TEMP/ci-azure-pilot"'},
            {"if": "always()", "uses": "actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02",
             "with": {"name": "ci-azure-pilot-${{ github.run_id }}-${{ github.run_attempt }}",
                      "path": "${{ runner.temp }}/ci-azure-pilot", "if-no-files-found": "error", "retention-days": 7}},
        ]
        fields = [
            ("workflow keys", sorted(document), ["concurrency", "jobs", "name", "on", "permissions"]),
            ("workflow name", document["name"], "CI Azure runner pilot"),
            ("triggers", sorted(document["on"]), ["workflow_dispatch"]),
            ("dispatch keys", sorted(document["on"]["workflow_dispatch"]), ["inputs"]),
            ("dispatch inputs", inputs, {key: {"required": True, "type": "string"} for key in ["trusted_sha", "runner_label"]}),
            ("permissions", document["permissions"], {"contents": "read"}),
            ("concurrency", document["concurrency"], {"group": "ci-azure-runner-pilot", "cancel-in-progress": False}),
            ("jobs", sorted(document["jobs"]), ["pilot"]),
            ("job keys", sorted(job), ["if", "runs-on", "steps", "timeout-minutes"]),
            ("main gate", job["if"], f"github.repository == '{REPOSITORY}' && github.event_name == 'workflow_dispatch' && github.ref == 'refs/heads/main' && github.event.repository.fork == false"),
            ("runner policy", job["runs-on"], {"group": "elsa-foundation-ci-pilot", "labels": "${{ inputs.runner_label }}"}),
            ("job bound", job["timeout-minutes"], 10),
            ("steps", steps, expected_steps),
        ]
        # JSON comparison distinguishes booleans from integers as well as comparing
        # complete objects: additional permissions/steps/inputs cannot hide here.
        return [f"Unexpected {name}" for name, actual, expected in fields
                if json.dumps(actual, sort_keys=True) != json.dumps(expected, sort_keys=True)]
    except (ValueError, TypeError, KeyError, AttributeError):
        return ["Invalid structured workflow"]


def context_errors(env: dict, event: dict, assignment: dict, actual_sha: str) -> list[str]:
    expected = {
        "GITHUB_REPOSITORY": REPOSITORY,
        "GITHUB_REPOSITORY_ID": "1232994340",
        "GITHUB_EVENT_NAME": "workflow_dispatch",
        "GITHUB_REF": "refs/heads/main",
        "GITHUB_WORKFLOW_REF": WORKFLOW,
        "GITHUB_RUN_ATTEMPT": "1",
        "RUNNER_ENVIRONMENT": "self-hosted",
    }
    errors = [f"Unexpected {key}" for key, value in expected.items() if env.get(key) != value]
    sha = env.get("GITHUB_SHA", "")
    if not re.fullmatch(r"[0-9a-f]{40}", sha) or any(value != sha for value in [
        actual_sha, env.get("PILOT_EXPECTED_SHA"), env.get("GITHUB_WORKFLOW_SHA"), assignment.get("sha")
    ]):
        errors.append("Source/workflow/assignment SHA mismatch")
    label = env.get("PILOT_RUNNER_LABEL", "")
    if not re.fullmatch(r"elsa-ci-pilot-[0-9a-f]{32}", label) or assignment.get("label") != label:
        errors.append("Invalid or mismatched one-job label")
    if assignment.get("run_id") != env.get("GITHUB_RUN_ID") or not re.fullmatch(r"[0-9]+", env.get("GITHUB_RUN_ID", "")):
        errors.append("Unexpected workflow run")
    repository = event.get("repository", {})
    if repository.get("full_name") != REPOSITORY or repository.get("id") != 1232994340 or repository.get("fork") is not False:
        errors.append("Unexpected event repository")
    if event.get("ref") != "refs/heads/main" or event.get("inputs") != {"trusted_sha": sha, "runner_label": label}:
        errors.append("Unexpected dispatch ref or inputs")
    return errors


def load_assignment() -> dict:
    for path, expected_type in [(ASSIGNMENT.parent, stat.S_ISDIR), (ASSIGNMENT, stat.S_ISREG)]:
        info = path.lstat()
        if not expected_type(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
            raise ValueError("Assignment and parent must be root-owned without group/other write access or symlinks")
    return json.loads(ASSIGNMENT.read_text())


def qualify(report_dir: Path) -> int:
    report_dir.mkdir(parents=True, exist_ok=False)
    report = {
        "scope": "source-only runner pilot; not product, browser, required-context or performance acceptance",
        "commands": [{"argv": argv, "status": "not_run", "exit_code": None, "seconds": None} for argv in COMMANDS],
        "errors": [],
    }
    result = 1
    try:
        actual_sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
        assignment = load_assignment()
        event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text())
        report["errors"] = context_errors(dict(os.environ), event, assignment, actual_sha)
        report.update({"sha": actual_sha, "run_id": os.environ.get("GITHUB_RUN_ID"), "runner_name": os.environ.get("RUNNER_NAME"), "assignment": {key: assignment.get(key) for key in ("sha", "label", "run_id")}})
        if report["errors"]:
            return result
        for index, argv in enumerate(COMMANDS):
            attempt = report["commands"][index]
            attempt["status"] = "starting"
            started = time.monotonic()
            try:
                with (report_dir / f"{index}.stdout").open("w") as stdout, (report_dir / f"{index}.stderr").open("w") as stderr:
                    process = subprocess.run(argv, cwd=ROOT, stdout=stdout, stderr=stderr, timeout=60, check=False)
                attempt.update(status="passed" if process.returncode == 0 else "failed", exit_code=process.returncode)
            except subprocess.TimeoutExpired:
                attempt.update(status="timed_out", timeout_seconds=60)
                raise
            except OSError:
                attempt["status"] = "start_failed"
                raise
            finally:
                attempt["seconds"] = time.monotonic() - started
            if process.returncode:
                return result
        result = 0
        return result
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        report["errors"].append(type(error).__name__)
        return result
    finally:
        report["exit_code"] = result
        report["files"] = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(report_dir.iterdir()) if path.is_file()}
        (report_dir / "proof.json").write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report-dir", type=Path, required=True)
    sys.exit(qualify(parser.parse_args().report_dir))
