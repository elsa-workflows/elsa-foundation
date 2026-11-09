#!/usr/bin/env python3
"""Block a push-triggered Packages run until the same commit's CI push run has succeeded (#4002).

Exit 0 when a completed, successful CI run for the commit exists; exit 1 when every CI run for it has completed
without success (failure, cancellation by a newer push, timeout) or when the wait budget runs out; exit 2 on
invalid input. Cancelled counts as not green: the newer commit's own Packages run publishes everything this one
would have (spec 150 FR-020), so nothing is lost by refusing here.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import time
from typing import Any, Callable
from urllib import parse, request

WORKFLOW_FILE = "ci.yml"
WORKFLOW_PATH = ".github/workflows/ci.yml"
FULL_SHA = re.compile(r"[0-9a-f]{40}\Z")
REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z")


def decide(runs: list[dict[str, Any]], sha: str) -> tuple[str, str]:
    """Return ("success" | "failure" | "wait", reason) for the CI push runs of one commit."""
    relevant = []
    for run in runs:
        path = run.get("path")
        if isinstance(path, str):
            path = path.split("@", 1)[0]
        if run.get("head_sha") != sha or run.get("event") != "push" or path != WORKFLOW_PATH:
            continue
        relevant.append(run)
    if not relevant:
        return "wait", f"No CI push run for {sha[:10]} is visible yet."
    completed = [run for run in relevant if run.get("status") == "completed"]
    if any(run.get("conclusion") == "success" for run in completed):
        run = next(run for run in completed if run.get("conclusion") == "success")
        return "success", f"CI run {run.get('id')} for {sha[:10]} succeeded."
    if len(completed) == len(relevant):
        outcomes = ", ".join(f"{run.get('id')}={run.get('conclusion')}" for run in completed)
        return "failure", f"Every CI push run for {sha[:10]} completed without success: {outcomes}."
    pending = ", ".join(f"{run.get('id')}={run.get('status')}" for run in relevant if run.get("status") != "completed")
    return "wait", f"CI for {sha[:10]} is still running: {pending}."


def fetch_runs(repository: str, sha: str, token: str | None) -> list[dict[str, Any]]:
    query = parse.urlencode({"head_sha": sha, "event": "push", "branch": "main", "per_page": "50"})
    url = f"https://api.github.com/repos/{repository}/actions/workflows/{WORKFLOW_FILE}/runs?{query}"
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "elsa-foundation-await-ci-result"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    with request.urlopen(request.Request(url, headers=headers), timeout=60) as response:
        payload = json.load(response)
    runs = payload.get("workflow_runs") if isinstance(payload, dict) else None
    return runs if isinstance(runs, list) else []


def wait_for_success(
    fetch: Callable[[], list[dict[str, Any]]],
    sha: str,
    *,
    timeout_seconds: float,
    interval_seconds: float,
    clock: Callable[[], float] = time.monotonic,
    sleep: Callable[[float], None] = time.sleep,
    log: Callable[[str], None] = lambda message: print(message, file=sys.stderr),
) -> int:
    deadline = clock() + timeout_seconds
    while True:
        verdict, reason = decide(fetch(), sha)
        log(reason)
        if verdict == "success":
            return 0
        if verdict == "failure":
            return 1
        if clock() >= deadline:
            log(f"Gave up after {int(timeout_seconds)} seconds without a successful CI run for {sha[:10]}.")
            return 1
        sleep(interval_seconds)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--repository", required=True, help="owner/name")
    parser.add_argument("--sha", required=True, help="full lowercase commit SHA-1 the run is building")
    parser.add_argument("--timeout-minutes", type=float, default=100.0)
    parser.add_argument("--interval-seconds", type=float, default=60.0)
    args = parser.parse_args(argv)
    if not REPOSITORY.fullmatch(args.repository):
        print("::error title=Invalid repository::--repository must be owner/name.", file=sys.stderr)
        return 2
    if not FULL_SHA.fullmatch(args.sha):
        print("::error title=Invalid commit::--sha must be a full lowercase SHA-1.", file=sys.stderr)
        return 2
    if args.timeout_minutes <= 0 or args.interval_seconds <= 0:
        print("::error title=Invalid wait::--timeout-minutes and --interval-seconds must be positive.", file=sys.stderr)
        return 2
    token = os.environ.get("GH_TOKEN") or os.environ.get("GITHUB_TOKEN")
    code = wait_for_success(
        lambda: fetch_runs(args.repository, args.sha, token),
        args.sha,
        timeout_seconds=args.timeout_minutes * 60,
        interval_seconds=args.interval_seconds,
    )
    if code == 1:
        print(f"::error title=CI not green::Refusing to pack or publish {args.sha[:10]}; its CI push run did not succeed.", file=sys.stderr)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
