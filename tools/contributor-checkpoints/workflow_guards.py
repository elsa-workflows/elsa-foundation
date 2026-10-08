"""Small shared guards for metadata-only contributor checkpoints."""

from __future__ import annotations

from datetime import datetime
import json
import os
import re
import sys
from typing import Any


_CHECKPOINT_TAG = re.compile(
    r"contributor-preview-(?P<date>[0-9]{4}-[0-9]{2}-[0-9]{2})\.[1-9][0-9]*"
)
_RELEASE_ACTIONS = {"prereleased", "published"}
_PACKAGES_EVENTS = {"push", "workflow_dispatch"}


def is_metadata_only_checkpoint(action: str, prerelease: str, tag_name: str) -> bool:
    """Accept only the reserved prerelease event/tag pair."""
    match = _CHECKPOINT_TAG.fullmatch(tag_name)
    if action not in _RELEASE_ACTIONS or prerelease != "true" or match is None:
        return False
    try:
        datetime.strptime(match.group("date"), "%Y-%m-%d")
    except ValueError:
        return False
    return True


def docker_event_admitted(event_name: str, source_event: str, conclusion: str) -> bool:
    """Keep direct PR/manual builds and admit only successful push/manual Packages runs."""
    if event_name in {"pull_request", "workflow_dispatch"}:
        return True
    return (
        event_name == "workflow_run"
        and source_event in _PACKAGES_EVENTS
        and conclusion == "success"
    )


def latest_packages_run(requested_sha: str, runs: list[dict[str, Any]]) -> str | None:
    """Choose the newest eligible Packages run for the exact main SHA."""
    if re.fullmatch(r"[0-9a-f]{40}", requested_sha) is None:
        return None

    candidates: list[tuple[datetime, int, str]] = []
    for run in runs:
        if (
            run.get("event") not in _PACKAGES_EVENTS
            or run.get("conclusion") != "success"
            or run.get("head_branch") != "main"
            or run.get("head_sha") != requested_sha
        ):
            continue

        run_id = run.get("id")
        started = run.get("run_started_at")
        if not isinstance(run_id, int) or not isinstance(started, str):
            continue
        try:
            started_at = datetime.fromisoformat(started.replace("Z", "+00:00"))
        except ValueError:
            continue
        if started_at.tzinfo is None:
            continue
        candidates.append((started_at, run_id, str(run_id)))

    return max(candidates)[2] if candidates else None


def _append_output(name: str, value: str) -> None:
    output_path = os.environ.get("GITHUB_OUTPUT")
    if not output_path:
        raise RuntimeError("GITHUB_OUTPUT is not set")
    with open(output_path, "a", encoding="utf-8") as output:
        output.write(f"{name}={value}\n")


def main(argv: list[str]) -> int:
    if argv == ["release"]:
        if is_metadata_only_checkpoint(
            os.environ.get("RELEASE_ACTION", ""),
            os.environ.get("RELEASE_PRERELEASE", ""),
            os.environ.get("RELEASE_TAG_NAME", ""),
        ):
            print("::notice title=Metadata-only contributor checkpoint::No packages or images are published by this release event.")
            return 0

        print(
            "::error title=Releases publish nothing yet::Packages from a GitHub Release are not published until "
            "the 4.0 release cut is built (#2085, https://github.com/elsa-workflows/elsa-foundation/issues/2085). "
            "Previews publish from main."
        )
        return 1

    if argv == ["docker-admission"]:
        admitted = docker_event_admitted(
            os.environ.get("GITHUB_EVENT_NAME", ""),
            os.environ.get("WORKFLOW_RUN_EVENT", ""),
            os.environ.get("WORKFLOW_RUN_CONCLUSION", ""),
        )
        _append_output("allowed", "true" if admitted else "false")
        print("Docker source event admitted." if admitted else "Docker source event has no image-build path.")
        return 0

    if argv == ["resolve-packages-run"]:
        requested_sha = os.environ.get("REQUESTED_COMMIT", "")
        try:
            runs = json.load(sys.stdin)
        except json.JSONDecodeError:
            return 2
        if not isinstance(runs, list):
            return 2
        selected = latest_packages_run(requested_sha, runs)
        if selected is None:
            return 1
        print(selected)
        return 0

    print("Usage: workflow_guards.py release|docker-admission|resolve-packages-run", file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
