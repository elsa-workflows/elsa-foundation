#!/usr/bin/env python3
"""Run one test shard: its runs in parallel, one retry of the failed tests, then the verdict (spec 263).

The shard job builds the solution first; this script only runs `dotnet test --no-build`. A shard is one matrix entry
written by test_tiers.py: `whole` projects and `split` class-group runs. Each run is its own `dotnet test` process with
its own results directory, try1/<index>/, so every outcome traces back to the run that produced it. Up to
--parallel runs at a time, which is how `dotnet test` on a solution parallelises projects as well.

Every test that failed in try1 runs once more, in try2/<index>/ (FR-004). test_results.py then decides the shard.

Usage:
  run_shard.py --entry <matrix-entry-json> --out <dir> --head <sha> [--parallel N] [--manifest <file>]
               [--summary <file>] [--dry-run]
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


def runs_of(entry: dict) -> list[dict]:
    return [{"project": project, "group": "", "filter": ""} for project in entry.get("whole", [])] + \
        [dict(run) for run in entry.get("split", [])]


def command(run: dict, results: str, filter_expression: str) -> list[str]:
    arguments = ["dotnet", "test", run["project"], "-c", "Release", "--no-build", "-p:WarningsNotAsErrors=NU1603",
                 "--logger", "trx", "--results-directory", results,
                 "--blame-hang-timeout", HANG_TIMEOUT, "--blame-hang-dump-type", "none"]
    if filter_expression:
        arguments += ["--filter", filter_expression]
    return arguments


def execute(commands: list[list[str]], parallel: int, dry_run: bool) -> None:
    def one(arguments: list[str]) -> None:
        print("+ " + " ".join(arguments), flush=True)
        if not dry_run:
            # The exit code is not the verdict: a failing test exits non-zero and is still retried; the TRX decides.
            subprocess.run(arguments, check=False)

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
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    entry = json.loads(args.entry)
    runs = runs_of(entry)
    os.makedirs(args.out, exist_ok=True)
    with open(os.path.join(args.out, "runs.json"), "w", encoding="utf-8") as handle:
        json.dump(runs, handle, indent=2)

    execute([command(run, os.path.join(args.out, "try1", str(index)), run["filter"]) for index, run in enumerate(runs)],
            args.parallel, args.dry_run)

    retries = []
    for index, run in enumerate(runs):
        retry = test_results.retry_filter(test_results.read_trx(os.path.join(args.out, "try1", str(index))).failed())
        if retry:
            retries.append(command(run, os.path.join(args.out, "try2", str(index)), retry))
    if retries:
        print(f"Retrying the failed tests of {len(retries)} run(s) once.", flush=True)
        execute(retries, args.parallel, args.dry_run)

    evaluate = argparse.Namespace(shard_dir=args.out, shard=entry["shard"], head=args.head, manifest=args.manifest,
                                  summary=args.summary, outcomes=os.path.join(args.out, "test-outcomes.json"))
    return test_results.evaluate(evaluate)


if __name__ == "__main__":
    sys.exit(main())
