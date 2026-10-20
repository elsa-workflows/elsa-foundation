#!/usr/bin/env python3
"""Classify a pull request's changes as documentation-only or not, for the CI fast path (spec 257).

"Build & test" runs this first. When every changed path is documentation, specs or agent instructions, and none is
a file a build or test reads, the job skips the .NET build and test run. It still runs the cheap Python guards and
the catalog link check. Anything uncertain (a git error, an empty diff, a non-PR event, an unlisted path) gives a
full run: this script can widen CI, never narrow it by accident.

Usage:
  change_scope.py --base <sha> --head <sha> [--github-output <file>] [--summary <file>]
  change_scope.py --self-test
"""

from __future__ import annotations

import argparse
import fnmatch
import subprocess
import sys
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
    "*.md",  # root-level only; see _matches
)

# ...and none of these. Each is a file a build or test reads, so changing it needs the full run.
EXCLUDE = (
    "EXTENSION_POINTS.md",  # CatalogLinkGuardTests and the extension-point map layer read the root catalog
    "docs/maps/dependency-map.json",  # DependencyMapTests, NuGetLockFileTests and the versioning calculator read it
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
        (["docs/maps/dependency-map.json"], False),
        (["specs/220-domain-owned-apis/contracts/management-api.openapi.yaml"], False),
        ([".claude/x/.editorconfig"], False),
        (["docs/Directory.Build.props"], False),
        (["docs/samples/Sample.cs"], False),
        (["Essentials/README.md"], False),
        (["Essentials/src/Api/EXTENSION_POINTS.md"], False),
        ([".github/workflows/ci.yml"], False),
        (["tools/maps/Elsa.Maps.Generator/Program.cs"], False),
        (["docs/x.md", "Essentials/src/X.cs"], False),
        ([], False),
    ]
    failures = [(paths, want) for paths, want in cases if classify(paths)[0] != want]
    for paths, want in failures:
        print(f"change_scope self-test: {paths} should be docs_only={str(want).lower()}", file=sys.stderr)
    if failures:
        return 1
    print(f"change_scope self-test: {len(cases)} cases pass")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--base")
    parser.add_argument("--head")
    parser.add_argument("--github-output")
    parser.add_argument("--summary")
    args = parser.parse_args()

    if args.self_test:
        return self_test()

    try:
        paths = changed_paths(args.base, args.head) if args.base and args.head else []
    except (subprocess.CalledProcessError, OSError) as error:
        print(f"change_scope: could not diff {args.base}...{args.head}: {error}", file=sys.stderr)
        paths = []

    docs_only, reason = classify(paths)
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
