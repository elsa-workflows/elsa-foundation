#!/usr/bin/env python3
"""Run a kernel-model mutation check and compare its verdict with the documented one (issue #4229, round 5 F).

`Kernel.Sim --mutations` and `--native-mutations` exit with the number of mutations they did not catch. Two kernel
mutations, FenceWithoutPosition and DeleteWithoutCount, are documented as caught only together with the rule that covers
them (prototypes/kernel-model/FINDINGS.md), so a plain exit code of 2 is the expected result, not a failure. This
wrapper runs the command, prints its output and passes only when every mutation that is not caught is on the allowed
list, at least one mutation was caught, and the exit code equals the number of uncaught mutations. It never weakens a
mutation: a new uncaught one, a missing report or a crash fails.

Usage:
  kernel_prototype_gate.py [--allow-uncaught Name,Name] -- <command> [args...]
  kernel_prototype_gate.py --self-test
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys

UNCAUGHT = re.compile(r"^(\w+): NOT CAUGHT\b", re.MULTILINE)
CAUGHT = re.compile(r"^(\w+): CAUGHT\b", re.MULTILINE)


def judge(output: str, exit_code: int, allowed: set[str]) -> list[str]:
    """The reasons this run fails, or an empty list."""
    uncaught = set(UNCAUGHT.findall(output))
    caught = set(CAUGHT.findall(output))
    problems = []
    if not caught:
        problems.append("no mutation was reported as caught: the check did not run or crashed")
    unexpected = sorted(uncaught - allowed)
    if unexpected:
        problems.append("mutations not caught that are not on the allowed list: " + ", ".join(unexpected))
    if exit_code != len(uncaught):
        problems.append(f"exit code {exit_code} does not equal the {len(uncaught)} uncaught mutations reported")
    return problems


def self_test() -> int:
    failures = []

    def check(name: str, ok: bool) -> None:
        if not ok:
            failures.append(name)

    ok = "A: CAUGHT at seed 1\nB: NOT CAUGHT in 10 seeds\nC: NOT CAUGHT in 10 seeds\n"
    check("expected uncaught passes", not judge(ok, 2, {"B", "C"}))
    check("a new uncaught mutation fails", bool(judge(ok, 2, {"B"})))
    check("a wrong exit code fails", bool(judge(ok, 0, {"B", "C"})))
    check("a crash with no report fails", bool(judge("", 134, {"B", "C"})))
    check("everything caught passes", not judge("A: CAUGHT at seed 1\n", 0, set()))
    check("a caught mutation that was allowed to escape passes", not judge("A: CAUGHT at seed 1\n", 0, {"B"}))
    check("indented text is not a verdict", bool(judge("  B: NOT CAUGHT in 1\n", 0, set())))
    for failure in failures:
        print(f"kernel_prototype_gate self-test: {failure}", file=sys.stderr)
    if failures:
        return 1
    print("kernel_prototype_gate self-test: all cases pass")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--allow-uncaught", default="")
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    if args.self_test:
        return self_test()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command:
        parser.error("give the command after --")
    result = subprocess.run(command, capture_output=True, text=True)
    sys.stdout.write(result.stdout)
    sys.stderr.write(result.stderr)
    allowed = {name for name in args.allow_uncaught.split(",") if name}
    problems = judge(result.stdout, result.returncode, allowed)
    for problem in problems:
        print(f"::error::{problem}", file=sys.stderr)
    if not problems:
        print(f"kernel_prototype_gate: verdict matches ({len(UNCAUGHT.findall(result.stdout))} documented uncaught)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
