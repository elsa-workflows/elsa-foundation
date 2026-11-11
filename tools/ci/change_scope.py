#!/usr/bin/env python3
"""Classify a pull request's changes as documentation-only or not, for the CI fast path (spec 257).

"Build & test" runs this first. When every changed path is documentation, specs or agent instructions, and none is
a file a build or test reads, the job skips the .NET build and test run. It still runs the cheap Python guards and
the catalog link check. Anything uncertain (a git error, an empty diff, a non-PR event, an unlisted path) gives a
full run: this script can widen CI, never narrow it by accident.

The same classifier gates pull requests to an initiative branch (docs/contributing/initiatives.md): ADR, spec and
agenda PRs there run no tier (only the gate job itself), and only a PR that carries implementation runs the tiers.
`--gate` writes `run=` for the `Initiative gate` job: false only for a documentation-only pull request whose base is
`initiative/*`; every other event or base, and any diff it cannot read, gives true, so main's CI is never narrowed by
it.

Usage:
  change_scope.py --base <sha> --head <sha> [--github-output <file>] [--summary <file>]
  change_scope.py --gate --event <name> --base-ref <ref> --base <sha> --head <sha> [--github-output <file>] [--summary <file>]
  change_scope.py --self-test
"""

from __future__ import annotations

import argparse
import fnmatch
import subprocess
import sys
from collections.abc import Callable
from pathlib import PurePosixPath

# A path is documentation-only when it matches one of these...
INCLUDE = (
    "docs/**",
    "specs/**",
    "Studio/specs/**",
    ".specify/**",
    "Studio/.specify/**",
    ".agents/**",
    ".claude/**",
    "Studio/.agents/**",
    "Studio/.claude/**",
    # Standalone pre-merge gate tooling (python, run by hand or by the initiative skills); nothing a build reads. Accepted
    # gap: RetiredPersistenceFamilyGuardTests scans tools/ for retired persistence-family names in .py/.json/.yml/.sh files,
    # so a change here is no longer checked by it before merge; it would only fail on the push to main.
    "tools/initiatives/**",
    "*.md",  # root-level only; see _matches
)

# ...and none of these. Each is a file a build or test reads, so changing it needs the full run.
EXCLUDE = (
    "EXTENSION_POINTS.md",  # CatalogLinkGuardTests and the extension-point map layer read the root catalog
    "specs/*/contracts/**",  # ManagementApiOperationInventoryTests reads a spec's OpenAPI contract
)

# Repository-wide sweeps (EfCoreDependencyGuardTests, RetiredTermEnforcementGuardTests, RetiredTermAnalyzerTests)
# read build and analyzer config wherever it lives, including under docs or agent directories.
BUILD_INPUT_NAMES = (".editorconfig",)
BUILD_INPUT_SUFFIXES = (".props", ".targets", ".csproj", ".cs", ".ruleset", ".globalconfig", ".sln", ".slnx", ".slnf")


def _matches(path: str, pattern: str) -> bool:
    if pattern == "*.md":
        return "/" not in path and path.endswith(".md")
    if pattern.endswith("/**"):
        prefix = pattern[:-3]
        if "*" in prefix:
            parts, pattern_parts = path.split("/"), prefix.split("/")
            return len(parts) > len(pattern_parts) and all(
                fnmatch.fnmatchcase(part, wanted) for part, wanted in zip(parts, pattern_parts)
            )
        return path.startswith(prefix + "/")
    return path == pattern


def is_build_input(path: str) -> bool:
    name = PurePosixPath(path).name
    return name in BUILD_INPUT_NAMES or name.endswith(BUILD_INPUT_SUFFIXES)


def first_full_run_path(paths: list[str]) -> str | None:
    """The first path that forces a full run, or None when every path is documentation-only."""
    for path in paths:
        if (
            is_build_input(path)
            or any(_matches(path, pattern) for pattern in EXCLUDE)
            or not any(_matches(path, pattern) for pattern in INCLUDE)
        ):
            return path
    return None


def classify(paths: list[str]) -> tuple[bool, str]:
    if not paths:
        return False, "full run: the diff is empty or could not be read"
    blocker = first_full_run_path(paths)
    if blocker is not None:
        return False, f"full run: `{blocker}` is not documentation-only"
    return True, f"documentation-only: {len(paths)} changed path(s); .NET build and tests skipped"


INITIATIVE_BASE_PREFIX = "initiative/"


def is_initiative_pr(event_name: str, base_ref: str) -> bool:
    return event_name == "pull_request" and base_ref.startswith(INITIATIVE_BASE_PREFIX)


def gate(event_name: str, base_ref: str, paths: Callable[[], list[str]]) -> tuple[bool, str]:
    """Whether CI runs at all. Only a documentation-only pull request to an initiative branch is turned away; `paths`
    is read only for such a pull request, so every other run starts exactly as before."""
    if not is_initiative_pr(event_name, base_ref):
        return True, f"CI runs: not a pull request to an `{INITIATIVE_BASE_PREFIX}*` branch"
    docs_only, reason = classify(paths())
    if docs_only:
        return False, (f"CI skipped: a documentation-only pull request to `{base_ref}` (decision documents run no CI; "
                       f"docs/contributing/merging.md#ci-tiers)")
    return True, f"CI runs: this pull request to `{base_ref}` carries implementation ({reason})"


def changed_paths(base: str, head: str) -> list[str]:
    result = subprocess.run(
        ["git", "diff", "--name-only", "--no-renames", f"{base}...{head}"],
        capture_output=True, text=True, check=True,
    )
    return [line for line in result.stdout.splitlines() if line]


def self_test() -> int:
    cases = [
        (["docs/x.md"], True),
        (["README.md", "AGENTS.md"], True),
        (["specs/257-x/spec.md", ".claude/skills/a/SKILL.md", ".specify/templates/t.md"], True),
        (["Studio/specs/004-x/tasks.md"], True),
        (["docs/maps/README.md"], True),
        (["EXTENSION_POINTS.md"], False),
        (["Essentials/src/Tasks/Elsa.Tasks/packages.lock.json"], False),
        (["specs/220-domain-owned-apis/contracts/management-api.openapi.yaml"], False),
        ([".claude/x/.editorconfig"], False),
        (["docs/Directory.Build.props"], False),
        (["docs/samples/Sample.cs"], False),
        (["Essentials/README.md"], False),
        (["Essentials/src/Api/EXTENSION_POINTS.md"], False),
        ([".github/workflows/ci.yml"], False),
        (["tools/maps/Elsa.Maps.Generator/Program.cs"], False),
        (["docs/x.md", "Essentials/src/X.cs"], False),
        (["tools/initiatives/check_decision_docs.py"], True),
        (["tools/initiatives/check_decision_docs.py", "docs/adr/0001-x.md"], True),
        (["tools/initiatives/Directory.Build.props"], False),
        (["tools/initiatives/Gate.cs"], False),
        (["tools/ci/change_scope.py"], False),
        ([], False),
    ]
    failures = [(paths, want) for paths, want in cases if classify(paths)[0] != want]
    for paths, want in failures:
        print(f"change_scope self-test: {paths} should be docs_only={str(want).lower()}", file=sys.stderr)

    def unread() -> list[str]:
        raise AssertionError("the diff is read only for a pull request to an initiative branch")

    base = "initiative/3052-elsa3-to-elsa4-conversion"
    gate_cases = [
        # (event, base ref, changed paths or None for "must not be read", expected run)
        ("pull_request", base, ["docs/adr/0093-x.md"], False),
        ("pull_request", base, ["specs/1221-x/spec.md", "specs/1221-x/plan.md", "specs/1221-x/tasks.md"], False),
        ("pull_request", base, ["docs/initiatives/3052/agenda-2026-11-11.md", "docs/contributing/initiatives.md"], False),
        ("pull_request", base, [".claude/skills/elsa-initiative/SKILL.md", ".agents/skills/x/SKILL.md"], False),
        ("pull_request", base, ["CONTEXT.md", "AGENTS.md", "docs/reference/glossary.md"], False),
        ("pull_request", base, ["tools/initiatives/check_decision_docs.py", "docs/adr/0093-x.md"], False),
        ("pull_request", base, ["docs/adr/0093-x.md", "Essentials/src/X/Y.cs"], True),
        ("pull_request", base, ["Essentials/src/X/packages.lock.json"], True),
        ("pull_request", base, ["EXTENSION_POINTS.md"], True),
        ("pull_request", base, ["specs/1221-x/contracts/api.openapi.yaml"], True),
        ("pull_request", base, [".github/workflows/ci.yml"], True),
        ("pull_request", base, [], True),  # an unreadable or empty diff runs CI
        ("pull_request", "main", None, True),
        ("pull_request", "release/4.0", None, True),
        ("pull_request", "feature/initiative/x", None, True),
        ("merge_group", "", None, True),
        ("push", "", None, True),
        ("schedule", "", None, True),
        ("workflow_dispatch", "", None, True),
    ]
    for event, ref, paths, want in gate_cases:
        try:
            run, _ = gate(event, ref, unread if paths is None else (lambda paths=paths: paths))
        except AssertionError as error:
            failures.append((paths, want))
            print(f"change_scope self-test: gate({event}, {ref!r}): {error}", file=sys.stderr)
            continue
        if run != want:
            failures.append((paths, want))
            print(f"change_scope self-test: gate({event}, {ref!r}, {paths}) should be run={str(want).lower()}",
                  file=sys.stderr)
    if failures:
        return 1
    print(f"change_scope self-test: {len(cases)} classifier and {len(gate_cases)} gate cases pass")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--gate", action="store_true", help="write run= for the Initiative gate job")
    parser.add_argument("--event", default="")
    parser.add_argument("--base-ref", default="")
    parser.add_argument("--base")
    parser.add_argument("--head")
    parser.add_argument("--github-output")
    parser.add_argument("--summary")
    args = parser.parse_args()

    if args.self_test:
        return self_test()

    def read_paths() -> list[str]:
        try:
            return changed_paths(args.base, args.head) if args.base and args.head else []
        except (subprocess.CalledProcessError, OSError) as error:
            print(f"change_scope: could not diff {args.base}...{args.head}: {error}", file=sys.stderr)
            return []

    if args.gate:
        run, reason = gate(args.event, args.base_ref, read_paths)
        print(reason)
        if args.github_output:
            with open(args.github_output, "a", encoding="utf-8") as output:
                output.write(f"run={'true' if run else 'false'}\n")
        if args.summary:
            with open(args.summary, "a", encoding="utf-8") as summary:
                summary.write(f"### Initiative gate\n\n{reason}\n")
        return 0

    docs_only, reason = classify(read_paths())
    print(reason)
    if args.github_output:
        with open(args.github_output, "a", encoding="utf-8") as output:
            output.write(f"docs_only={'true' if docs_only else 'false'}\n")
    if args.summary:
        with open(args.summary, "a", encoding="utf-8") as summary:
            summary.write(f"### Change scope\n\n{reason}\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
