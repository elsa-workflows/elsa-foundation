#!/usr/bin/env python3
"""Report relative Markdown links that do not resolve.

Checks every tracked EXTENSION_POINTS.md and every tracked README.md under Essentials/ and Extensions/.
External links (scheme or protocol-relative) and same-file anchors are skipped; a link's #fragment and
?query are dropped before the target is checked. Links inside fenced code blocks and inline code are
ignored. A leading '/' resolves against the repository root, the way GitHub renders it. A target must be a
tracked file or a directory holding one, matched case-sensitively, so a link that only works on a
case-insensitive disk or to an untracked local file is reported too.

The Architecture suite's CatalogLinkGuardTests enforces the same rule over the same files: without --all-readmes this
script checks exactly the set Every_relative_link_in_a_catalog_or_module_readme_resolves checks. CI's documentation-only
fast path runs it in that mode in place of the .NET build (spec 257). The guard's other test, that the root index links
every source catalog, can only break through a catalog change, and a catalog change always gets the full run.

Paths are git paths, so they are joined and normalised with posixpath on every platform.

Usage: python3 tools/maps/check-catalog-links.py [--all-readmes]
Exits 1 when any link is broken.
"""
import os
import posixpath
import re
import subprocess
import sys

INLINE_LINK = re.compile(r"!?\[(?:[^\[\]]|\[[^\]]*\])*\]\(\s*<?([^)\s>]+)>?(?:\s+[\"'(][^)]*)?\)")
REFERENCE_DEFINITION = re.compile(r"^ {0,3}\[[^\]]+\]:[ \t]*<?([^\s>]+)>?", re.MULTILINE)
INLINE_CODE = re.compile(r"`+[^`]*`+")
SCHEME = re.compile(r"^[A-Za-z][A-Za-z0-9+.-]*:")


def tracked(root, pathspecs):
    out = subprocess.run(["git", "-C", root, "ls-files", "-z", "--", *pathspecs], check=True, capture_output=True)
    return sorted(path for path in out.stdout.decode().split("\0") if path)


def targets(text):
    """Yields (line number, target) for every link, with fenced blocks and inline code blanked first.

    Link text may wrap across lines, so links are matched over the whole text rather than line by line.
    """
    kept = []
    in_fence = False
    for line in text.splitlines():
        fence = re.match(r"^\s*(```|~~~)", line)
        kept.append("" if fence or in_fence else INLINE_CODE.sub(lambda code: " " * len(code.group(0)), line))
        if fence:
            in_fence = not in_fence
    body = "\n".join(kept)
    for pattern in (INLINE_LINK, REFERENCE_DEFINITION):
        for match in pattern.finditer(body):
            yield body.count("\n", 0, match.start(1)) + 1, match.group(1)


def broken_links(root, relative_path, tracked_paths):
    with open(os.path.join(root, relative_path), encoding="utf-8") as handle:
        text = handle.read()
    for number, target in targets(text):
        if target.startswith(("#", "//")) or SCHEME.match(target):
            continue
        path = re.split(r"[#?]", target, maxsplit=1)[0]
        if not path:
            continue
        base = "" if path.startswith("/") else posixpath.dirname(relative_path)
        resolved = posixpath.normpath(posixpath.join(base, path.lstrip("/")))
        if resolved.startswith("..") or resolved not in tracked_paths:
            yield number, target


def main():
    root = subprocess.run(["git", "rev-parse", "--show-toplevel"], check=True, capture_output=True, text=True).stdout.strip()
    pathspecs = [":(glob)**/EXTENSION_POINTS.md", ":(glob)Essentials/**/README.md", ":(glob)Extensions/**/README.md"]
    if "--all-readmes" in sys.argv:
        pathspecs.append(":(glob)**/README.md")

    all_files = tracked(root, [])
    tracked_paths = set(all_files)
    for path in all_files:
        while path := posixpath.dirname(path):
            tracked_paths.add(path)
    files = tracked(root, pathspecs)
    failures = [(path, number, target) for path in files for number, target in broken_links(root, path, tracked_paths)]
    for path, number, target in failures:
        print(f"{path}:{number}: {target}")
    print(f"{len(failures)} broken relative link(s) in {len({path for path, _, _ in failures})} of {len(files)} file(s).", file=sys.stderr)
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
