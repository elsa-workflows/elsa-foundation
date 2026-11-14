#!/usr/bin/env python3
"""Run one test shard: its runs in parallel, one retry of the failed tests, then the verdict (spec 263).

The shard job builds the solution first; this script only runs `dotnet test --no-build`. A shard is one matrix entry
written by test_tiers.py: `whole` projects and `split` class-group runs. Each run is its own `dotnet test` process with
its own results directory, try1/<index>/, so every outcome traces back to the run that produced it. Up to
--parallel runs at a time, which is how `dotnet test` on a solution parallelises projects as well.

An EF container shard's entry lists `suites` instead: each suite is one run with its required-provider variable set
for that run only, never for the job, so one suite's mode cannot leak into the next. Container suites run one at a
time (pass --parallel 1), because each starts its own database containers. With `secrets`, the Secrets EF
composition proof is one more run against the PostgreSQL the job started.

Unless a container suite sets retryOnFailure=false, every test that failed in try1 runs once more, in try2/<index>/
(FR-004). test_results.py then decides the shard; a failed single-attempt test still fails the shard.

With --retain-discovery, each initial run first retains unparsed discovery stdout/stderr and a command/exit receipt
under discovery/<index>/. Discovery uses the same project, configuration, filter and environment as that run, without
building. Discovery has a ten-minute process timeout; failure or timeout fails the shard even if its tests pass.
The actual tests and their retry/outcome path still run. Their verdict is labelled test execution only, followed by
the discovery-aware overall shard verdict. These receipts do not compare case counts.

Usage:
  run_shard.py --entry <matrix-entry-json> --out <dir> --head <sha> [--parallel N] [--manifest <file>]
               [--summary <file>] [--retain-discovery] [--dry-run]
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import test_results  # noqa: E402

HANG_TIMEOUT = "10m"
DISCOVERY_TIMEOUT_SECONDS = 10 * 60
SECRETS_CONNECTION = "ELSA_SECRETS_EF_POSTGRESQL_TEST_CONNECTION_STRING"


def runs_of(entry: dict) -> list[dict]:
    runs = [{"project": project, "group": "", "filter": ""} for project in entry.get("whole", [])]
    runs += [dict(run) for run in entry.get("split", [])]
    for suite in entry.get("suites", []):
        retry_on_failure = suite.get("retryOnFailure", True)
        if type(retry_on_failure) is not bool:
            raise ValueError("suite retryOnFailure must be a boolean")
        runs.append({"project": suite["project"], "group": suite["suite"], "filter": "",
                     "retryOnFailure": retry_on_failure,
                     "env": {suite["require"]: "1"} if suite.get("require") else {}})
    if entry.get("secrets"):
        runs.append({"project": entry["secretsProject"], "group": "secrets-ef-composition", "filter": "",
                     "env": {SECRETS_CONNECTION: os.environ.get(SECRETS_CONNECTION, "")}})
    return runs


def command(run: dict, results: str, filter_expression: str, *, list_tests: bool = False) -> list[str]:
    arguments = ["dotnet", "test", run["project"], "-c", "Release", "--no-build", "-p:WarningsNotAsErrors=NU1603"]
    arguments += ["--list-tests"] if list_tests else [
        "--logger", "trx", "--results-directory", results,
        "--blame-hang-timeout", HANG_TIMEOUT, "--blame-hang-dump-type", "none"]
    if filter_expression:
        arguments += ["--filter", filter_expression]
    return arguments


def retain_discovery(runs: list[dict], output: str, shard: str, head: str) -> bool:
    succeeded = True
    for index, run in enumerate(runs):
        directory = os.path.join(output, "discovery", str(index))
        os.makedirs(directory, exist_ok=True)
        arguments = command(run, directory, run["filter"], list_tests=True)
        env = run.get("env", {})
        receipt = {"schema": 1, "shard": shard, "headSha": head, "runIndex": index,
                   "project": run["project"], "group": run.get("group", ""), "filter": run["filter"],
                   "command": arguments, "requiredVariableNames": sorted(name for name in env if name != SECRETS_CONNECTION),
                   "timeoutSeconds": DISCOVERY_TIMEOUT_SECONDS, "exitCode": None}
        receipt_path = os.path.join(directory, "receipt.json")
        # Clear any previous receipt before starting, so interruption cannot leave an old successful exit.
        with open(receipt_path, "w", encoding="utf-8") as handle:
            json.dump(receipt, handle, indent=2)
        with open(os.path.join(directory, "stdout.log"), "wb") as stdout, \
                open(os.path.join(directory, "stderr.log"), "wb") as stderr:
            try:
                result = subprocess.run(arguments, check=False, env={**os.environ, **env}, stdout=stdout, stderr=stderr,
                                        timeout=DISCOVERY_TIMEOUT_SECONDS)
                receipt["exitCode"] = result.returncode
            except (OSError, subprocess.TimeoutExpired) as error:
                # The exception message can contain private paths; retain only its type.
                receipt["errorKind"] = type(error).__name__
        with open(receipt_path, "w", encoding="utf-8") as handle:
            json.dump(receipt, handle, indent=2)
        if receipt["exitCode"] != 0:
            succeeded = False
            print(f"Discovery failed for run {index}; see its retained receipt.", flush=True)
    return succeeded


def execute(commands: list[tuple[list[str], dict]], parallel: int, dry_run: bool) -> None:
    def one(item: tuple[list[str], dict]) -> None:
        arguments, env = item
        armed = " ".join(f"{name}=1" for name in env if name != SECRETS_CONNECTION)
        print("+ " + (armed + " " if armed else "") + " ".join(arguments), flush=True)
        if not dry_run:
            # The exit code is not the verdict: a failing test exits non-zero and is still retried; the TRX decides.
            subprocess.run(arguments, check=False, env={**os.environ, **env})

    with ThreadPoolExecutor(max_workers=max(1, parallel)) as pool:
        list(pool.map(one, commands))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--entry", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--parallel", type=int, default=os.cpu_count() or 2)
    parser.add_argument("--manifest", default=test_results.MANIFEST)
    parser.add_argument("--summary")
    parser.add_argument("--retain-discovery", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    entry = json.loads(args.entry)
    runs = runs_of(entry)
    for index, run in enumerate(runs):
        if run.get("retryOnFailure", True) is False:
            for attempt in ("try1", "try2"):
                if os.path.lexists(os.path.join(args.out, attempt, str(index))):
                    parser.error("single-attempt suite requires fresh results directories")
    os.makedirs(args.out, exist_ok=True)
    with open(os.path.join(args.out, "runs.json"), "w", encoding="utf-8") as handle:
        # The Secrets connection string stays out of the recorded runs.
        json.dump([{key: value for key, value in run.items() if key != "env"} for run in runs], handle, indent=2)

    discovery_ok = True
    if args.retain_discovery and not args.dry_run:
        discovery_ok = retain_discovery(runs, args.out, entry["shard"], args.head)

    execute([(command(run, os.path.join(args.out, "try1", str(index)), run["filter"]), run.get("env", {}))
             for index, run in enumerate(runs)], args.parallel, args.dry_run)

    retries = []
    for index, run in enumerate(runs):
        if run.get("retryOnFailure", True) is False:
            print(f"Single-attempt suite: no automatic retry for {run['project']}", flush=True)
            continue
        retry = test_results.retry_filter(test_results.read_trx(os.path.join(args.out, "try1", str(index))).failed())
        if retry:
            retries.append((command(run, os.path.join(args.out, "try2", str(index)), retry), run.get("env", {})))
    if retries:
        print(f"Retrying the failed tests of {len(retries)} run(s) once.", flush=True)
        execute(retries, args.parallel, args.dry_run)

    evaluate = argparse.Namespace(shard_dir=args.out, shard=entry["shard"], head=args.head, manifest=args.manifest,
                                  summary=args.summary, outcomes=os.path.join(args.out, "test-outcomes.json"))
    test_exit = (test_results.evaluate(evaluate, label="Test execution only") if args.retain_discovery
                 else test_results.evaluate(evaluate))
    exit_code = test_exit if test_exit else (0 if discovery_ok else 1)
    if args.retain_discovery:
        discovery_status = "not run (dry-run)" if args.dry_run else ("passed" if discovery_ok else "failed")
        text = (f"### Shard `{entry['shard']}`: {'passed' if exit_code == 0 else 'failed'}\n\n"
                f"- Discovery command: {discovery_status}\n"
                f"- Test execution: {'passed' if test_exit == 0 else 'failed'}\n")
        print(text)
        if args.summary:
            with open(args.summary, "a", encoding="utf-8") as summary:
                summary.write(text)
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
