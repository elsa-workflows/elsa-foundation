#!/usr/bin/env python3
"""Turn the test tier manifest into CI shard matrices (spec 263).

tools/ci/test-tiers.json assigns every test project to the PR tier or the integration tier and records the shard
grouping. This script reads it and writes the GitHub Actions matrices the shard jobs run. It needs no .NET build, so
the plan job that calls it stays off the critical path. The generator's `test-tiers-check` validates the manifest
against the project graph; this script trusts that check and only refuses JSON it cannot use.

Usage:
  test_tiers.py select --tier pr|full|push|nightly [--manifest <file>] [--base <sha> --head <sha>]
                       [--github-output <file>] [--summary <file>]
  test_tiers.py check-docs [--root <dir>]
  test_tiers.py --self-test

Tiers:
  pr       the PR-tier shards (`Build & test`). While integrationLeavesPrTier is false they also carry the
           integration-tier units, so no test stops gating merges before the full tier is required (FR-011).
  full     the integration shards and the EF container shards (`Full tier`).
  push     a push to main: the PR shards plus, once the switch is on, the integration shards (FR-010).
  nightly  push plus the EF container shards.
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from collections.abc import Callable

MANIFEST = "tools/ci/test-tiers.json"
RULESET = "main-2"
FULL_TIER_CONTEXT = "Full tier"


class ManifestError(Exception):
    pass


def load(text: str) -> dict:
    try:
        manifest = json.loads(text)
    except json.JSONDecodeError as error:
        raise ManifestError(f"{MANIFEST} is not valid JSON: {error}") from error
    if not isinstance(manifest, dict) or manifest.get("schema") != 1:
        raise ManifestError(f"{MANIFEST}: expected an object with \"schema\": 1")
    for key in ("projects", "splits", "shards", "containerSuites", "quarantine"):
        if key not in manifest:
            raise ManifestError(f"{MANIFEST}: missing \"{key}\"")
    return manifest


def class_filter(classes: list[str], negate: bool) -> str:
    # The trailing dot stops `FooTests` from also matching `FooTestsExtra`.
    if negate:
        return "&".join(f"FullyQualifiedName!~{name}." for name in classes)
    return "|".join(f"FullyQualifiedName~{name}." for name in classes)


def unit_run(manifest: dict, unit: str) -> dict:
    """A shard unit is a project path, or `<project>#<group>` for one class group of a split project."""
    project, _, group = unit.partition("#")
    if not group:
        if project in manifest["splits"]:
            raise ManifestError(f"{MANIFEST}: `{project}` is split; a shard must name one of its groups")
        return {"project": project, "group": "", "filter": ""}
    groups = manifest["splits"].get(project, {}).get("groups", [])
    chosen = next((g for g in groups if g.get("name") == group), None)
    if chosen is None:
        raise ManifestError(f"{MANIFEST}: unit `{unit}` names no group of a split project")
    if chosen.get("rest"):
        siblings = [name for g in groups if not g.get("rest") for name in g.get("classes", [])]
        return {"project": project, "group": group, "filter": class_filter(siblings, negate=True)}
    return {"project": project, "group": group, "filter": class_filter(chosen.get("classes", []), negate=False)}


def test_matrix(manifest: dict, shards: list[dict], prefix: str) -> dict:
    include = []
    for shard in shards:
        runs = [unit_run(manifest, unit) for unit in shard["units"]]
        include.append({
            "shard": f"{prefix}{shard['name']}",
            "whole": sorted(run["project"] for run in runs if not run["filter"]),
            "split": [run for run in runs if run["filter"]],
        })
    return {"include": include}


def container_matrix(manifest: dict) -> dict:
    suites = {suite["suite"]: suite for suite in manifest["containerSuites"]}
    secrets = manifest.get("secretsCompositionSuite", {}).get("suite")
    include = []
    for shard in manifest["shards"].get("containers", []):
        unknown = [name for name in shard["suites"] if name not in suites and name != secrets]
        if unknown:
            raise ManifestError(f"{MANIFEST}: container shard `{shard['name']}` names unknown suites {unknown}")
        include.append({
            "shard": shard["name"],
            "suites": [suites[name] for name in shard["suites"] if name in suites],
            "secrets": secrets in shard["suites"],
        })
    return {"include": include}


def select(manifest: dict, tier: str) -> dict[str, dict]:
    switch = bool(manifest.get("integrationLeavesPrTier"))
    shards = manifest["shards"]
    empty = {"include": []}
    pr = test_matrix(manifest, shards["pr"], "")
    integration = test_matrix(manifest, shards["integration"], "")
    containers = container_matrix(manifest)
    if tier == "pr":
        return {"pr": pr, "integration": empty, "container": empty}
    if tier == "full":
        return {"pr": empty, "integration": integration, "container": containers}
    if tier in ("push", "nightly"):
        # While the switch is off, the PR shards already carry the integration units; running the integration shards
        # as well would run those tests twice.
        return {"pr": pr, "integration": integration if switch else empty,
                "container": containers if tier == "nightly" else empty}
    raise ManifestError(f"unknown tier `{tier}`")


def switch_turned_on(base_text: str | None, head: dict) -> bool:
    if not head.get("integrationLeavesPrTier"):
        return False
    if base_text is None:
        return True
    try:
        return not json.loads(base_text).get("integrationLeavesPrTier")
    except json.JSONDecodeError:
        return True


def ruleset_requires(context: str, fetch: Callable[[str], object]) -> bool:
    for ruleset in fetch("rulesets") or []:
        if ruleset.get("name") != RULESET:
            continue
        detail = fetch(f"rulesets/{ruleset['id']}") or {}
        for rule in detail.get("rules", []):
            if rule.get("type") == "required_status_checks":
                checks = rule.get("parameters", {}).get("required_status_checks", [])
                if any(check.get("context") == context for check in checks):
                    return True
    return False


def gh_fetch(path: str) -> object:
    repo = os.environ.get("GITHUB_REPOSITORY", "elsa-workflows/elsa-foundation")
    result = subprocess.run([os.environ.get("GH", "gh"), "api", f"repos/{repo}/{path}"],
                            capture_output=True, text=True, check=True)
    return json.loads(result.stdout)


def git_show(sha: str, path: str) -> str | None:
    result = subprocess.run(["git", "show", f"{sha}:{path}"], capture_output=True, text=True)
    return result.stdout if result.returncode == 0 else None


def summary_table(matrices: dict[str, dict]) -> str:
    lines = ["### Test shards", "", "| Matrix | Shard | Projects | Class groups |", "|---|---|---|---|"]
    for name, matrix in matrices.items():
        for entry in matrix["include"]:
            if name == "container":
                lines.append(f"| {name} | {entry['shard']} | {len(entry['suites'])} suites"
                             f"{' + Secrets EF proof' if entry['secrets'] else ''} | |")
            else:
                groups = ", ".join(run["group"] for run in entry["split"])
                lines.append(f"| {name} | {entry['shard']} | {len(entry['whole'])} | {groups} |")
    return "\n".join(lines) + "\n"


def run_select(args: argparse.Namespace, fetch: Callable[[str], object] = gh_fetch) -> int:
    with open(args.manifest, encoding="utf-8") as handle:
        manifest = load(handle.read())
    if args.base and switch_turned_on(git_show(args.base, MANIFEST), manifest):
        try:
            required = ruleset_requires(FULL_TIER_CONTEXT, fetch)
        except (subprocess.CalledProcessError, OSError, json.JSONDecodeError) as error:
            print(f"test_tiers: could not read the rulesets to verify the switch: {error}", file=sys.stderr)
            return 1
        if not required:
            print(f"test_tiers: this change sets integrationLeavesPrTier to true, but ruleset {RULESET} does not "
                  f"require `{FULL_TIER_CONTEXT}`. Integration-tier projects may leave the PR tier only after the full "
                  f"tier is a required check (FR-011). Ask an admin to add it, then re-run.", file=sys.stderr)
            return 1
    matrices = select(manifest, args.tier)
    if args.github_output:
        with open(args.github_output, "a", encoding="utf-8") as output:
            for name, matrix in matrices.items():
                output.write(f"{name}_matrix={json.dumps(matrix, separators=(',', ':'))}\n")
                output.write(f"{name}_count={len(matrix['include'])}\n")
    if args.summary:
        with open(args.summary, "a", encoding="utf-8") as summary:
            summary.write(summary_table(matrices))
    print(json.dumps({name: len(matrix["include"]) for name, matrix in matrices.items()}))
    return 0


# Self-test -----------------------------------------------------------------------------------------------------------

def _fixture(switch: bool = False) -> dict:
    split = "t/Big/Big.Tests.csproj"
    return {
        "schema": 1,
        "integrationLeavesPrTier": switch,
        "projects": [],
        "splits": {split: {"groups": [
            {"name": "big-a", "classes": ["Big.Tests.Alpha", "Big.Tests.Beta"]},
            {"name": "big-rest", "rest": True}]}},
        "shards": {
            "pr": [{"name": "pr-1", "units": ["t/A/A.Tests.csproj", "t/B/B.Tests.csproj"]},
                   {"name": "pr-2", "units": [f"{split}#big-a"]}],
            "integration": [{"name": "int-1", "units": [f"{split}#big-rest", "t/C/C.Tests.csproj"]}],
            "containers": [{"name": "ef-1", "suites": ["db", "secrets-ef-composition"]}],
        },
        "containerSuites": [{"suite": "db", "project": "t/Db/Db.Tests.csproj", "require": ""}],
        "secretsCompositionSuite": {"suite": "secrets-ef-composition", "project": "t/S/S.Tests.csproj"},
        "quarantine": [],
    }


def _ruleset_fetch(required: list[str]) -> Callable[[str], object]:
    def fetch(path: str) -> object:
        if path == "rulesets":
            return [{"id": 7, "name": RULESET}]
        return {"rules": [{"type": "required_status_checks",
                           "parameters": {"required_status_checks": [{"context": c} for c in required]}}]}
    return fetch


def self_test() -> int:
    failures: list[str] = []

    def check(name: str, condition: bool) -> None:
        if not condition:
            failures.append(name)

    m = select(_fixture(), "pr")
    check("pr: two shards", [e["shard"] for e in m["pr"]["include"]] == ["pr-1", "pr-2"])
    check("pr: whole projects run together", m["pr"]["include"][0]["whole"] == ["t/A/A.Tests.csproj", "t/B/B.Tests.csproj"])
    check("pr: split group filter",
          m["pr"]["include"][1]["split"] == [{"project": "t/Big/Big.Tests.csproj", "group": "big-a",
                                              "filter": "FullyQualifiedName~Big.Tests.Alpha.|FullyQualifiedName~Big.Tests.Beta."}])
    check("pr: no full-tier matrices", not m["integration"]["include"] and not m["container"]["include"])

    m = select(_fixture(), "full")
    rest = m["integration"]["include"][0]["split"][0]
    check("full: rest group negates its siblings",
          rest["filter"] == "FullyQualifiedName!~Big.Tests.Alpha.&FullyQualifiedName!~Big.Tests.Beta.")
    check("full: whole project beside a split unit", m["integration"]["include"][0]["whole"] == ["t/C/C.Tests.csproj"])
    check("full: container shard carries the suite and the Secrets proof",
          m["container"]["include"] == [{"shard": "ef-1", "suites": [{"suite": "db", "project": "t/Db/Db.Tests.csproj",
                                                                       "require": ""}], "secrets": True}])
    check("full: no PR matrix", not m["pr"]["include"])

    check("push, switch off: no integration shards", not select(_fixture(False), "push")["integration"]["include"])
    check("push, switch on: integration shards", len(select(_fixture(True), "push")["integration"]["include"]) == 1)
    check("push: no containers", not select(_fixture(True), "push")["container"]["include"])
    check("nightly: containers", len(select(_fixture(), "nightly")["container"]["include"]) == 1)

    for name, mutate in [
        ("whole split project refused", lambda f: f["shards"]["pr"][0]["units"].append("t/Big/Big.Tests.csproj")),
        ("unknown group refused", lambda f: f["shards"]["pr"][0]["units"].append("t/Big/Big.Tests.csproj#nope")),
        ("unknown suite refused", lambda f: f["shards"]["containers"][0]["suites"].append("ghost")),
    ]:
        fixture = _fixture()
        mutate(fixture)
        try:
            select(fixture, "full" if "suite" in name else "pr")
            failures.append(name)
        except ManifestError:
            pass
    for name, text in [("malformed JSON refused", "{"), ("wrong schema refused", '{"schema": 2}')]:
        try:
            load(text)
            failures.append(name)
        except ManifestError:
            pass

    on = _fixture(True)
    check("switch: unchanged true is not a turn-on", not switch_turned_on(json.dumps(_fixture(True)), on))
    check("switch: false to true is a turn-on", switch_turned_on(json.dumps(_fixture(False)), on))
    check("switch: off is never a turn-on", not switch_turned_on(None, _fixture(False)))
    check("ruleset: missing Full tier refuses", not ruleset_requires(FULL_TIER_CONTEXT, _ruleset_fetch(["Build & test"])))
    check("ruleset: present Full tier allows",
          ruleset_requires(FULL_TIER_CONTEXT, _ruleset_fetch(["Build & test", FULL_TIER_CONTEXT])))

    for failure in failures:
        print(f"test_tiers self-test: {failure}", file=sys.stderr)
    if failures:
        return 1
    print("test_tiers self-test: all cases pass")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--self-test", action="store_true")
    sub = parser.add_subparsers(dest="command")
    sel = sub.add_parser("select")
    sel.add_argument("--tier", required=True, choices=["pr", "full", "push", "nightly"])
    sel.add_argument("--manifest", default=MANIFEST)
    sel.add_argument("--base")
    sel.add_argument("--head")
    sel.add_argument("--github-output")
    sel.add_argument("--summary")
    args = parser.parse_args()

    if args.self_test:
        return self_test()
    try:
        if args.command == "select":
            return run_select(args)
    except (ManifestError, OSError) as error:
        print(f"test_tiers: {error}", file=sys.stderr)
        return 1
    parser.print_usage(sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
