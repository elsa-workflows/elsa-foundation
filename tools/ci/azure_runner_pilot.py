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
    report = {"scope": "source-only runner pilot; not product, browser, required-context or performance acceptance", "commands": [], "errors": []}
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
            started = time.monotonic()
            with (report_dir / f"{index}.stdout").open("w") as stdout, (report_dir / f"{index}.stderr").open("w") as stderr:
                process = subprocess.run(argv, cwd=ROOT, stdout=stdout, stderr=stderr, timeout=60, check=False)
            report["commands"].append({"argv": argv, "exit_code": process.returncode, "seconds": time.monotonic() - started})
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
