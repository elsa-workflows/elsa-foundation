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
import re
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
            "secretsProject": manifest.get("secretsCompositionSuite", {}).get("project", ""),
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


# Workflow and documentation guards (FR-003, FR-007, FR-024, FR-025) -------------------------------------------------

CI = ".github/workflows/ci.yml"
CI_FULL = ".github/workflows/ci-full.yml"
MERGING = "docs/contributing/merging.md"
DOCS_ANCHOR = "merging.md#ci-tiers"
SKILLS = [".claude/skills/elsa-auto-review-merge/SKILL.md", ".claude/skills/elsa-quality-fix/SKILL.md",
          ".claude/skills/speckit-implement/SKILL.md", ".claude/skills/elsa-add-unit-tests/SKILL.md"]
DISPATCH = "gh workflow run ci.yml --ref <branch> -f tier=full"


def _jobs(text: str) -> dict[str, str]:
    """Top-level job blocks of a workflow, by key. A text scan: the runners carry no YAML library."""
    body = text.split("\njobs:\n", 1)[1] if "\njobs:\n" in text else ""
    blocks = re.split(r"^  ([A-Za-z0-9_-]+):[ \t]*\n", body, flags=re.M)
    return {blocks[i]: blocks[i + 1] for i in range(1, len(blocks) - 1, 2)}


def _job_name(block: str) -> str:
    match = re.search(r"^    name:\s*(.+)$", block, re.M)
    return match.group(1).strip().strip('"') if match else ""


def check_workflows(ci: str, ci_full: str) -> list[str]:
    """A required context must never be skippable, and no labeled trigger may sit on a required workflow (research R8)."""
    problems = []
    build = [block for block in _jobs(ci).values() if _job_name(block) == "Build & test"]
    if len(build) != 1:
        problems.append(f"{CI}: expected exactly one job named `Build & test`, found {len(build)}")
    else:
        block = build[0]
        if "if: ${{ !cancelled() }}" not in block:
            problems.append(f"{CI}: `Build & test` must run with `if: ${{{{ !cancelled() }}}}` so it always reports")
        needs = re.search(r"^    needs:\s*\[([^\]]*)\]", block, re.M)
        for job in ("plan-tiers", "pr-shard", "architecture-guards", "core-only"):
            if not needs or job not in [n.strip() for n in needs.group(1).split(",")]:
                problems.append(f"{CI}: `Build & test` must need `{job}`")
    full = [block for block in _jobs(ci_full).values() if "'Full tier'" in _job_name(block) or _job_name(block) == FULL_TIER_CONTEXT]
    if len(full) != 1:
        problems.append(f"{CI_FULL}: expected exactly one job that reports `Full tier`, found {len(full)}")
    else:
        block = full[0]
        if "if: ${{ !cancelled() }}" not in block:
            problems.append(f"{CI_FULL}: the `Full tier` job must run with `if: ${{{{ !cancelled() }}}}`")
        if "needs.plan.result != 'success'" not in _job_name(block) or 'PLAN" != success' not in block:
            problems.append(f"{CI_FULL}: the `Full tier` job must be named `Full tier` and fail when the plan did not succeed")
    for path, text in ((CI, ci), (CI_FULL, ci_full)):
        # Comments explain why there is no label trigger; only YAML outside them counts.
        trigger = "\n".join(line.split("#", 1)[0] for line in text.split("\njobs:\n", 1)[0].splitlines())
        if re.search(r"\blabeled\b", trigger):
            problems.append(f"{path}: a `labeled` trigger on a workflow that reports a required check lets any label cancel a real run")
    return problems


def check_docs(read: Callable[[str], str | None]) -> list[str]:
    """The CI tiers section exists, the PR-driving skills link to it, and its literals match the workflows."""
    problems = []
    merging = read(MERGING) or ""
    section = merging.split("## CI tiers", 1)[1].split("\n## ", 1)[0] if "## CI tiers" in merging else None
    if section is None:
        problems.append(f"{MERGING}: no `## CI tiers` section")
    else:
        for literal in (DISPATCH, "PR tier", "full tier", "gh pr ready", "draft", MANIFEST):
            if literal.lower() not in section.lower():
                problems.append(f"{MERGING}#ci-tiers does not mention `{literal}`")
    for skill in SKILLS:
        text = read(skill)
        if text is None or DOCS_ANCHOR not in text:
            problems.append(f"{skill}: no link to docs/contributing/{DOCS_ANCHOR}")
    ci = read(CI) or ""
    if not re.search(r"^      tier:\n(?:        .*\n)*?        options: \[full, pr\]", ci, re.M):
        problems.append(f"{CI}: the workflow_dispatch input `tier` with options [full, pr] is what `{DISPATCH}` relies on")
    return problems


def _read_file(path: str) -> str | None:
    try:
        with open(path, encoding="utf-8") as handle:
            return handle.read()
    except OSError:
        return None


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
                                                                       "require": ""}], "secrets": True,
                                         "secretsProject": "t/S/S.Tests.csproj"}])
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

    good_ci = ("on:\n  pull_request:\njobs:\n  build-and-test:\n    name: Build & test\n"
               "    needs: [plan-tiers, pr-shard, architecture-guards, core-only]\n    if: ${{ !cancelled() }}\n")
    good_full = ("on:\n  pull_request:\n    types: [opened, reopened, synchronize, ready_for_review]\njobs:\n  full-tier:\n"
                 "    name: \"${{ (needs.plan.result != 'success' || x) && 'Full tier' || 'Full tier (not run: draft)' }}\"\n"
                 "    if: ${{ !cancelled() }}\n    steps:\n      - run: if [[ \"$PLAN\" != success ]]; then exit 1; fi\n")
    check("workflows: the real shape passes", not check_workflows(good_ci, good_full))
    check("workflows: a skippable Build & test fails",
          bool(check_workflows(good_ci.replace("if: ${{ !cancelled() }}", "if: ${{ needs.x.outputs.y }}"), good_full)))
    check("workflows: Build & test without the shards fails",
          bool(check_workflows(good_ci.replace("pr-shard, ", ""), good_full)))
    check("workflows: a comment that mentions `labeled` is not a trigger",
          not check_workflows(good_ci, "# There is no `labeled` trigger here.\n" + good_full))
    check("workflows: a labeled trigger fails",
          bool(check_workflows(good_ci, good_full.replace("ready_for_review]", "ready_for_review, labeled]"))))
    check("workflows: a Full tier that ignores a failed plan fails",
          bool(check_workflows(good_ci, good_full.replace("needs.plan.result != 'success' || ", ""))))

    docs = {
        MERGING: f"# Merging\n\n## CI tiers\n\nThe PR tier and the full tier, per {MANIFEST}. Open a draft, then "
                 f"`gh pr ready <n>`. Run `{DISPATCH}`.\n\n## Review and merge\n",
        CI: "      tier:\n        description: Which tier\n        type: choice\n        options: [full, pr]\n",
        **{skill: f"See [CI tiers](../../../docs/contributing/{DOCS_ANCHOR})." for skill in SKILLS},
    }
    check("docs: the real shape passes", not check_docs(docs.get))
    check("docs: a missing skill link fails",
          bool(check_docs(lambda p: "no link" if p == SKILLS[1] else docs.get(p))))
    check("docs: a renamed dispatch option fails",
          bool(check_docs(lambda p: docs[CI].replace("[full, pr]", "[everything, pr]") if p == CI else docs.get(p))))
    check("docs: a missing section fails", bool(check_docs(lambda p: "# Merging\n" if p == MERGING else docs.get(p))))
    check("docs: a section without the dispatch command fails",
          bool(check_docs(lambda p: docs[MERGING].replace(DISPATCH, "run it by hand") if p == MERGING else docs.get(p))))

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
    sub.add_parser("check-workflows")
    sub.add_parser("check-docs")
    args = parser.parse_args()

    if args.self_test:
        return self_test()
    try:
        if args.command == "select":
            return run_select(args)
        if args.command in ("check-workflows", "check-docs"):
            if args.command == "check-workflows":
                problems = check_workflows(_read_file(CI) or "", _read_file(CI_FULL) or "")
            else:
                problems = check_docs(_read_file)
            for problem in problems:
                print(f"test_tiers {args.command}: {problem}", file=sys.stderr)
            if not problems:
                print(f"test_tiers {args.command}: ok")
            return 1 if problems else 0
    except (ManifestError, OSError) as error:
        print(f"test_tiers: {error}", file=sys.stderr)
        return 1
    parser.print_usage(sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())
