#!/usr/bin/env python3
"""Turn the test tier manifest into CI shard matrices (spec 263).

tools/ci/test-tiers.json assigns every test project to the PR tier or the integration tier and records the shard
grouping. This script reads it and writes the GitHub Actions matrices the shard jobs run. It needs no .NET build, so
the plan job that calls it stays off the critical path. The generator's `test-tiers-check` validates the manifest
against the project graph; this script trusts that check and only refuses JSON it cannot use.

Usage:
  test_tiers.py select --tier pr|full|push|nightly [--manifest <file>] [--base <sha> --head <sha>]
                       [--event <github event>] [--github-output <file>] [--summary <file>]
  test_tiers.py check-docs [--root <dir>]
  test_tiers.py --self-test

Tiers:
  pr       the PR-tier shards (`Build & test`). While integrationLeavesPrTier is false they also carry the
           integration-tier units, so no test stops gating merges before the full tier is required (FR-011).
  full     the integration shards and the EF container shards (`Full tier`).
  push     a push to main: the PR shards plus, once the switch is on, the integration shards (FR-010).
  nightly  push plus the EF container shards.

With `--event pull_request` and a base, only the units and container suites whose test project the change can affect
are kept, and a shard left empty is dropped (tools/ci/affected_tests.py, issue #3787). Every other event, a merge-queue
entry included, keeps everything.
"""

from __future__ import annotations

import argparse
import contextlib
import io
import json
import re
import os
import subprocess
import sys
import tempfile
from collections.abc import Callable

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import affected_tests  # noqa: E402

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


Keep = Callable[[str], bool]


def _all(project: str) -> bool:
    return True


def test_matrix(manifest: dict, shards: list[dict], prefix: str, keep: Keep = _all) -> dict:
    include = []
    for shard in shards:
        runs = [run for run in (unit_run(manifest, unit) for unit in shard["units"]) if keep(run["project"])]
        if not runs:
            continue
        include.append({
            "shard": f"{prefix}{shard['name']}",
            "whole": sorted(run["project"] for run in runs if not run["filter"]),
            "split": [run for run in runs if run["filter"]],
        })
    return {"include": include}


def container_matrix(manifest: dict, keep: Keep = _all) -> dict:
    suites = {suite["suite"]: suite for suite in manifest["containerSuites"]}
    secrets = manifest.get("secretsCompositionSuite", {}).get("suite")
    secrets_project = manifest.get("secretsCompositionSuite", {}).get("project", "")
    include = []
    for shard in manifest["shards"].get("containers", []):
        unknown = [name for name in shard["suites"] if name not in suites and name != secrets]
        if unknown:
            raise ManifestError(f"{MANIFEST}: container shard `{shard['name']}` names unknown suites {unknown}")
        kept = [suites[name] for name in shard["suites"] if name in suites and keep(suites[name]["project"])]
        with_secrets = secrets in shard["suites"] and keep(secrets_project)
        if not kept and not with_secrets:
            continue
        include.append({
            "shard": shard["name"],
            "suites": kept,
            "secrets": with_secrets,
            "secretsProject": secrets_project,
        })
    return {"include": include}


def build_proof_shard(manifest: dict) -> list[dict]:
    """One PR shard cut to its quickest whole project. Every PR shard builds the whole solution. When a pull request
    keeps no PR-tier test but changes a solution project that neither Architecture guards nor Core-only builds, this
    shard keeps the solution build, and the compile-break check it gives (#543), in `Build & test`."""
    medians = {entry["project"]: entry.get("medianMinutes") for entry in manifest["projects"]}
    best = None
    for shard in manifest["shards"]["pr"]:
        for unit in shard["units"]:
            if "#" in unit:
                continue
            minutes = medians.get(unit)
            cost = minutes if isinstance(minutes, (int, float)) else float("inf")
            if best is None or cost < best[0]:
                best = (cost, shard["name"], unit)
    return [{"name": best[1], "units": [best[2]]}] if best else manifest["shards"]["pr"][:1]


def select(manifest: dict, tier: str, keep: Keep = _all, build_proof: bool = False) -> dict[str, dict]:
    """`keep` says which test projects run: all of them unless a pull request narrows it. With `build_proof`, a PR
    tier that keeps no shard still gets one, cut to its quickest project, so the solution is still built."""
    switch = bool(manifest.get("integrationLeavesPrTier"))
    shards = manifest["shards"]
    empty = {"include": []}
    pr = test_matrix(manifest, shards["pr"], "", keep)
    if build_proof and not pr["include"]:
        pr = test_matrix(manifest, build_proof_shard(manifest), "")
    integration = test_matrix(manifest, shards["integration"], "", keep)
    containers = container_matrix(manifest, keep)
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


def job_entry(manifest: dict, shard: str, projects: list[str]) -> dict:
    """A run_shard.py entry for a job outside the shard matrix (Architecture guards, Core-only), so it gets the same
    retry, quarantine and reporting (FR-004). Only manifest test projects are kept: a solution filter also lists
    source projects."""
    tests = {entry["project"] for entry in manifest["projects"]}
    return {"shard": shard, "whole": sorted(project for project in projects if project in tests), "split": []}


def slnf_projects(text: str) -> list[str]:
    return [project.replace("\\", "/") for project in json.loads(text)["solution"]["projects"]]


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


def _selected_projects(matrices: dict[str, dict]) -> set[str]:
    projects = set()
    for name, matrix in matrices.items():
        for entry in matrix["include"]:
            if name == "container":
                projects.update(suite["project"] for suite in entry["suites"])
                if entry["secrets"]:
                    projects.add(entry["secretsProject"])
            else:
                projects.update(entry["whole"])
                projects.update(run["project"] for run in entry["split"])
    return projects


def affected_summary(decision: affected_tests.Decision | None, matrices: dict[str, dict], totals: dict[str, dict],
                     build_proof: bool, limit: int = 40) -> str:
    """Why this run's shards were chosen, for the step summary."""
    lines = ["### Affected tests", ""]
    if decision is None:
        lines.append("Every shard. Only a pull request narrows the tests; a push to main, the nightly run, a manual "
                     "dispatch and a merge-queue entry run everything.")
    elif decision.everything:
        lines.append(f"Every shard: {decision.reason}.")
    else:
        def listed(title: str, items: set[str]) -> None:
            ordered = sorted(items)
            lines.append(f"{title} ({len(ordered)}):")
            lines.append("")
            lines.extend(f"- `{item}`" for item in ordered[:limit])
            if not ordered:
                lines.append("- none")
            if len(ordered) > limit:
                lines.append(f"- and {len(ordered) - limit} more")
            lines.append("")
        lines.append("Only the test projects this pull request can affect. Architecture guards, Core-only and "
                     "Generated maps fresh still run, and the merge queue runs every shard before it lands.")
        lines.append("")
        listed("Changed projects", decision.changed_projects)
        if decision.placed_elsewhere:
            listed("Paths no project owns, counted for the projects that read them", set(decision.placed_elsewhere))
        listed("Selected test projects", _selected_projects(matrices))
        if build_proof:
            lines.append("No PR-tier test is affected, but the change reaches a solution project that neither "
                         "Architecture guards nor Core-only builds, so one PR shard is kept, cut to its quickest "
                         "project, to build the solution.")
            lines.append("")
    for name, total in totals.items():
        if total["include"]:
            lines.append(f"- {name}: {len(matrices[name]['include'])} of {len(total['include'])} shards")
    return "\n".join(lines) + "\n\n"


def run_select(args: argparse.Namespace, fetch: Callable[[str], object] = gh_fetch,
               decide: Callable[..., affected_tests.Decision] = affected_tests.compute) -> int:
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
    decision = None
    if getattr(args, "event", None) == "pull_request":
        decision = decide(args.base, args.head, always_run=set(manifest.get("ownJobs", {})))
    keep = decision.keeps if decision else _all
    matrices = select(manifest, args.tier, keep)
    build_proof = bool(decision and decision.unbuilt and not matrices["pr"]["include"]
                       and args.tier in ("pr", "push", "nightly"))
    if build_proof:
        matrices = select(manifest, args.tier, keep, build_proof=True)
    if args.github_output:
        with open(args.github_output, "a", encoding="utf-8") as output:
            for name, matrix in matrices.items():
                output.write(f"{name}_matrix={json.dumps(matrix, separators=(',', ':'))}\n")
                output.write(f"{name}_count={len(matrix['include'])}\n")
    if args.summary:
        with open(args.summary, "a", encoding="utf-8") as summary:
            summary.write(affected_summary(decision, matrices, select(manifest, args.tier), build_proof))
            summary.write(summary_table(matrices))
    print(json.dumps({name: len(matrix["include"]) for name, matrix in matrices.items()}))
    return 0


# Workflow and documentation guards (FR-003, FR-007, FR-024, FR-025) -------------------------------------------------

CI = ".github/workflows/ci.yml"
CI_FULL = CI  # Both required aggregates now live in the same workflow.
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


MERGE_GROUP_TRIGGER = re.compile(r"^  merge_group:[ \t]*\n    types: \[checks_requested\][ \t]*$", re.M)


def check_workflows(ci: str, ci_full: str) -> list[str]:
    """A required context must never be skippable, the merge queue must get every required context, and no labeled
    trigger may sit on a required workflow (research R8)."""
    problems = []
    if not MERGE_GROUP_TRIGGER.search(ci.split("\njobs:\n", 1)[0]):
        problems.append(f"{CI}: the required checks must also run on `merge_group: types: [checks_requested]`, or every "
                        f"merge-queue entry waits for checks that never report")
    build = [block for block in _jobs(ci).values() if _job_name(block) == "Build & test" or "'Build & test'" in _job_name(block)]
    if len(build) != 1:
        problems.append(f"{CI}: expected exactly one job named `Build & test`, found {len(build)}")
    else:
        block = build[0]
        if not any(condition in block for condition in (
                "if: ${{ !cancelled() }}",
                "if: ${{ !cancelled() && (github.event_name != 'workflow_dispatch' || inputs.suite == 'ci') }}",
                f"if: ${{{{ !cancelled() && {GATE_OPEN} && (github.event_name != 'workflow_dispatch' || inputs.suite == 'ci') }}}}")):
            problems.append(f"{CI}: `Build & test` must run with `if: ${{{{ !cancelled() ... }}}}` (no other gate than the initiative gate's explicit false) so it always reports")
        needs = re.search(r"^    needs:\s*\[([^\]]*)\]", block, re.M)
        for job in ("plan-tiers", "pr-shard", "architecture-guards", "core-only", "plan-scope",
                    "studio-lint-and-test", "studio-browser-tests", "studio-dotnet-build-and-test", "studio-ux-acceptance"):
            if not needs or job not in [n.strip() for n in needs.group(1).split(",")]:
                problems.append(f"{CI}: `Build & test` must need `{job}`")
        if "run: python3 tools/ci/studio_gate.py" not in block:
            problems.append(f"{CI}: Build & test must execute the selected Studio verdict")
        elif block.find("run: python3 tools/ci/studio_gate.py") > block.find("DOCS_ONLY") >= 0:
            problems.append(f"{CI}: Studio verdict must precede the docs-only fast path")
        for key, source in {"STUDIO_SELECTED": "needs.plan-scope.outputs.studio", "STUDIO_FRONTEND": "needs.studio-lint-and-test.result",
                            "STUDIO_BROWSER": "needs.studio-browser-tests.result", "STUDIO_DOTNET": "needs.studio-dotnet-build-and-test.result",
                            "STUDIO_UX": "needs.studio-ux-acceptance.result"}.items():
            if f"{key}: ${{{{ {source} }}}}" not in block:
                problems.append(f"{CI}: Studio verdict must consume {key} from {source}")
    full = [block for block in _jobs(ci_full).values() if "'Full tier'" in _job_name(block) or _job_name(block) == FULL_TIER_CONTEXT]
    if len(full) != 1:
        problems.append(f"{CI_FULL}: expected exactly one job that reports `Full tier`, found {len(full)}")
    else:
        block = full[0]
        if not any(condition in block for condition in (
                "if: ${{ !cancelled() }}",
                "if: ${{ !cancelled() && (github.event_name == 'pull_request' || github.event_name == 'merge_group') }}",
                f"if: ${{{{ !cancelled() && {GATE_OPEN} && (github.event_name == 'pull_request' || github.event_name == 'merge_group') }}}}")):
            problems.append(f"{CI_FULL}: the `Full tier` job must run with `if: ${{{{ !cancelled() ... }}}}` on pull requests "
                            f"and merge-queue entries")
        if "needs.plan-full.result != 'success'" not in _job_name(block) or 'PLAN" != success' not in block:
            problems.append(f"{CI_FULL}: the `Full tier` job must be named `Full tier` and fail when the plan did not succeed")
    # #3708: the latest ready suite must execute all required contexts, not rename/skip
    # the PR tier and maps while leaving their green results in an earlier draft suite.
    for job in ("plan-tiers", "build-and-test", "generated-maps"):
        block = _jobs(ci).get(job, "")
        if not block:
            problems.append(f"{CI}: missing `{job}` required for complete ready runs")
        # A folded/quoted scalar may put the exclusion on a continuation line.
        # Stop at the next job-level field; step-level conditions are unrelated.
        for field in ("name", "if"):
            match = re.search(rf"^    {field}:[ \t]*(.*)$", block, re.M)
            if not match:
                continue
            value = [match.group(1)]
            for line in block[match.end():].splitlines():
                if line.strip() and len(line) - len(line.lstrip()) <= 4:
                    break
                if line.strip() and not line.lstrip().startswith("#"):
                    value.append(line.strip())
            if "ready_for_review" in " ".join(value):
                problems.append(f"{CI}: `{job}` must run unchanged on the ready transition")
                break
    concurrency = ci.split("\njobs:\n", 1)[0]
    if re.search(r"^.*ready_for_review.*(?:'ready'|'head').*$", concurrency, re.M):
        problems.append(f"{CI}: ready and head events must share one PR concurrency group")
    for path, text in ((CI, ci), (CI_FULL, ci_full)):
        # Comments explain why there is no label trigger; only YAML outside them counts.
        trigger = "\n".join(line.split("#", 1)[0] for line in text.split("\njobs:\n", 1)[0].splitlines())
        if re.search(r"\blabeled\b", trigger):
            problems.append(f"{path}: a `labeled` trigger on a workflow that reports a required check lets any label cancel a real run")
    return problems


# Initiative branches ------------------------------------------------------------------------------------------------
#
# A pull request to initiative/* runs CI only when it carries implementation (docs/contributing/merging.md#ci-tiers).
# The `initiative-gate` job decides (change_scope.py --gate); every other job skips on its explicit run=false. The check
# below proves that on the real ci.yml by evaluating each job's `if:` the way GitHub does, in dependency order: a job
# with no status function in its condition also needs every ancestor to have succeeded, a skipped job's outputs are
# empty, and !cancelled()/always()/failure() lift that implicit success() requirement.

GATE_JOB = "initiative-gate"
GATE_OPEN = f"needs.{GATE_JOB}.outputs.run != 'false'"
GATE_NAME = "Initiative gate"  # merging.md#ci-tiers names it; check_docs keeps the two in step


class ExpressionError(Exception):
    pass


def _job_if(block: str) -> str:
    """A job's `if:` (plain or a folded scalar), without the ${{ }} wrapper; "" when it has none."""
    match = re.search(r"^    if:[ \t]*(.*)$", block, re.M)
    if not match:
        return ""
    value = match.group(1).strip()
    if value in (">-", ">", "|", "|-"):
        lines = []
        for line in block[match.end():].splitlines()[1:]:
            if line.strip() and len(line) - len(line.lstrip()) <= 4:
                break
            lines.append(line.strip())
        value = " ".join(lines).strip()
    if value.startswith("${{") and value.endswith("}}"):
        value = value[3:-2].strip()
    return value


def _job_needs(block: str) -> list[str]:
    inline = re.search(r"^    needs:[ \t]*\[([^\]]*)\]", block, re.M)
    if inline:
        return [need.strip() for need in inline.group(1).split(",") if need.strip()]
    single = re.search(r"^    needs:[ \t]*([A-Za-z0-9_-]+)[ \t]*$", block, re.M)
    if single:
        return [single.group(1)]
    listed = re.search(r"^    needs:[ \t]*\n((?:      - .*\n)+)", block, re.M)
    return [line.strip()[2:].strip() for line in listed.group(1).splitlines()] if listed else []


_TOKEN = re.compile(r"\s*(?:(?P<str>'(?:[^']|'')*')|(?P<op>&&|\|\||==|!=|!|\(|\))"
                    r"|(?P<name>[A-Za-z_][A-Za-z0-9_.-]*\(\)|[A-Za-z_][A-Za-z0-9_.-]*))")


def evaluate(expression: str, context: Callable[[str], object]) -> object:
    """A GitHub Actions expression over string literals, names, ==, !=, !, && and || (the subset ci.yml's job
    conditions use), with GitHub's semantics: && and || return an operand, == and != compare strings ignoring case."""
    tokens, position, end = [], 0, len(expression.rstrip())
    while position < end:
        match = _TOKEN.match(expression, position)
        if not match or match.end() == position:
            raise ExpressionError(f"cannot read `{expression[position:]}` in `{expression}`")
        position = match.end()
        tokens.append((match.lastgroup, match.group(match.lastgroup)))
    index = 0

    def peek() -> tuple[str, str] | None:
        return tokens[index] if index < len(tokens) else None

    def take() -> tuple[str, str]:
        nonlocal index
        if index >= len(tokens):
            raise ExpressionError(f"unexpected end of `{expression}`")
        index += 1
        return tokens[index - 1]

    def truthy(value: object) -> bool:
        return value not in ("", None, False, 0)

    def text(value: object) -> str:
        return ("true" if value else "false") if isinstance(value, bool) else str(value or "")

    def primary() -> object:
        kind, value = take()
        if (kind, value) == ("op", "!"):
            return not truthy(primary())
        if (kind, value) == ("op", "("):
            result = either()
            if take() != ("op", ")"):
                raise ExpressionError(f"unbalanced parentheses in `{expression}`")
            return result
        if kind == "str":
            return value[1:-1].replace("''", "'")
        if kind == "name":
            return context(value)
        raise ExpressionError(f"unexpected `{value}` in `{expression}`")

    def comparison() -> object:
        left = primary()
        while peek() in (("op", "=="), ("op", "!=")):
            operator = take()[1]
            same = text(left).lower() == text(primary()).lower()
            left = same if operator == "==" else not same
        return left

    def both() -> object:
        left = comparison()
        while peek() == ("op", "&&"):
            take()
            right = comparison()
            left = right if truthy(left) else left
        return left

    def either() -> object:
        left = both()
        while peek() == ("op", "||"):
            take()
            right = both()
            left = left if truthy(left) else right
        return left

    result = either()
    if index != len(tokens):
        raise ExpressionError(f"trailing `{tokens[index][1]}` in `{expression}`")
    return result


def simulate(ci: str, event: str, outputs: dict[str, dict[str, str]], draft: bool = False,
             failed: frozenset[str] = frozenset()) -> dict[str, bool]:
    """Which jobs of a workflow start for `event` (not workflow_dispatch). A started job succeeds with the given outputs,
    or fails without outputs when it is in `failed`. success() and failure() look at every ancestor, not only the
    direct needs: GitHub skips a job without a status function when any job it transitively needs did not succeed
    (actions/runner#491)."""
    jobs = _jobs(ci)
    results: dict[str, str] = {}
    started: dict[str, bool] = {}
    ancestry: dict[str, set[str]] = {}

    def visit(job: str, trail: tuple[str, ...] = ()) -> None:
        if job in results:
            return
        if job in trail or job not in jobs:
            raise ExpressionError(f"`{job}` is missing or part of a cycle ({' -> '.join(trail + (job,))})")
        needs = _job_needs(jobs[job])
        for need in needs:
            visit(need, trail + (job,))
        ancestors = ancestry[job] = set(needs).union(*(ancestry[need] for need in needs))

        def context(name: str) -> object:
            if name == "cancelled()":
                return False
            if name == "always()":
                return True
            if name == "success()":
                return all(results[ancestor] == "success" for ancestor in ancestors)
            if name == "failure()":
                return any(results[ancestor] == "failure" for ancestor in ancestors)
            if name == "github.event_name":
                return event
            if name == "github.event.pull_request.draft":
                return draft
            if name in ("inputs.suite", "inputs.tier"):
                return ""
            parts = name.split(".")
            if parts[0] == "needs" and len(parts) >= 3 and parts[1] in needs:
                if parts[2] == "result" and len(parts) == 3:
                    return results[parts[1]]
                if parts[2] == "outputs" and len(parts) == 4:
                    return outputs.get(parts[1], {}).get(parts[3], "") if results[parts[1]] == "success" else ""
            raise ExpressionError(f"`{job}`: unknown name `{name}`")

        condition = _job_if(jobs[job])
        if re.search(r"\b(?:success|failure|cancelled|always)\(\)", condition):
            expression = condition
        else:
            expression = f"success() && ({condition})" if condition else "success()"
        runs = bool(evaluate(expression, context))
        started[job] = runs
        results[job] = ("failure" if job in failed else "success") if runs else "skipped"

    for job in jobs:
        visit(job)
    return started


# What the planning jobs report for a change that carries implementation: some shards affected, the full tier on.
_CODE_OUTPUTS = {
    "plan-scope": {"studio": "false", "weaver": "false", "published-images": "false", "studio-order": "false",
                   "backend-source": "false"},
    "plan-tiers": {"docs_only": "false", "pr_count": "1", "integration_count": "1", "container_count": "1"},
    "plan-full": {"run": "true", "docs_only": "false", "integration_count": "1", "container_count": "1"},
}
# The tier jobs a ready pull request that carries implementation starts, to main and to an initiative branch alike.
TIER_JOBS = ("plan-scope", "plan-tiers", "pr-shard", "architecture-guards", "core-only", "build-and-test",
             "generated-maps", "plan-full", "integration-shard", "ef-container-shard", "full-tier")


def _raises(action: Callable[[], object], error: type[Exception]) -> bool:
    try:
        action()
    except error:
        return True
    return False


def check_initiative_gate(ci: str) -> list[str]:
    """Docs-only PR to initiative/*: only the gate starts. Code PR to it: the PR and full tiers start, as on main.
    Any other run: the gate says run=true and the jobs start as they did before; only an explicit false skips, and a
    gate that fails still starts every required context, so none passes by being skipped."""
    problems = []
    trigger = ci.split("\njobs:\n", 1)[0]
    pull_request = re.search(r"^  pull_request:[ \t]*\n((?: {4,}.*\n)+)", trigger, re.M)
    if not pull_request or not re.search(r"^      - 'initiative/\*\*'[ \t]*$", pull_request.group(1), re.M):
        problems.append(f"{CI}: `pull_request.branches` must include 'initiative/**' so implementation PRs to an "
                        f"initiative branch run CI")
    jobs = _jobs(ci)
    gate = jobs.get(GATE_JOB, "")
    if not gate or _job_needs(gate) or _job_if(gate):
        problems.append(f"{CI}: `{GATE_JOB}` must be an unconditional first job with no needs")
        return problems
    if "python3 tools/ci/change_scope.py --gate" not in gate or "run: ${{ steps.gate.outputs.run }}" not in gate:
        problems.append(f"{CI}: `{GATE_JOB}` must output run from `change_scope.py --gate`")
    if _job_name(gate) != GATE_NAME:
        problems.append(f"{CI}: `{GATE_JOB}` must be named `{GATE_NAME}`, the name merging.md#ci-tiers uses")
    try:
        closed = {GATE_JOB: {"run": "false"}}
        started = []
        for draft in (False, True):
            started += [job for job, runs in simulate(ci, "pull_request", closed, draft=draft).items()
                        if runs and job != GATE_JOB and job not in started]
        if started:
            problems.append(f"{CI}: a documentation-only PR to an initiative branch starts {', '.join(started)}; "
                            f"only `{GATE_JOB}` may run")
        code = simulate(ci, "pull_request", {GATE_JOB: {"run": "true"}, **_CODE_OUTPUTS})
        missing = [job for job in TIER_JOBS if not code.get(job)]
        if missing:
            problems.append(f"{CI}: a ready PR that carries implementation does not start {', '.join(missing)}")
        # Only an explicit 'false' skips: a gate that reports nothing starts every job, and a gate that fails still
        # starts every required context, which then gives its own verdict rather than passing as skipped.
        for event in ("pull_request", "merge_group", "push", "schedule"):
            opened = simulate(ci, event, {GATE_JOB: {"run": "true"}, **_CODE_OUTPUTS})
            silent = simulate(ci, event, _CODE_OUTPUTS)
            if {j: r for j, r in opened.items() if j != GATE_JOB} != {j: r for j, r in silent.items() if j != GATE_JOB}:
                problems.append(f"{CI}: on {event}, jobs must skip only on an explicit run=false from `{GATE_JOB}`")
            broken = simulate(ci, event, _CODE_OUTPUTS, failed=frozenset({GATE_JOB}))
            required = ["build-and-test", "generated-maps"] + (["full-tier"] if event in ("pull_request", "merge_group") else [])
            skipped = [job for job in required if not broken.get(job)]
            if skipped:
                problems.append(f"{CI}: on {event}, a failed `{GATE_JOB}` skips {', '.join(skipped)}; a required check "
                                f"must still start and give its own verdict")
    except ExpressionError as error:
        problems.append(f"{CI}: cannot evaluate the job conditions: {error}")
    return problems


def check_docs(read: Callable[[str], str | None]) -> list[str]:
    """The CI tiers section exists, the PR-driving skills link to it, and its literals match the workflows."""
    problems = []
    merging = read(MERGING) or ""
    section = merging.split("## CI tiers", 1)[1].split("\n## ", 1)[0] if "## CI tiers" in merging else None
    if section is None:
        problems.append(f"{MERGING}: no `## CI tiers` section")
    else:
        for literal in (DISPATCH, "PR tier", "full tier", "gh pr ready", "draft", MANIFEST, GATE_NAME, "initiative/"):
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

    # Affected-only selection on pull requests (#3787).
    only = lambda *projects: (lambda project: project in projects)  # noqa: E731
    m = select(_fixture(), "pr", only("t/B/B.Tests.csproj"))
    check("affected: an unaffected unit is dropped from its shard",
          [(e["shard"], e["whole"]) for e in m["pr"]["include"]] == [("pr-1", ["t/B/B.Tests.csproj"])])
    m = select(_fixture(), "full", only("t/Big/Big.Tests.csproj"))
    check("affected: a split group follows its project",
          [e["shard"] for e in m["integration"]["include"]] == ["int-1"]
          and m["integration"]["include"][0]["whole"] == [] and len(m["integration"]["include"][0]["split"]) == 1)
    check("affected: a container shard with no affected suite is dropped", not m["container"]["include"])
    m = select(_fixture(), "full", only("t/S/S.Tests.csproj"))
    check("affected: the Secrets proof follows its project",
          m["container"]["include"] == [{"shard": "ef-1", "suites": [], "secrets": True,
                                         "secretsProject": "t/S/S.Tests.csproj"}])
    m = select(_fixture(), "full", only("t/Db/Db.Tests.csproj"))
    check("affected: a suite follows its project", [s["suite"] for s in m["container"]["include"][0]["suites"]] == ["db"]
          and not m["container"]["include"][0]["secrets"])
    m = select(_fixture(), "full", only())
    check("affected: nothing affected leaves empty matrices",
          all(not matrix["include"] for matrix in m.values()))
    m = select(_fixture() | {"projects": [{"project": "t/A/A.Tests.csproj", "medianMinutes": 2.0},
                                          {"project": "t/B/B.Tests.csproj", "medianMinutes": 0.1}]},
               "pr", only(), build_proof=True)
    check("affected: the build proof keeps one shard, cut to its quickest whole project",
          [(e["shard"], e["whole"], e["split"]) for e in m["pr"]["include"]] == [("pr-1", ["t/B/B.Tests.csproj"], [])])
    m = select(_fixture(), "pr", only("t/A/A.Tests.csproj"), build_proof=True)
    check("affected: no build proof when a PR shard already builds the solution",
          [e["whole"] for e in m["pr"]["include"]] == [["t/A/A.Tests.csproj"]])

    def selected(event: str, decision: affected_tests.Decision, tier: str = "full") -> tuple[dict, list[str], str]:
        with tempfile.TemporaryDirectory() as scratch:
            manifest, output, summary = (os.path.join(scratch, name) for name in ("m.json", "out", "summary"))
            with open(manifest, "w", encoding="utf-8") as handle:
                json.dump(_fixture(True), handle)
            calls: list[str] = []
            args = argparse.Namespace(manifest=manifest, tier=tier, base=None, head="h", event=event,
                                      github_output=output, summary=summary)
            with contextlib.redirect_stdout(io.StringIO()):
                run_select(args, decide=lambda *a, **k: calls.append(event) or decision)
            with open(output, encoding="utf-8") as handle:
                outputs = dict(line.split("=", 1) for line in handle.read().splitlines())
            with open(summary, encoding="utf-8") as handle:
                return outputs, calls, handle.read()

    known = {"t/A/A.Tests.csproj", "t/B/B.Tests.csproj", "t/C/C.Tests.csproj", "t/Big/Big.Tests.csproj",
             "t/Db/Db.Tests.csproj", "t/S/S.Tests.csproj"}
    nothing = affected_tests.Decision(False, "affected projects only", {"src/X/X.csproj"}, {"src/X/X.csproj"},
                                      known=known)
    outputs, _, summary = selected("pull_request", nothing, "pr")
    check("run_select: a PR tier that affects nothing writes pr_count=0",
          outputs.get("pr_count") == "0" and outputs.get("pr_matrix") == '{"include":[]}'
          and "build the solution" not in summary)
    unbuilt = affected_tests.Decision(False, "affected projects only", {"src/X/X.csproj"}, {"src/X/X.csproj"},
                                      unbuilt={"src/X/X.csproj"}, known=known)
    outputs, _, summary = selected("pull_request", unbuilt, "pr")
    check("run_select: an unbuilt solution project keeps one build-proof shard and says so",
          outputs.get("pr_count") == "1" and "build the solution" in summary)
    outputs, calls, summary = selected("pull_request", nothing)
    check("run_select: a pull request that affects nothing writes zero counts",
          outputs.get("integration_count") == "0" and outputs.get("container_count") == "0"
          and outputs.get("integration_matrix") == '{"include":[]}' and calls == ["pull_request"])
    check("run_select: the summary names the changed projects and the shard counts",
          "`src/X/X.csproj`" in summary and "integration: 0 of 1 shards" in summary)
    for event in ("merge_group", "push", "schedule", "workflow_dispatch"):
        outputs, calls, summary = selected(event, nothing)
        check(f"run_select: {event} selects everything without diffing",
              outputs.get("integration_count") == "1" and outputs.get("container_count") == "1" and not calls)
    outputs, _, summary = selected("pull_request", affected_tests.Decision(True, "`global.json` is a shared build input"))
    check("run_select: a select-everything decision keeps every shard and says why",
          outputs.get("integration_count") == "1" and "global.json" in summary)

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

    entry = job_entry(_fixture() | {"projects": [{"project": "t/A/A.Tests.csproj"}]}, "core-only",
                      slnf_projects('{"solution": {"projects": ["src\\\\A\\\\A.csproj", "t\\\\A\\\\A.Tests.csproj"]}}'))
    check("job entry: keeps only test projects, with forward slashes",
          entry == {"shard": "core-only", "whole": ["t/A/A.Tests.csproj"], "split": []})

    good_ci = ("on:\n  pull_request:\n  merge_group:\n    types: [checks_requested]\njobs:\n  plan-tiers:\n    name: Plan tiers\n"
               "  generated-maps:\n    name: Generated maps fresh\n"
               "  build-and-test:\n    name: Build & test\n"
               "    needs: [plan-tiers, pr-shard, architecture-guards, core-only, plan-scope, studio-lint-and-test, studio-browser-tests, studio-dotnet-build-and-test, studio-ux-acceptance]\n    if: ${{ !cancelled() }}\n")
    good_ci += ("    steps:\n      - name: Studio proofs\n        env:\n"
                "          STUDIO_SELECTED: ${{ needs.plan-scope.outputs.studio }}\n"
                "          STUDIO_FRONTEND: ${{ needs.studio-lint-and-test.result }}\n"
                "          STUDIO_BROWSER: ${{ needs.studio-browser-tests.result }}\n"
                "          STUDIO_DOTNET: ${{ needs.studio-dotnet-build-and-test.result }}\n"
                "          STUDIO_UX: ${{ needs.studio-ux-acceptance.result }}\n"
                "        run: python3 tools/ci/studio_gate.py\n")
    good_full = ("on:\n  pull_request:\n    types: [opened, reopened, synchronize, ready_for_review]\n"
                 "  merge_group:\n    types: [checks_requested]\njobs:\n  full-tier:\n"
                 "    name: \"${{ (needs.plan-full.result != 'success' || x) && 'Full tier' || 'Full tier (not run: draft)' }}\"\n"
                 "    if: ${{ !cancelled() }}\n    steps:\n      - run: if [[ \"$PLAN\" != success ]]; then exit 1; fi\n")
    queue_full = good_full.replace("if: ${{ !cancelled() }}",
                                   "if: ${{ !cancelled() && (github.event_name == 'pull_request' || github.event_name == 'merge_group') }}")
    check("workflows: the real shape passes", not check_workflows(good_ci, good_full))
    check("workflows: a skippable Build & test fails",
          bool(check_workflows(good_ci.replace("if: ${{ !cancelled() }}", "if: ${{ needs.x.outputs.y }}"), good_full)))
    check("workflows: a ready-only skip of the required PR context fails",
          bool(check_workflows(good_ci.replace("if: ${{ !cancelled() }}",
               "if: ${{ !cancelled() && github.event.action != 'ready_for_review' }}"), good_full)))
    check("workflows: ready cannot rename or skip maps",
          bool(check_workflows(good_ci + "  generated-maps:\n    name: Generated maps fresh\n"
               "    if: github.event.action != 'ready_for_review'\n", good_full)))
    check("workflows: a folded ready-only maps skip fails",
          bool(check_workflows(good_ci.replace("    name: Generated maps fresh\n",
               "    name: Generated maps fresh\n    if: >-\n"
               "      github.event.action != 'ready_for_review' && github.event_name == 'pull_request'\n"), good_full)))
    check("workflows: a multiline ready-only maps name fails",
          bool(check_workflows(good_ci.replace("    name: Generated maps fresh\n",
               "    name: >-\n      ${{ github.event.action == 'ready_for_review'\n"
               "        && 'Maps already requested' || 'Generated maps fresh' }}\n"), good_full)))
    check("workflows: ready cannot skip the PR-tier plan",
          bool(check_workflows(good_ci.replace("  plan-tiers:\n", "  plan-tiers:\n    if: github.event.action != 'ready_for_review'\n"), good_full)))
    check("workflows: deleting maps cannot leave a required context missing",
          bool(check_workflows(good_ci.replace("  generated-maps:\n    name: Generated maps fresh\n", ""), good_full)))
    check("workflows: ready and pushes cannot leave superseded runs in separate groups",
          bool(check_workflows("concurrency:\n  group: ${{ github.event.action == 'ready_for_review' && 'ready' || 'head' }}\n"
               + good_ci, good_full)))
    check("workflows: Build & test without the shards fails",
          bool(check_workflows(good_ci.replace("pr-shard, ", ""), good_full)))
    check("workflows: disconnected UX verdict fails",
          bool(check_workflows(good_ci.replace("run: python3 tools/ci/studio_gate.py", "run: true"), good_full)))
    check("workflows: skipped UX cannot be replaced by a constant green",
          bool(check_workflows(good_ci.replace("${{ needs.studio-ux-acceptance.result }}", "success"), good_full)))
    check("workflows: a docs-only return before Studio verdict fails",
          bool(check_workflows(good_ci.replace("    steps:", "    # DOCS_ONLY premature return\n    steps:"), good_full)))
    check("workflows: a comment that mentions `labeled` is not a trigger",
          not check_workflows(good_ci, "# There is no `labeled` trigger here.\n" + good_full))
    check("workflows: a labeled trigger fails",
          bool(check_workflows(good_ci, good_full.replace("ready_for_review]", "ready_for_review, labeled]"))))
    check("workflows: a Full tier for pull requests and the merge queue passes", not check_workflows(good_ci, queue_full))
    check("workflows: a Full tier for pull requests only fails",
          bool(check_workflows(good_ci, queue_full.replace(" || github.event_name == 'merge_group'", ""))))
    check("workflows: no merge_group trigger fails",
          bool(check_workflows(good_ci.replace("  merge_group:\n    types: [checks_requested]\n", ""), good_full)))
    check("workflows: a merge_group trigger without checks_requested fails",
          bool(check_workflows(good_ci.replace("[checks_requested]", "[destroyed]"), good_full)))
    check("workflows: a merge_group mention in a comment is not a trigger",
          bool(check_workflows(good_ci.replace("  merge_group:\n    types: [checks_requested]\n",
                                               "  # merge_group:\n  #   types: [checks_requested]\n"), good_full)))
    check("workflows: a Full tier that ignores a failed plan fails",
          bool(check_workflows(good_ci, good_full.replace("needs.plan-full.result != 'success' || ", ""))))

    # Initiative branches: GitHub's expression rules, then the real ci.yml in the three directions and its regressions.
    names = {"a": "", "b": "x", "t": True, "f": False}.get
    check("expressions: && and || return an operand", evaluate("a || b", names) == "x" and evaluate("b && a", names) == "")
    check("expressions: == ignores case and ! binds tightest",
          evaluate("'Main' == 'main' && !f", names) is True and evaluate("!t || t", names) is True)
    check("expressions: an unknown token is refused", _raises(lambda: evaluate("a ~ b", names), ExpressionError))
    chain = ("on:\n  push:\njobs:\n  a:\n    name: A\n  b:\n    needs: a\n    if: ${{ !cancelled() }}\n"
             "  c:\n    needs: b\n  d:\n    needs: [b]\n    if: ${{ !cancelled() }}\n")
    ran = simulate(chain, "push", {}, failed=frozenset({"a"}))
    check("simulate: a failed ancestor skips a job without a status function (actions/runner#491)",
          ran == {"a": True, "b": True, "c": False, "d": True})
    real_ci = _read_file(CI) or ""
    check("initiative: the real ci.yml passes", bool(real_ci) and not check_initiative_gate(real_ci))
    if real_ci:
        def starts(event: str, outputs: dict, draft: bool = False) -> set[str]:
            return {job for job, runs in simulate(real_ci, event, outputs, draft=draft).items() if runs}

        check("initiative: a docs-only PR to an initiative branch starts only the gate",
              starts("pull_request", {GATE_JOB: {"run": "false"}}) == {GATE_JOB}
              and starts("pull_request", {GATE_JOB: {"run": "false"}}, draft=True) == {GATE_JOB})
        check("initiative: a code PR to an initiative branch starts the PR and full tiers",
              set(TIER_JOBS) <= starts("pull_request", {GATE_JOB: {"run": "true"}, **_CODE_OUTPUTS}))
        for event in ("pull_request", "merge_group", "push", "schedule"):
            check(f"initiative: run=true on {event} (main) starts what a silent gate does",
                  starts(event, {GATE_JOB: {"run": "true"}, **_CODE_OUTPUTS}) - {GATE_JOB}
                  == starts(event, _CODE_OUTPUTS) - {GATE_JOB})
        gate_if = "if: ${{ !cancelled() && needs.initiative-gate.outputs.run != 'false' }}\n"
        regressions = {
            "a job that starts without the gate": real_ci.replace("    needs: initiative-gate\n    " + gate_if, "", 1),
            "an aggregate that ignores the gate": real_ci.replace(
                "    if: ${{ !cancelled() && needs.initiative-gate.outputs.run != 'false' && (github.event_name == 'pull_request'",
                "    if: ${{ !cancelled() && (github.event_name == 'pull_request'"),
            "jobs that need run == 'true' (a failed gate would skip required checks)":
                real_ci.replace("needs.initiative-gate.outputs.run != 'false'", "needs.initiative-gate.outputs.run == 'true'"),
            "no initiative/** trigger": real_ci.replace("      - 'initiative/**'\n", ""),
            "no gate job": real_ci.replace("  initiative-gate:\n", "  initiative-gate-renamed:\n"),
            "a required context that a failed gate skips": real_ci.replace(
                "    needs: initiative-gate\n    if: ${{ !cancelled() && needs.initiative-gate.outputs.run != 'false' && "
                "(github.event_name != 'workflow_dispatch' || inputs.suite == 'ci') }}\n    runs-on: ubuntu-latest\n"
                "    timeout-minutes: 15",
                "    needs: initiative-gate\n    if: ${{ needs.initiative-gate.outputs.run != 'false' && "
                "(github.event_name != 'workflow_dispatch' || inputs.suite == 'ci') }}\n    runs-on: ubuntu-latest\n"
                "    timeout-minutes: 15"),
            "a renamed gate job": real_ci.replace("    name: Initiative gate\n", "    name: Initiative check\n"),
        }
        for name, text in regressions.items():
            check(f"initiative: {name} fails", text != real_ci and bool(check_initiative_gate(text)))

    docs = {
        MERGING: f"# Merging\n\n## CI tiers\n\nThe PR tier and the full tier, per {MANIFEST}. Open a draft, then "
                 f"`gh pr ready <n>`. Run `{DISPATCH}`. A PR to `initiative/*`: `{GATE_NAME}`.\n\n## Review and merge\n",
        CI: "      tier:\n        description: Which tier\n        type: choice\n        options: [full, pr]\n",
        **{skill: f"See [CI tiers](../../../docs/contributing/{DOCS_ANCHOR})." for skill in SKILLS},
    }
    check("docs: the real shape passes", not check_docs(docs.get))
    check("docs: a missing skill link fails",
          bool(check_docs(lambda p: "no link" if p == SKILLS[1] else docs.get(p))))
    check("docs: a renamed dispatch option fails",
          bool(check_docs(lambda p: docs[CI].replace("[full, pr]", "[everything, pr]") if p == CI else docs.get(p))))
    check("docs: a missing section fails", bool(check_docs(lambda p: "# Merging\n" if p == MERGING else docs.get(p))))
    check("docs: a section without the initiative gate fails",
          bool(check_docs(lambda p: docs[MERGING].replace(GATE_NAME, "the gate") if p == MERGING else docs.get(p))))
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
    sel.add_argument("--event", help="the GitHub event; only pull_request narrows the shards to affected projects")
    sel.add_argument("--github-output")
    sel.add_argument("--summary")
    ent = sub.add_parser("entry")
    ent.add_argument("--shard", required=True)
    ent.add_argument("--manifest", default=MANIFEST)
    source = ent.add_mutually_exclusive_group(required=True)
    source.add_argument("--slnf")
    source.add_argument("--project")
    sub.add_parser("check-workflows")
    sub.add_parser("check-docs")
    args = parser.parse_args()

    if args.self_test:
        return self_test()
    try:
        if args.command == "select":
            return run_select(args)
        if args.command == "entry":
            with open(args.manifest, encoding="utf-8") as handle:
                manifest = load(handle.read())
            projects = [args.project] if args.project else slnf_projects(_read_file(args.slnf) or "")
            print(json.dumps(job_entry(manifest, args.shard, projects)))
            return 0
        if args.command in ("check-workflows", "check-docs"):
            if args.command == "check-workflows":
                problems = check_workflows(_read_file(CI) or "", _read_file(CI_FULL) or "")
                problems += check_initiative_gate(_read_file(CI) or "")
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
