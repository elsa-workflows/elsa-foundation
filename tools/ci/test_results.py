#!/usr/bin/env python3
"""Decide a test shard's result from its TRX files: one retry, the quarantine list, slow tests (spec 263).

A shard runs each of its runs (a project, or one class group of a split project) into try1/<run>/. Every test that
failed there is re-run once into try2/<run>/ (FR-004). This script reads both and decides:

  - a test that is not quarantined and failed twice (or failed and did not run again) fails the shard;
  - a test that passed on its retry is reported, and recorded for the flake-tracking issue;
  - a quarantined test's failure is reported with its issue and never fails the shard (FR-012);
  - a test host that aborted, or a run that wrote no TRX, fails the shard and names the run;
  - a PR-tier test slower than slowTestSeconds and not on the allow-list is reported (FR-019), and fails the shard only
    when slowTestMode is "fail" (FR-020).

Usage:
  test_results.py failures <trx-dir>
  test_results.py retry-filter <trx-dir>
  test_results.py evaluate <shard-dir> --shard <name> --head <sha> [--manifest <file>] [--summary <file>] [--outcomes <file>]
  test_results.py check-quarantine [--manifest <file>] [--base <sha>]
  test_results.py record-flakes <artifacts-dir> --run-url <url> --body <file>
  test_results.py --self-test

<shard-dir> holds runs.json (the list of {project, group, filter} the shard ran) and try1/<index>/, try2/<index>/.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from collections.abc import Callable
from dataclasses import dataclass, field

MANIFEST = "tools/ci/test-tiers.json"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
FAILED = {"Failed", "Error", "Timeout", "Aborted"}
TEST_NAME = re.compile(r"^[A-Za-z_][A-Za-z0-9_.+]*$")


@dataclass
class Results:
    """Outcomes of one or more TRX files, by fully qualified test name (theory cases folded together)."""

    outcomes: dict[str, str] = field(default_factory=dict)
    seconds: dict[str, float] = field(default_factory=dict)
    files: int = 0
    aborted: list[str] = field(default_factory=list)

    def failed(self) -> list[str]:
        return sorted(name for name, outcome in self.outcomes.items() if outcome == "Failed")


def _seconds(duration: str | None) -> float:
    if not duration:
        return 0.0
    hours, minutes, seconds = duration.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(seconds)


def read_trx(directory: str) -> Results:
    results = Results()
    if not os.path.isdir(directory):
        return results
    for root, _, files in os.walk(directory):
        for name in sorted(files):
            if not name.endswith(".trx"):
                continue
            path = os.path.join(root, name)
            results.files += 1
            try:
                document = ET.parse(path).getroot()
            except ET.ParseError as error:
                results.aborted.append(f"{name}: unreadable TRX ({error})")
                continue
            methods = {}
            for test in document.iterfind(".//t:UnitTest", NS):
                method = test.find("t:TestMethod", NS)
                if method is not None:
                    methods[test.get("id")] = f"{method.get('className')}.{method.get('name')}"
            for result in document.iterfind(".//t:UnitTestResult", NS):
                test = methods.get(result.get("testId"))
                if test is None:
                    continue
                outcome = "Failed" if result.get("outcome") in FAILED else result.get("outcome", "")
                # A theory passes only when every case passes.
                if results.outcomes.get(test) != "Failed":
                    results.outcomes[test] = outcome
                results.seconds[test] = max(results.seconds.get(test, 0.0), _seconds(result.get("duration")))
            summary = document.find("t:ResultSummary", NS)
            counters = summary.find("t:Counters", NS) if summary is not None else None
            if summary is not None and summary.get("outcome") == "Aborted" or \
                    counters is not None and counters.get("aborted", "0") != "0":
                results.aborted.append(f"{name}: the test run was aborted")
            # Only an Error-level run message signals a host that stopped; Warning and Informational messages carry
            # ordinary test output, which may well contain words like "crashed".
            for info in document.iterfind(".//t:RunInfo", NS):
                text = " ".join("".join(info.itertext()).split())
                if info.get("outcome") == "Error" and ("abort" in text.lower() or "crash" in text.lower()):
                    results.aborted.append(f"{name}: {text[:200]}")
    return results


def retry_filter(failed: list[str]) -> str:
    return "|".join(f"FullyQualifiedName={name}" for name in failed if TEST_NAME.match(name))


@dataclass
class Verdict:
    failed_twice: list[str] = field(default_factory=list)
    passed_on_retry: list[str] = field(default_factory=list)
    quarantined_failed: list[dict] = field(default_factory=list)
    slow: list[dict] = field(default_factory=list)
    aborted: list[str] = field(default_factory=list)
    slow_fails: bool = False

    @property
    def ok(self) -> bool:
        return not self.failed_twice and not self.aborted and not (self.slow_fails and self.slow)


def decide(manifest: dict, runs: list[dict], first: list[Results], second: list[Results]) -> Verdict:
    verdict = Verdict()
    quarantine = {entry["test"]: entry["issue"] for entry in manifest.get("quarantine", [])}
    allowed = {entry["test"] for entry in manifest.get("slowAllowList", [])}
    pr_projects = {entry["project"] for entry in manifest.get("projects", []) if entry.get("tier") == "pr"}
    threshold = float(manifest.get("slowTestSeconds", 10))
    verdict.slow_fails = manifest.get("slowTestMode", "report") == "fail"

    for run, try1, try2 in zip(runs, first, second):
        label = run["project"] + (f"#{run['group']}" if run.get("group") else "")
        if try1.files == 0:
            verdict.aborted.append(f"{label}: wrote no TRX, so the test host did not finish")
        verdict.aborted.extend(f"{label}: {reason}" for reason in try1.aborted)
        for test in try1.failed():
            if test in quarantine:
                verdict.quarantined_failed.append({"test": test, "issue": quarantine[test]})
            elif try2.outcomes.get(test) == "Passed":
                verdict.passed_on_retry.append(test)
            else:
                verdict.failed_twice.append(test)
        if run["project"] in pr_projects:
            verdict.slow.extend({"test": test, "seconds": round(seconds, 1)}
                                for test, seconds in sorted(try1.seconds.items())
                                if seconds > threshold and test not in allowed)
    return verdict


def summary_markdown(shard: str, verdict: Verdict, repo_url: str) -> str:
    lines = [f"### Shard `{shard}`: {'passed' if verdict.ok else 'failed'}", ""]

    def section(title: str, items: list[str]) -> None:
        if items:
            lines.extend([f"**{title}**", "", *[f"- {item}" for item in items], ""])

    section("Failed twice", [f"`{t}`" for t in verdict.failed_twice])
    section("Did not finish", verdict.aborted)
    section("Passed on retry (recorded on the flake tracker)", [f"`{t}`" for t in verdict.passed_on_retry])
    section("Quarantined, failed (not counted)",
            [f"`{q['test']}`: [#{q['issue']}]({repo_url}/issues/{q['issue']})" for q in verdict.quarantined_failed])
    section(f"Slower than the limit ({'fails' if verdict.slow_fails else 'report only'})",
            [f"`{s['test']}`: {s['seconds']} s" for s in verdict.slow])
    return "\n".join(lines) + "\n"


def evaluate(args: argparse.Namespace) -> int:
    with open(args.manifest, encoding="utf-8") as handle:
        manifest = json.load(handle)
    with open(os.path.join(args.shard_dir, "runs.json"), encoding="utf-8") as handle:
        runs = json.load(handle)
    first = [read_trx(os.path.join(args.shard_dir, "try1", str(index))) for index in range(len(runs))]
    second = [read_trx(os.path.join(args.shard_dir, "try2", str(index))) for index in range(len(runs))]
    verdict = decide(manifest, runs, first, second)
    repo_url = f"{os.environ.get('GITHUB_SERVER_URL', 'https://github.com')}/{os.environ.get('GITHUB_REPOSITORY', 'elsa-workflows/elsa-foundation')}"
    text = summary_markdown(args.shard, verdict, repo_url)
    print(text)
    if args.summary:
        with open(args.summary, "a", encoding="utf-8") as summary:
            summary.write(text)
    if args.outcomes:
        with open(args.outcomes, "w", encoding="utf-8") as outcomes:
            json.dump({
                "schema": 1, "shard": args.shard, "headSha": args.head,
                "failedTwice": verdict.failed_twice, "passedOnRetry": verdict.passed_on_retry,
                "quarantinedFailed": verdict.quarantined_failed, "slow": verdict.slow,
                "aborted": verdict.aborted or None,
            }, outcomes, indent=2)
    return 0 if verdict.ok else 1


# Quarantine guard (FR-013) -------------------------------------------------------------------------------------------

class UnreadableBase(Exception):
    pass


def base_entries(sha: str, run: Callable[[list[str]], subprocess.CompletedProcess]) -> dict[str, int] | None:
    """The quarantine entries of the manifest at <sha>; None when the file is not in that commit."""
    if run(["git", "cat-file", "-e", f"{sha}^{{commit}}"]).returncode != 0:
        # The base may be a revision like <merge>^1; the fetch hint names the commit to fetch.
        commit = sha.split("^", 1)[0]
        raise UnreadableBase(f"commit {sha} is not in this clone; fetch its history first (git fetch origin {commit})")
    if run(["git", "cat-file", "-e", f"{sha}:{MANIFEST}"]).returncode != 0:
        return None
    shown = run(["git", "show", f"{sha}:{MANIFEST}"])
    if shown.returncode != 0:
        raise UnreadableBase(f"could not read {MANIFEST} at {sha}: {shown.stderr.strip()}")
    try:
        return {entry["test"]: entry["issue"] for entry in json.loads(shown.stdout).get("quarantine", [])}
    except (json.JSONDecodeError, KeyError, TypeError) as error:
        raise UnreadableBase(f"{MANIFEST} at {sha} is not a manifest: {error}") from error


def to_check(entries: list[dict], base: dict[str, int] | None) -> list[dict]:
    if base is None:
        return entries
    return [entry for entry in entries if base.get(entry["test"]) != entry["issue"]]


def issue_state(number: int, run: Callable[[list[str]], subprocess.CompletedProcess]) -> str | None:
    result = run([os.environ.get("GH", "gh"), "issue", "view", str(number), "--json", "state", "--jq", ".state"])
    return result.stdout.strip() if result.returncode == 0 else None


def check_quarantine(manifest: dict, base_sha: str | None,
                     run: Callable[[list[str]], subprocess.CompletedProcess]) -> tuple[int, list[str]]:
    entries = manifest.get("quarantine", [])
    try:
        base = base_entries(base_sha, run) if base_sha else None
    except UnreadableBase as error:
        return 2, [f"check-quarantine: {error}"]
    problems = []
    for entry in to_check(entries, base):
        state = issue_state(entry["issue"], run)
        if state != "OPEN":
            problems.append(f"Quarantine entry `{entry['test']}` points at #{entry['issue']}, which is "
                            f"{'missing' if state is None else state.lower()}. Remove the entry if the test is fixed, "
                            f"or reopen the issue if it is still flaky.")
    return (1 if problems else 0), problems


# Flake record (FR-004) ----------------------------------------------------------------------------------------------

SHA = re.compile(r"^[0-9a-f]{40}$")
SHARD = re.compile(r"^[a-z0-9][a-z0-9-]{0,63}$")


def flake_comment(artifacts_dir: str, run_url: str) -> str | None:
    """A comment for the flake tracker from a run's test-outcomes artifacts; None when nothing passed on retry.

    The artifacts come from a run that may have started from a fork, so they are data only: every name must match a
    strict pattern before it is written, and anything that does not is dropped and logged.
    """
    rows, head = [], None
    for root, _, files in os.walk(artifacts_dir):
        for name in sorted(files):
            if name != "test-outcomes.json":
                continue
            try:
                with open(os.path.join(root, name), encoding="utf-8") as handle:
                    data = json.load(handle)
                shard, sha, retried = data["shard"], data["headSha"], data.get("passedOnRetry") or []
            except (OSError, json.JSONDecodeError, KeyError, TypeError) as error:
                print(f"record-flakes: skipping an unreadable outcome file: {error}", file=sys.stderr)
                continue
            if not (isinstance(shard, str) and SHARD.match(shard) and isinstance(sha, str) and SHA.match(sha)):
                print("record-flakes: skipping an outcome file with an invalid shard or SHA", file=sys.stderr)
                continue
            head = head or sha
            for test in retried:
                if isinstance(test, str) and TEST_NAME.match(test):
                    rows.append(f"| `{test}` | {shard} |")
                else:
                    print("record-flakes: skipping an invalid test name", file=sys.stderr)
    if not rows:
        return None
    return "\n".join([f"Passed on retry in [this run]({run_url}) at `{head}`:", "", "| Test | Shard |", "|---|---|",
                      *sorted(set(rows)), ""])


def _run(command: list[str]) -> subprocess.CompletedProcess:
    return subprocess.run(command, capture_output=True, text=True)


# Self-test -----------------------------------------------------------------------------------------------------------

def _trx(directory: str, tests: list[tuple[str, str, str, str]], aborted: bool = False, run_info: str = "") -> None:
    """Writes a minimal TRX: tests are (class, method, outcome, duration)."""
    os.makedirs(directory, exist_ok=True)
    unit_tests = "".join(f'<UnitTest id="{i}" name="{c}.{m}" storage="x.dll"><TestMethod className="{c}" name="{m}"/></UnitTest>'
                         for i, (c, m, _, _) in enumerate(tests))
    results = "".join(f'<UnitTestResult testId="{i}" testName="{c}.{m}" outcome="{o}" duration="{d}"/>'
                      for i, (c, m, o, d) in enumerate(tests))
    outcome = "Aborted" if aborted else "Completed"
    with open(os.path.join(directory, "r.trx"), "w", encoding="utf-8") as handle:
        handle.write(f'<TestRun xmlns="{NS["t"]}"><Results>{results}</Results><TestDefinitions>{unit_tests}</TestDefinitions>'
                     f'<ResultSummary outcome="{outcome}"><Counters aborted="0"/><RunInfos>{run_info}</RunInfos></ResultSummary></TestRun>')


def self_test() -> int:
    failures: list[str] = []

    def check(name: str, condition: bool) -> None:
        if not condition:
            failures.append(name)

    manifest = {
        "slowTestSeconds": 10, "slowTestMode": "report",
        "projects": [{"project": "p/A.csproj", "tier": "pr"}, {"project": "p/W.csproj", "tier": "integration"}],
        "quarantine": [{"test": "A.Flaky.Known", "issue": 42}],
        "slowAllowList": [{"test": "A.Slow.Allowed", "reason": "x"}],
    }
    runs = [{"project": "p/A.csproj", "group": "", "filter": ""}, {"project": "p/W.csproj", "group": "", "filter": ""}]
    quick, slow, slower = "00:00:00.1", "00:00:12.0", "00:00:30.0"

    with tempfile.TemporaryDirectory() as tmp:
        def case(name: str, a1, a2, w1=None, w2=None, aborted=False) -> Verdict:
            root = os.path.join(tmp, name)
            for index, (one, two) in enumerate([(a1, a2), (w1 if w1 is not None else [("W.T", "Ok", "Passed", quick)], w2)]):
                if one is not None:
                    _trx(os.path.join(root, "try1", str(index)), one, aborted=aborted and index == 0)
                if two is not None:
                    _trx(os.path.join(root, "try2", str(index)), two)
            first = [read_trx(os.path.join(root, "try1", str(i))) for i in range(2)]
            second = [read_trx(os.path.join(root, "try2", str(i))) for i in range(2)]
            return decide(manifest, runs, first, second)

        v = case("pass", [("A.T", "Ok", "Passed", quick)], None)
        check("pass: shard passes", v.ok and not v.passed_on_retry)

        v = case("retry", [("A.T", "Flip", "Failed", quick)], [("A.T", "Flip", "Passed", quick)])
        check("fail then pass: shard passes and the test is reported", v.ok and v.passed_on_retry == ["A.T.Flip"])

        v = case("twice", [("A.T", "Bad", "Failed", quick)], [("A.T", "Bad", "Failed", quick)])
        check("fail twice: shard fails", not v.ok and v.failed_twice == ["A.T.Bad"])

        v = case("notrerun", [("A.T", "Bad", "Failed", quick)], None)
        check("fail and no retry result: shard fails", not v.ok and v.failed_twice == ["A.T.Bad"])

        v = case("quarantine", [("A.Flaky", "Known", "Failed", quick)], [("A.Flaky", "Known", "Failed", quick)])
        check("quarantined fail: shard passes and names the issue",
              v.ok and v.quarantined_failed == [{"test": "A.Flaky.Known", "issue": 42}])

        v = case("theory", [("A.T", "Th", "Passed", quick), ("A.T", "Th", "Failed", quick)],
                 [("A.T", "Th", "Passed", quick), ("A.T", "Th", "Passed", quick)])
        check("theory: one failing case fails the method; a passing retry clears it", v.ok and v.passed_on_retry == ["A.T.Th"])
        check("theory: retry filter names the method without arguments", retry_filter(["A.T.Th"]) == "FullyQualifiedName=A.T.Th")
        check("retry filter refuses unsafe names", retry_filter(["A.T.Th", "bad name|x"]) == "FullyQualifiedName=A.T.Th")

        v = case("aborted", [("A.T", "Ok", "Passed", quick)], None, aborted=True)
        check("aborted run: shard fails", not v.ok and v.aborted)
        noisy = os.path.join(tmp, "noisy")
        _trx(noisy, [("A.T", "Ok", "Passed", quick)],
             run_info='<RunInfo outcome="Warning"><Text>A_crashed_incumbent_is_displaced [SKIP]</Text></RunInfo>')
        check("a warning that mentions a crash is not an abort", not read_trx(noisy).aborted)
        crashed = os.path.join(tmp, "crashed")
        _trx(crashed, [("A.T", "Ok", "Passed", quick)],
             run_info='<RunInfo outcome="Error"><Text>The active test run was aborted. Reason: Test host process crashed</Text></RunInfo>')
        check("an error that the host crashed is an abort", bool(read_trx(crashed).aborted))

        v = case("missing", None, None)
        check("run with no TRX: shard fails", not v.ok and any("wrote no TRX" in a for a in v.aborted))

        v = case("slow", [("A.Slow", "Allowed", "Passed", slower), ("A.Slow", "Other", "Passed", slow)], None,
                 w1=[("W.T", "Long", "Passed", slower)])
        check("slow: PR-tier test over the limit is reported, allow-listed and integration tests are not",
              v.slow == [{"test": "A.Slow.Other", "seconds": 12.0}])
        check("slow: report mode does not fail the shard", v.ok)
        manifest["slowTestMode"] = "fail"
        v = case("slowfail", [("A.Slow", "Other", "Passed", slow)], None)
        check("slow: fail mode fails the shard", not v.ok)
        manifest["slowTestMode"] = "report"

    # Quarantine guard with a stubbed git and gh.
    def stub(base_manifest: str | None, states: dict[int, str], commit_present: bool = True):
        def run(command: list[str]) -> subprocess.CompletedProcess:
            if command[:3] == ["git", "cat-file", "-e"]:
                ok = commit_present if command[3].endswith("^{commit}") else base_manifest is not None
                return subprocess.CompletedProcess(command, 0 if ok else 1, "", "")
            if command[:2] == ["git", "show"]:
                return subprocess.CompletedProcess(command, 0, base_manifest or "", "")
            number = int(command[3])
            return subprocess.CompletedProcess(command, 0 if number in states else 1, states.get(number, ""), "")
        return run

    entry = {"test": "A.Flaky.Known", "issue": 42}
    head = {"quarantine": [entry]}
    base_same = json.dumps({"quarantine": [entry]})
    check("quarantine: unchanged entry with a closed issue passes with --base",
          check_quarantine(head, "b" * 40, stub(base_same, {42: "CLOSED"}))[0] == 0)
    check("quarantine: new entry with a closed issue fails with --base",
          check_quarantine(head, "b" * 40, stub(json.dumps({"quarantine": []}), {42: "CLOSED"}))[0] == 1)
    check("quarantine: manifest absent at base means every entry is new",
          check_quarantine(head, "b" * 40, stub(None, {42: "CLOSED"}))[0] == 1)
    check("quarantine: closed issue fails without --base", check_quarantine(head, None, stub(None, {42: "CLOSED"}))[0] == 1)
    check("quarantine: open issue passes without --base", check_quarantine(head, None, stub(None, {42: "OPEN"}))[0] == 0)
    check("quarantine: missing issue fails", check_quarantine(head, None, stub(None, {}))[0] == 1)
    check("quarantine: unreadable base commit exits 2",
          check_quarantine(head, "b" * 40, stub(base_same, {42: "OPEN"}, commit_present=False))[0] == 2)

    with tempfile.TemporaryDirectory() as tmp:
        def outcome(folder: str, data: object) -> None:
            os.makedirs(os.path.join(tmp, folder))
            with open(os.path.join(tmp, folder, "test-outcomes.json"), "w", encoding="utf-8") as handle:
                handle.write(data if isinstance(data, str) else json.dumps(data))
        outcome("a", {"shard": "pr-1", "headSha": "a" * 40, "passedOnRetry": ["A.T.Flip", "bad name`; rm -rf /"]})
        outcome("b", {"shard": "pr-2", "headSha": "a" * 40, "passedOnRetry": []})
        outcome("c", {"shard": "../evil", "headSha": "a" * 40, "passedOnRetry": ["A.T.Other"]})
        outcome("d", "{not json")
        comment = flake_comment(tmp, "https://example.test/run/1") or ""
        check("record-flakes: lists a valid retried test", "`A.T.Flip` | pr-1" in comment)
        check("record-flakes: drops invalid names and shards", "rm -rf" not in comment and "A.T.Other" not in comment)
    with tempfile.TemporaryDirectory() as tmp:
        check("record-flakes: nothing to record gives no comment", flake_comment(tmp, "u") is None)

    for failure in failures:
        print(f"test_results self-test: {failure}", file=sys.stderr)
    if failures:
        return 1
    print("test_results self-test: all cases pass")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--self-test", action="store_true")
    sub = parser.add_subparsers(dest="command")
    failures = sub.add_parser("failures")
    failures.add_argument("trx_dir")
    retry = sub.add_parser("retry-filter")
    retry.add_argument("trx_dir")
    ev = sub.add_parser("evaluate")
    ev.add_argument("shard_dir")
    ev.add_argument("--shard", required=True)
    ev.add_argument("--head", required=True)
    ev.add_argument("--manifest", default=MANIFEST)
    ev.add_argument("--summary")
    ev.add_argument("--outcomes")
    quarantine = sub.add_parser("check-quarantine")
    quarantine.add_argument("--manifest", default=MANIFEST)
    quarantine.add_argument("--base")
    record = sub.add_parser("record-flakes")
    record.add_argument("artifacts_dir")
    record.add_argument("--run-url", required=True)
    record.add_argument("--body", required=True)
    args = parser.parse_args()

    if args.self_test:
        return self_test()
    try:
        if args.command == "record-flakes":
            comment = flake_comment(args.artifacts_dir, args.run_url)
            if comment is None:
                print("record-flakes: no test passed on retry")
                return 0
            with open(args.body, "w", encoding="utf-8") as body:
                body.write(comment)
            print(comment)
            return 0
        if args.command == "failures":
            print("\n".join(read_trx(args.trx_dir).failed()))
            return 0
        if args.command == "retry-filter":
            print(retry_filter(read_trx(args.trx_dir).failed()))
            return 0
        if args.command == "evaluate":
            return evaluate(args)
        if args.command == "check-quarantine":
            with open(args.manifest, encoding="utf-8") as handle:
                code, problems = check_quarantine(json.load(handle), args.base, _run)
            for problem in problems:
                print(problem, file=sys.stderr)
            if code == 0:
                print("check-quarantine: every checked entry has an open tracking issue")
            return code
    except (OSError, json.JSONDecodeError) as error:
        print(f"test_results: {error}", file=sys.stderr)
        return 2
    parser.print_usage(sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
