#!/usr/bin/env python3
"""Deterministic checks for an initiative's pre-merge gate (docs/contributing/initiatives.md#the-pre-merge-gate).

Run it on the tree to be merged: the initiative branch with `main` merged in, or a proposal PR's head with the
initiative branch merged in, so that `main`'s newest ADRs and specs are in the tree. It looks only at ADRs
(`docs/adr/NNNN-*.md`; the closed `EB-` series is out of scope) and specs (`specs/NNN[N]-*/`) that this branch added
or changed since it forked from `--base` (default `origin/main`).

Errors (exit code 1):
  - number collision: an ADR or spec number now used by more than one slug, where at least one of those
    (number, slug) pairs did not exist at the fork point. The duplicates `main` already carries are not reported.
  - status line: a new or changed ADR has no status (a `Status:` line, a `status:` front-matter key, or a
    `## Status` heading followed by a value), or a new or changed spec.md has no `**Status**:` line holding exactly
    one spec-lifecycle value, optionally followed by ` — ` and prose.
  - alternatives: a new or changed ADR has no `Alternatives considered` (or `Options considered`) section, or that
    section is empty. This is a presence check, like the status line; it never judges whether the alternatives are
    good. Specs do not carry their own section: they refer to the alternatives in the ADRs they apply.
Warnings (exit code 0): `ADR NNNN` references in new or changed docs, specs, CONTEXT.md or constitution files that
match no ADR in the tree. A reference can name a number the board has reserved but that is not drafted yet, so it is
reported, not failed.

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
ADR_STATUS_LINE = re.compile(r"^[ \t>*-]*(?:\*\*)?status(?:\*\*)?[ \t]*:(?:\*\*)?[ \t]*[^\s*]", re.IGNORECASE | re.MULTILINE)
ADR_STATUS_HEADING = re.compile(r"^#+[ \t]*status[ \t]*\n(?:[ \t]*\n)*[ \t]*[^\s#]", re.IGNORECASE | re.MULTILINE)
SPEC_STATUS = re.compile(r"^\*\*Status\*\*:\s*(?P<value>.+?)\s*$", re.MULTILINE)
SPEC_STATUS_VALUE = re.compile(
    r"^(?:Draft|Approved|In progress|Implemented|Superseded|Abandoned)(?:\s+—\s+.*)?$", re.IGNORECASE)
ADR_REFERENCE = re.compile(r"\bADRs?[\s-]+(?P<list>\d{4}(?:\s*(?:,|/|&|\band\b|\bor\b)\s*\d{4})*)")
ADR_ALTERNATIVES_HEADING = re.compile(
    r"^(?P<hashes>#+)[ \t]*(?:alternatives|options[ \t]+considered)\b.*$", re.IGNORECASE | re.MULTILINE)
STATUS_WINDOW = 25  # lines from the top of an ADR in which its status must appear
REFERENCE_SCOPE = ("docs", "specs", "CONTEXT.md", ".specify/memory")


def adr_number(path: str) -> tuple[str, str] | None:
    match = ADR_FILE.match(path)
    return (match["number"], match["slug"]) if match else None


def spec_number(path: str) -> tuple[str, str] | None:
    match = SPEC_DIR.match(path)
    return (match["number"].zfill(4), match["slug"]) if match else None


def collisions(tree_paths: list[str], base_paths: list[str], key, kind: str) -> list[str]:
    """Numbers shared by different slugs, where at least one (number, slug) pair is new since the fork point."""
    by_number: dict[str, set[str]] = {}
    for path in tree_paths:
        found = key(path)
        if found:
            by_number.setdefault(found[0], set()).add(found[1])
    at_fork = {found for found in map(key, base_paths) if found}
    return [
        f"number collision: {kind} {number} is used by {', '.join(sorted(slugs))}"
        for number, slugs in sorted(by_number.items())
        if len(slugs) > 1 and any((number, slug) not in at_fork for slug in slugs)
    ]


def adr_status_error(path: str, text: str) -> str | None:
    head = "\n".join(text.splitlines()[:STATUS_WINDOW])
    if ADR_STATUS_LINE.search(head) or ADR_STATUS_HEADING.search(head):
        return None
    return f"status line: {path} has no status in its first {STATUS_WINDOW} lines"


def spec_status_error(path: str, text: str) -> str | None:
    match = SPEC_STATUS.search(text)
    if match is None:
        return f"status line: {path} has no **Status**: line"
    if not SPEC_STATUS_VALUE.match(match["value"]):
        return f"status line: {path} has status '{match['value']}', not one spec lifecycle value (then ' — ' prose)"
    return None


def adr_alternatives_error(path: str, text: str) -> str | None:
    match = ADR_ALTERNATIVES_HEADING.search(text)
    if match is None:
        return f"alternatives: {path} has no 'Alternatives considered' section"
    level = len(match["hashes"])
    for line in text[match.end():].splitlines():
        stripped = line.strip()
        if not stripped:
            continue
        heading = re.match(r"^(#+)\s", line)
        if heading and len(heading[1]) <= level:
            break  # a sibling or parent heading with nothing in between: the section is empty
        return None  # prose or a deeper subheading counts as content
    return f"alternatives: {path} has an empty 'Alternatives considered' section"


def unresolved_references(path: str, text: str, adr_numbers: set[str]) -> list[str]:
    cited = {number for m in ADR_REFERENCE.finditer(text) for number in re.findall(r"\d{4}", m["list"])}
    return [f"reference: {path} cites ADR {number}, which is not in docs/adr/" for number in sorted(cited - adr_numbers)]


def check(tree: dict[str, str], changed: set[str], base_paths: list[str]) -> tuple[list[str], list[str]]:
    """tree maps every path in scope to its text (empty for files that are not read); changed is the paths new or
    changed since the fork point; base_paths is the docs/adr and specs paths at the fork point. Returns
    (errors, warnings)."""
    paths = sorted(tree)
    errors = collisions(paths, base_paths, adr_number, "ADR") + collisions(paths, base_paths, spec_number, "spec")
    warnings: list[str] = []
    adr_numbers = {found[0] for found in map(adr_number, paths) if found}
    for path in sorted(changed):
        if path not in tree:
            continue  # deleted
        text = tree[path]
        if adr_number(path):
            errors.extend(error for error in (adr_status_error(path, text), adr_alternatives_error(path, text))
                          if error)
        elif spec_number(path) and path.endswith("/spec.md"):
            error = spec_status_error(path, text)
            if error:
                errors.append(error)
        if path.endswith(".md"):
            warnings.extend(unresolved_references(path, text, adr_numbers))
    return errors, warnings


def git_lines(root: Path, *args: str) -> list[str]:
    result = subprocess.run(["git", *args], cwd=root, capture_output=True, text=True, check=True)
    return [line for line in result.stdout.splitlines() if line]


def load(root: Path, base: str) -> tuple[dict[str, str], set[str], list[str]]:
    fork_point = git_lines(root, "merge-base", base, "HEAD")[0]
    numbered = ("docs/adr", "specs")
    tracked = git_lines(root, "ls-files", *numbered)
    untracked = git_lines(root, "ls-files", "--others", "--exclude-standard", *REFERENCE_SCOPE)
    changed = set(git_lines(root, "diff", "--name-only", "--no-renames", fork_point, "--", *REFERENCE_SCOPE))
    changed |= set(untracked)
    base_paths = git_lines(root, "ls-tree", "-r", "--name-only", fork_point, "--", *numbered)
    tree: dict[str, str] = {path: "" for path in tracked if (root / path).is_file()}
    for path in changed:
        full = root / path
        if path.endswith(".md") and full.is_file():
            tree[path] = full.read_text(encoding="utf-8", errors="replace")
        elif full.is_file() and path.startswith(numbered):
            tree.setdefault(path, "")
    return tree, changed, base_paths


def self_test() -> int:
    alts = "\n## Alternatives considered\n\n- Do nothing: rejected because the gap is real.\n"
    adr_ok = "# ADR\n\nStatus: proposed (2026-11-06)\n" + alts
    spec_ok = "# Spec\n\n**Status**: Draft — **provisional**. Waits on ADR 0091.\n"
    legacy = ["specs/1203-a/spec.md", "specs/1203-b/spec.md"]
    cases = [
        ("clean", {"docs/adr/0091-a.md": adr_ok, "specs/1207-x/spec.md": spec_ok}, {"docs/adr/0091-a.md"}, [], 0, 0),
        ("front matter status", {"docs/adr/0091-a.md": "---\nstatus: accepted\n---\n# ADR\n" + alts},
         {"docs/adr/0091-a.md"}, [], 0, 0),
        ("status heading", {"docs/adr/0020-a.md": "# ADR\n\n## Status\n\nAccepted\n" + alts},
         {"docs/adr/0020-a.md"}, [], 0, 0),
        ("status list item", {"docs/adr/0044-a.md": "# ADR\n\n- Status: Accepted\n" + alts},
         {"docs/adr/0044-a.md"}, [], 0, 0),
        ("adr collision with main", {"docs/adr/0091-a.md": adr_ok, "docs/adr/0091-b.md": ""},
         {"docs/adr/0091-a.md"}, ["docs/adr/0091-b.md"], 1, 0),
        ("legacy duplicate untouched", dict.fromkeys(legacy, ""), set(), legacy, 0, 0),
        ("legacy duplicate amended", {legacy[0]: "**Status**: Implemented\n", legacy[1]: ""}, {legacy[0]}, legacy, 0, 0),
        ("spec collision", {"specs/1201-a/spec.md": spec_ok, "specs/1201-b/spec.md": ""},
         {"specs/1201-a/spec.md"}, ["specs/1201-b/spec.md"], 1, 1),
        ("three-digit spec number", {"specs/095-a/spec.md": spec_ok, "specs/0095-b/spec.md": ""},
         {"specs/095-a/spec.md"}, ["specs/0095-b/spec.md"], 1, 1),
        ("missing adr status", {"docs/adr/0092-a.md": "# ADR\n\nNo status here.\n" + alts},
         {"docs/adr/0092-a.md"}, [], 1, 0),
        ("empty status line", {"docs/adr/0092-a.md": "# ADR\n\nStatus:\n\n## Context\n" + alts},
         {"docs/adr/0092-a.md"}, [], 1, 0),
        ("empty status heading", {"docs/adr/0092-a.md": "# ADR\n\n## Status\n\n## Context\n" + alts},
         {"docs/adr/0092-a.md"}, [], 1, 0),
        ("bold status line", {"docs/adr/0092-a.md": "# ADR\n\n**Status:** Accepted\n" + alts},
         {"docs/adr/0092-a.md"}, [], 0, 0),
        ("missing alternatives", {"docs/adr/0092-a.md": "# ADR\n\nStatus: accepted\n"}, {"docs/adr/0092-a.md"}, [], 1, 0),
        ("empty alternatives section",
         {"docs/adr/0092-a.md": "# ADR\n\nStatus: accepted\n\n## Alternatives considered\n\n## Next steps\n\nx\n"},
         {"docs/adr/0092-a.md"}, [], 1, 0),
        ("options considered accepted",
         {"docs/adr/0092-a.md": "# ADR\n\nStatus: accepted\n\n## Options considered\n\n- A vs B.\n"},
         {"docs/adr/0092-a.md"}, [], 0, 0),
        ("alternatives subheading counts",
         {"docs/adr/0092-a.md": "# ADR\n\nStatus: accepted\n\n## Alternatives considered\n\n### Option A\n\nRejected.\n"},
         {"docs/adr/0092-a.md"}, [], 0, 0),
        ("bad spec status", {"specs/1208-x/spec.md": "**Status**: Pending\n"}, {"specs/1208-x/spec.md"}, [], 1, 0),
        ("spec status prefix only", {"specs/1208-x/spec.md": "**Status**: Drafting\n"}, {"specs/1208-x/spec.md"}, [], 1, 0),
        ("missing spec status", {"specs/1208-x/spec.md": "# Spec\n"}, {"specs/1208-x/spec.md"}, [], 1, 0),
        ("unresolved reference", {"docs/adr/0093-a.md": adr_ok + "Amends ADR 0045.\n"}, {"docs/adr/0093-a.md"}, [], 0, 1),
        ("reference list", {"CONTEXT.md": "See ADRs 0005 and 0007, ADR-0029.\n"}, {"CONTEXT.md"}, [], 0, 3),
        ("resolved reference", {"docs/adr/0093-a.md": adr_ok + "Amends ADR 0045.\n", "docs/adr/0045-v.md": ""},
         {"docs/adr/0093-a.md"}, [], 0, 0),
        ("deleted file", {"docs/adr/0091-a.md": ""}, {"docs/adr/0090-gone.md"}, [], 0, 0),
    ]
    failures = 0
    for name, tree, changed, base_paths, want_errors, want_warnings in cases:
        errors, warnings = check(tree, changed, base_paths)
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

    try:
        root = Path(git_lines(Path(args.root), "rev-parse", "--show-toplevel")[0])
        tree, changed, base_paths = load(root, args.base)
    except (subprocess.CalledProcessError, OSError, IndexError) as error:
        print(f"check_decision_docs: could not read the repository at {args.root} against {args.base}: {error}",
              file=sys.stderr)
        return 2

    errors, warnings = check(tree, changed, base_paths)
    for line in errors:
        print(f"ERROR {line}")
    for line in warnings:
        print(f"WARN  {line}")
    print(f"check_decision_docs: {root}, {len(changed)} changed path(s) since the fork point with {args.base}, "
          f"{len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
