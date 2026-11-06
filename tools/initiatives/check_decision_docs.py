#!/usr/bin/env python3
"""Deterministic checks for an initiative's pre-merge gate (docs/contributing/initiatives.md#the-pre-merge-gate).

Run it on the tree to be merged: the initiative branch with `main` merged in, or a proposal PR's head with the
initiative branch merged in, so that `main`'s newest ADRs and specs are in the tree. It looks only at ADRs
(`docs/adr/NNNN-*.md`) and specs (`specs/NNNN-*/`) that this branch added or changed since it forked from `--base`
(default `origin/main`), so the duplicates `main` already carries are not reported again.

Errors (exit code 1):
  - number collision: a new or changed ADR or spec folder shares its number with a different slug;
  - status line: a new or changed ADR has no status, or a new or changed spec has no `**Status**:` line from the
    spec lifecycle vocabulary.
Warnings (exit code 0): `ADR NNNN` references in new or changed files that match no ADR in the tree. A reference can
name a number the board has reserved but that is not drafted yet, so it is reported, not failed.

These checks never judge content. Contradiction, ambiguity and overlap are for the gate's reviewers.

Usage:
  check_decision_docs.py [--base <ref>] [--root <dir>]
  check_decision_docs.py --self-test
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

ADR_FILE = re.compile(r"^docs/adr/(?P<number>\d{4})-(?P<slug>[^/]+)\.md$")
SPEC_DIR = re.compile(r"^specs/(?P<number>\d{3,4})-(?P<slug>[^/]+)/")
ADR_STATUS = re.compile(r"^(?:status:\s*\S|\**Status\**:\s*\S)", re.IGNORECASE | re.MULTILINE)
SPEC_STATUS = re.compile(r"^\*\*Status\*\*:\s*(?P<value>.+)$", re.MULTILINE)
SPEC_STATUSES = ("draft", "approved", "in progress", "implemented", "superseded", "abandoned")
ADR_REFERENCE = re.compile(r"\bADRs?\s+(?P<number>\d{4})\b")
STATUS_WINDOW = 25  # lines from the top of an ADR in which its status must appear


def adr_number(path: str) -> tuple[str, str] | None:
    match = ADR_FILE.match(path)
    return (match["number"], match["slug"]) if match else None


def spec_number(path: str) -> tuple[str, str] | None:
    match = SPEC_DIR.match(path)
    return (match["number"].zfill(4), match["slug"]) if match else None


def collisions(tree_paths: list[str], changed: set[str], key) -> list[str]:
    """Numbers shared by different slugs, where at least one of those slugs comes from a changed path."""
    by_number: dict[str, set[str]] = {}
    changed_slugs: set[tuple[str, str]] = set()
    for path in tree_paths:
        found = key(path)
        if found is None:
            continue
        by_number.setdefault(found[0], set()).add(found[1])
        if path in changed:
            changed_slugs.add(found)
    errors = []
    for number, slugs in sorted(by_number.items()):
        if len(slugs) > 1 and any((number, slug) in changed_slugs for slug in slugs):
            errors.append(f"number collision: {number} is used by {', '.join(sorted(slugs))}")
    return errors


def adr_status_error(path: str, text: str) -> str | None:
    head = "\n".join(text.splitlines()[:STATUS_WINDOW])
    return None if ADR_STATUS.search(head) else f"status line: {path} has no status in its first {STATUS_WINDOW} lines"


def spec_status_error(path: str, text: str) -> str | None:
    match = SPEC_STATUS.search(text)
    if match is None:
        return f"status line: {path} has no **Status**: line"
    value = match["value"].strip().lower()
    if not value.startswith(SPEC_STATUSES):
        return f"status line: {path} has status '{match['value'].strip()}', not one of the spec lifecycle values"
    return None


def unresolved_references(path: str, text: str, adr_numbers: set[str]) -> list[str]:
    missing = sorted({m["number"] for m in ADR_REFERENCE.finditer(text)} - adr_numbers)
    return [f"reference: {path} cites ADR {number}, which is not in docs/adr/" for number in missing]


def check(tree: dict[str, str], changed: set[str]) -> tuple[list[str], list[str]]:
    """tree maps every repository path to its text (empty for files that are not read); changed is the new or
    changed paths. Returns (errors, warnings)."""
    paths = sorted(tree)
    errors = collisions(paths, changed, adr_number) + collisions(paths, changed, spec_number)
    warnings: list[str] = []
    adr_numbers = {found[0] for found in map(adr_number, paths) if found}
    for path in sorted(changed):
        if path not in tree:
            continue  # deleted
        text = tree[path]
        if adr_number(path):
            error = adr_status_error(path, text)
        elif spec_number(path) and path.endswith("/spec.md"):
            error = spec_status_error(path, text)
        else:
            error = None
        if error:
            errors.append(error)
        if path.endswith(".md") and (path.startswith("docs/") or path.startswith("specs/")):
            warnings.extend(unresolved_references(path, text, adr_numbers))
    return errors, warnings


def git_lines(root: Path, *args: str) -> list[str]:
    result = subprocess.run(["git", *args], cwd=root, capture_output=True, text=True, check=True)
    return [line for line in result.stdout.splitlines() if line]


def load(root: Path, base: str) -> tuple[dict[str, str], set[str]]:
    tracked = git_lines(root, "ls-files", "docs/adr", "specs")
    untracked = git_lines(root, "ls-files", "--others", "--exclude-standard", "docs/adr", "specs")
    fork_point = git_lines(root, "merge-base", base, "HEAD")[0]
    changed = set(git_lines(root, "diff", "--name-only", "--no-renames", fork_point, "--", "docs", "specs"))
    changed |= set(untracked)
    tree: dict[str, str] = {}
    for path in tracked + untracked:
        full = root / path
        if not full.is_file():
            continue
        wanted = path in changed and path.endswith(".md")
        tree[path] = full.read_text(encoding="utf-8", errors="replace") if wanted else ""
    for path in changed:  # changed docs outside docs/adr and specs, for the reference check
        full = root / path
        if path not in tree and path.endswith(".md") and full.is_file():
            tree[path] = full.read_text(encoding="utf-8", errors="replace")
    return tree, changed


def self_test() -> int:
    adr_ok = "# ADR\n\nStatus: proposed (2026-11-06)\n"
    adr_front = "---\nstatus: accepted\n---\n# ADR\n"
    spec_ok = "# Spec\n\n**Status**: Draft — **provisional**. Waits on ADR 0091.\n"
    cases = [
        ("clean", {"docs/adr/0091-a.md": adr_ok, "specs/1207-x/spec.md": spec_ok}, {"docs/adr/0091-a.md"}, 0, 0),
        ("frontmatter status", {"docs/adr/0091-a.md": adr_front}, {"docs/adr/0091-a.md"}, 0, 0),
        ("adr collision with main", {"docs/adr/0091-a.md": adr_ok, "docs/adr/0091-b.md": ""},
         {"docs/adr/0091-a.md"}, 1, 0),
        ("legacy duplicate ignored", {"specs/1203-a/spec.md": "", "specs/1203-b/spec.md": ""}, set(), 0, 0),
        ("spec collision", {"specs/1201-a/spec.md": spec_ok, "specs/1201-b/spec.md": ""},
         {"specs/1201-a/spec.md"}, 1, 1),
        ("three-digit spec number", {"specs/095-a/spec.md": spec_ok, "specs/0095-b/spec.md": ""},
         {"specs/095-a/spec.md"}, 1, 1),
        ("missing adr status", {"docs/adr/0092-a.md": "# ADR\n\nNo status here.\n"}, {"docs/adr/0092-a.md"}, 1, 0),
        ("bad spec status", {"specs/1208-x/spec.md": "**Status**: Pending\n"}, {"specs/1208-x/spec.md"}, 1, 0),
        ("missing spec status", {"specs/1208-x/spec.md": "# Spec\n"}, {"specs/1208-x/spec.md"}, 1, 0),
        ("unresolved reference", {"docs/adr/0093-a.md": adr_ok + "Amends ADR 0045.\n"}, {"docs/adr/0093-a.md"}, 0, 1),
        ("resolved reference", {"docs/adr/0093-a.md": adr_ok + "Amends ADR 0045.\n", "docs/adr/0045-v.md": ""},
         {"docs/adr/0093-a.md"}, 0, 0),
        ("deleted file", {"docs/adr/0091-a.md": ""}, {"docs/adr/0090-gone.md"}, 0, 0),
    ]
    failures = 0
    for name, tree, changed, want_errors, want_warnings in cases:
        errors, warnings = check(tree, changed)
        if len(errors) != want_errors or len(warnings) != want_warnings:
            failures += 1
            print(f"check_decision_docs self-test '{name}': got {errors} / {warnings}", file=sys.stderr)
    if failures:
        return 1
    print(f"check_decision_docs self-test: {len(cases)} cases pass")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--base", default="origin/main")
    parser.add_argument("--root", default=".")
    args = parser.parse_args()

    if args.self_test:
        return self_test()

    root = Path(args.root)
    try:
        tree, changed = load(root, args.base)
    except (subprocess.CalledProcessError, OSError) as error:
        print(f"check_decision_docs: could not read the tree against {args.base}: {error}", file=sys.stderr)
        return 2

    errors, warnings = check(tree, changed)
    for line in errors:
        print(f"ERROR {line}")
    for line in warnings:
        print(f"WARN  {line}")
    print(f"check_decision_docs: {len(changed)} changed path(s) against {args.base}, "
          f"{len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
