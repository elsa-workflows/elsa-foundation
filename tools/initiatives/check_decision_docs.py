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
  - alternatives: a newly added ADR has no `Alternatives considered` (or `Options considered`) section, or that
    section is empty. The heading may be a hash heading (`## Alternatives considered`) or a bold run-in label
    (`**Alternatives considered.**`) with content after it. This is a presence check, like the status line; it never
    judges whether the alternatives are good, and it runs only on ADRs this branch adds, not on a pre-existing ADR it
    merely amends. A heading or list quoted inside a fenced code block does not count. Specs do not carry their own
    section: they refer to the alternatives in the ADRs they apply.
  - timeless: a line this branch adds to an ADR, or to a committed meeting agenda under `docs/initiatives/`,
    @-mentions a person (`@login`). The document records the decision,
    never who holds a position or must act; names and routing belong on the PR and the board.
Warnings (exit code 0):
  - timeless: a line this branch adds to an ADR carries a PR or issue reference (`#1234`, a GitHub pull or issue
    URL), a dated heading, or an open-questions heading. These belong in the PR description. They are reported for
    the author to move, not failed, because an ADR may cite an issue as durable evidence.
  - `ADR NNNN` references in new or changed docs, specs, CONTEXT.md or constitution files that
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
    r"^(?P<hashes>#+)[ \t]*(?:alternatives|options)[ \t]+considered\b.*$", re.IGNORECASE | re.MULTILINE)
ADR_ALTERNATIVES_BOLD = re.compile(
    r"^[ \t>*-]*\*\*[ \t]*(?:alternatives|options)[ \t]+considered\b", re.IGNORECASE | re.MULTILINE)
MENTION = re.compile(r"(?<![\w.`/])@(?P<login>[A-Za-z0-9][A-Za-z0-9-]{0,38})\b")
ISSUE_REFERENCE = re.compile(r"(?<![\w&/])#\d{3,6}\b|github\.com/[\w.-]+/[\w.-]+/(?:pull|issues)/\d+")
DATED_HEADING = re.compile(r"^#+\s.*\b20\d\d-\d\d-\d\d\b")
OPEN_QUESTIONS_HEADING = re.compile(r"^#+\s.*\b(?:open questions|questions for (?:the )?reviewers)\b", re.IGNORECASE)
MEETING_DIR = "docs/initiatives/"  # committed meeting agendas: name-free like ADRs
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


def strip_fenced_code(text: str) -> str:
    """Drop fenced code blocks (``` or ~~~) so an example heading or list inside one is not read as an
    Alternatives section or its content."""
    kept: list[str] = []
    fence: str | None = None
    for line in text.splitlines(keepends=True):
        opener = re.match(r"[ \t]*(`{3,}|~{3,})", line)
        if fence is None:
            if opener:
                fence = opener[1][0]  # ` or ~
            else:
                kept.append(line)
        elif re.match(rf"[ \t]*{fence}{{3,}}[ \t]*$", line):
            fence = None  # a bare closing fence; drop it and read prose again
    return "".join(kept)


def bold_label_has_content(text: str, start: int) -> bool:
    """Whether a bold run-in alternatives label has content: on its own line after the label, or on a
    following line before the next heading. `start` is the end of the label match."""
    line_end = text.find("\n", start)
    rest_of_line = text[start:] if line_end == -1 else text[start:line_end]
    if re.search(r"[0-9A-Za-z]", rest_of_line):
        return True  # content on the label's own line, e.g. **Alternatives considered.** We chose X
    if line_end == -1:
        return False
    for line in text[line_end + 1:].splitlines():
        if not line.strip():
            continue
        return re.match(r"^#+\s", line) is None  # content unless the next thing is a new heading
    return False


def adr_alternatives_error(path: str, text: str) -> str | None:
    text = strip_fenced_code(text)  # a heading or list quoted in a fenced example is not a real section
    if any(bold_label_has_content(text, bold.end()) for bold in ADR_ALTERNATIVES_BOLD.finditer(text)):
        return None  # a bold run-in label (e.g. **Alternatives considered.**) that carries content
    match = ADR_ALTERNATIVES_HEADING.search(text)
    if match is None:
        if ADR_ALTERNATIVES_BOLD.search(text):
            return f"alternatives: {path} has an empty 'Alternatives considered' section"
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


def strip_inline_code(line: str) -> str:
    return re.sub(r"`[^`]*`", "", line)


def timeless_findings(path: str, added: list[str]) -> tuple[list[str], list[str]]:
    """Errors and warnings for the lines a branch adds to an ADR: the document stays name-free and timeless."""
    errors: list[str] = []
    warnings: list[str] = []
    for line in strip_fenced_code("\n".join(added) + "\n").splitlines():
        prose = strip_inline_code(line)
        for mention in MENTION.finditer(prose):
            errors.append(f"timeless: {path} mentions @{mention['login']}; names belong on the PR and the board")
        if ISSUE_REFERENCE.search(prose):
            warnings.append(f"timeless: {path} links a PR or issue ({line.strip()[:80]}); move it to the PR description")
        if DATED_HEADING.match(line):
            warnings.append(f"timeless: {path} has a dated heading ({line.strip()[:80]})")
        if OPEN_QUESTIONS_HEADING.match(line):
            warnings.append(f"timeless: {path} has an open-questions heading; open questions live in the PR description")
    return errors, warnings


def check(tree: dict[str, str], changed: set[str], base_paths: list[str],
          added: dict[str, list[str]] | None = None) -> tuple[list[str], list[str]]:
    """tree maps every path in scope to its text (empty for files that are not read); changed is the paths new or
    changed since the fork point; base_paths is the docs/adr and specs paths at the fork point; added maps an ADR
    path to the lines this branch added to it. Returns (errors, warnings)."""
    paths = sorted(tree)
    errors = collisions(paths, base_paths, adr_number, "ADR") + collisions(paths, base_paths, spec_number, "spec")
    warnings: list[str] = []
    base_set = set(base_paths)
    adr_numbers = {found[0] for found in map(adr_number, paths) if found}
    for path in sorted(changed):
        if path not in tree:
            continue  # deleted
        text = tree[path]
        if adr_number(path):
            status = adr_status_error(path, text)
            if status:
                errors.append(status)
            if path not in base_set:  # the section is required only on ADRs this branch adds, not on amendments
                alternatives = adr_alternatives_error(path, text)
                if alternatives:
                    errors.append(alternatives)
            timeless_errors, timeless_warnings = timeless_findings(path, (added or {}).get(path, []))
            errors.extend(timeless_errors)
            warnings.extend(timeless_warnings)
        elif spec_number(path) and path.endswith("/spec.md"):
            error = spec_status_error(path, text)
            if error:
                errors.append(error)
        elif path.startswith(MEETING_DIR) and path.endswith(".md"):
            # a committed meeting agenda is name-free like an ADR; it may link PRs, so only mentions fail
            errors.extend(timeless_findings(path, (added or {}).get(path, []))[0])
        if path.endswith(".md"):
            warnings.extend(unresolved_references(path, text, adr_numbers))
    return errors, warnings


def git_lines(root: Path, *args: str) -> list[str]:
    result = subprocess.run(["git", *args], cwd=root, capture_output=True, text=True, check=True)
    return [line for line in result.stdout.splitlines() if line]


def added_lines(root: Path, fork_point: str, untracked: list[str]) -> dict[str, list[str]]:
    """The lines this branch adds to each ADR since the fork point, including uncommitted and untracked ones."""
    added: dict[str, list[str]] = {}
    result = subprocess.run(["git", "diff", "-U0", "--no-renames", fork_point, "--", "docs/adr", MEETING_DIR], cwd=root,
                            capture_output=True, text=True, encoding="utf-8", errors="replace", check=True)
    path = None
    for line in result.stdout.splitlines():
        if line.startswith("+++ "):
            path = line[6:] if line.startswith("+++ b/") else None
        elif path and line.startswith("+"):
            added.setdefault(path, []).append(line[1:])
    for path in untracked:
        if adr_number(path) or (path.startswith(MEETING_DIR) and path.endswith(".md")):
            added[path] = (root / path).read_text(encoding="utf-8", errors="replace").splitlines()
    return added


def load(root: Path, base: str) -> tuple[dict[str, str], set[str], list[str], dict[str, list[str]]]:
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
    return tree, changed, base_paths, added_lines(root, fork_point, untracked)


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
        ("heading must say considered",
         {"docs/adr/0092-a.md": "# ADR\n\nStatus: accepted\n\n## Alternatives to this process\n\nprose\n"},
         {"docs/adr/0092-a.md"}, [], 1, 0),
        ("bold alternatives label",
         {"docs/adr/0092-a.md": "# ADR\n\nStatus: accepted\n\n**Alternatives considered.** We chose X over Y.\n"},
         {"docs/adr/0092-a.md"}, [], 0, 0),
        ("bold options list item",
         {"docs/adr/0092-a.md": "# ADR\n\nStatus: accepted\n\n- **Options considered:** X vs Y.\n"},
         {"docs/adr/0092-a.md"}, [], 0, 0),
        ("empty bold alternatives label",
         {"docs/adr/0092-a.md": "# ADR\n\nStatus: accepted\n\n**Alternatives considered.**\n\n## Next steps\n\nx\n"},
         {"docs/adr/0092-a.md"}, [], 1, 0),
        ("modified legacy adr without section",
         {"docs/adr/0003-a.md": "# ADR\n\nStatus: accepted\n\nClarified a stale reference.\n"},
         {"docs/adr/0003-a.md"}, ["docs/adr/0003-a.md"], 0, 0),
        ("new adr without section still fails",
         {"docs/adr/0094-a.md": "# ADR\n\nStatus: accepted\n\nNo alternatives here.\n"},
         {"docs/adr/0094-a.md"}, [], 1, 0),
        ("fenced example is not section content",
         {"docs/adr/0092-a.md":
          "# ADR\n\nStatus: accepted\n\n## Alternatives considered\n\n```\n- Example: X vs Y.\n```\n\n## Next steps\n\nx\n"},
         {"docs/adr/0092-a.md"}, [], 1, 0),
        ("heading inside a fence does not count",
         {"docs/adr/0092-a.md":
          "# ADR\n\nStatus: accepted\n\n```\n## Alternatives considered\n\n- A vs B.\n```\n\nText.\n"},
         {"docs/adr/0092-a.md"}, [], 1, 0),
        ("bad spec status", {"specs/1208-x/spec.md": "**Status**: Pending\n"}, {"specs/1208-x/spec.md"}, [], 1, 0),
        ("spec status prefix only", {"specs/1208-x/spec.md": "**Status**: Drafting\n"}, {"specs/1208-x/spec.md"}, [], 1, 0),
        ("missing spec status", {"specs/1208-x/spec.md": "# Spec\n"}, {"specs/1208-x/spec.md"}, [], 1, 0),
        ("unresolved reference", {"docs/adr/0093-a.md": adr_ok + "Amends ADR 0045.\n"}, {"docs/adr/0093-a.md"}, [], 0, 1),
        ("reference list", {"CONTEXT.md": "See ADRs 0005 and 0007, ADR-0029.\n"}, {"CONTEXT.md"}, [], 0, 3),
        ("resolved reference", {"docs/adr/0093-a.md": adr_ok + "Amends ADR 0045.\n", "docs/adr/0045-v.md": ""},
         {"docs/adr/0093-a.md"}, [], 0, 0),
        ("deleted file", {"docs/adr/0091-a.md": ""}, {"docs/adr/0090-gone.md"}, [], 0, 0),
    ]
    amended = "docs/adr/0045-a.md"
    timeless = [
        ("added mention fails", ["Agreed with @sfmskywalker."], 1, 0),
        ("mention in code or email passes", ["Use `@scope/pkg` or mail a@b.io."], 0, 0),
        ("issue link warns", ["This replaces routing by return value (#1114)."], 0, 1),
        ("pr url warns", ["See https://github.com/o/r/pull/3389."], 0, 1),
        ("dated heading warns", ["## Amendment (2026-10-27)"], 0, 1),
        ("open questions heading warns", ["## Questions for the reviewers"], 0, 1),
        ("timeless prose passes", ["An empty selection sends a dead arrival on every outgoing connection."], 0, 0),
        ("fenced mention passes", ["```", "@example", "```"], 0, 0),
    ]
    agenda = "docs/initiatives/3052/2026-11-12-meeting-agenda.md"
    cases.append(("agenda mention fails", {agenda: "x"}, {agenda}, [], 1, 0, {agenda: ["**With:** @someone."]}))
    cases.append(("agenda pr link passes", {agenda: "x"}, {agenda}, [], 0, 0, {agenda: ["See #3411 and position 1."]}))
    for name, lines, want_errors, want_warnings in timeless:
        cases.append((name, {amended: adr_ok}, {amended}, [amended], want_errors,
                      want_warnings, {amended: lines}))
    failures = 0
    for name, tree, changed, base_paths, want_errors, want_warnings, *rest in cases:
        errors, warnings = check(tree, changed, base_paths, rest[0] if rest else None)
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
        tree, changed, base_paths, added = load(root, args.base)
    except (subprocess.CalledProcessError, OSError, IndexError) as error:
        print(f"check_decision_docs: could not read the repository at {args.root} against {args.base}: {error}",
              file=sys.stderr)
        return 2

    errors, warnings = check(tree, changed, base_paths, added)
    for line in errors:
        print(f"ERROR {line}")
    for line in warnings:
        print(f"WARN  {line}")
    print(f"check_decision_docs: {root}, {len(changed)} changed path(s) since the fork point with {args.base}, "
          f"{len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
