#!/usr/bin/env python3
"""Reproduce pure relocation and mapped-history proof from a full-history checkout.

Run `python3 tools/consolidation/verify-history.py`. Shallow clones need the
candidate's complete ancestry; missing objects fail rather than skip evidence.
"""
from pathlib import Path
import json
import subprocess

ROOT = Path(__file__).resolve().parents[2]
EVIDENCE = Path(__file__).resolve().parent
MANIFEST = json.loads((EVIDENCE / "layout-manifest.json").read_text())


def git(*args: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(ROOT), *args])


def entries(ref: str) -> dict[bytes, bytes]:
    return dict(reversed(row.split(b"\t", 1))
                for row in git("ls-tree", "-rz", ref).split(b"\0") if row)


def relocation(source: str, checkpoint: str, ledger_name: str) -> int:
    original, moved = entries(source), entries(checkpoint)
    ledger = json.loads((EVIDENCE / ledger_name).read_text())
    assert set(original) == {p.encode() for p in ledger}, "Incomplete source ledger"
    assert len(set(ledger.values())) == len(ledger), "Colliding destination paths"
    for old, new in ledger.items():
        assert original[old.encode()] == moved.get(new.encode()), (old, new)
    return len(ledger)


def metadata(commit: bytes) -> tuple[list[bytes], bytes]:
    headers, message = commit.split(b"\n\n", 1)
    retained = [line for line in headers.splitlines()
                if line.startswith((b"author ", b"committer ", b"encoding "))]
    return retained, message


def main() -> None:
    foundation, studio = MANIFEST["foundation_source"], MANIFEST["studio_source"]
    counts = {
        "foundation_files": relocation(foundation, "87d14dd2b04b0ccf2b11bf155c4fe8ecd2dd17b4",
                                       "foundation-path-map.json"),
        "studio_files": relocation(studio, "b8c8cf3a699441b918292b2d6575ad5e3191cd0f",
                                   "studio-path-map.json"),
    }
    rows = (EVIDENCE / "studio-commit-map.txt").read_text().splitlines()[1:]
    for row in rows:
        old, new = row.split()
        assert old != "0" * 40 and new != "0" * 40, row
        assert metadata(git("cat-file", "commit", old)) == metadata(git("cat-file", "commit", new)), row
        assert git("rev-parse", old + "^{tree}") == git("rev-parse", new + ":Studio"), row
    for ref in (foundation, studio):
        subprocess.run(["git", "-C", str(ROOT), "merge-base", "--is-ancestor", ref, "HEAD"], check=True)
    counts.update(mapped_commits=len(rows), original_ancestry_retained=True)
    print(json.dumps(counts, indent=2))


if __name__ == "__main__":
    main()
