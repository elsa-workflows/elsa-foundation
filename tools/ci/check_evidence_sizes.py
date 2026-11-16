#!/usr/bin/env python3
"""Keep raw captures out of Git; release assets carry source-bound evidence."""

from __future__ import annotations

import argparse
from pathlib import Path
import subprocess
import tempfile
import unittest

MAX_FILE_BYTES = 2 * 1024 * 1024


def oversized(root: Path, paths: list[str], limit: int = MAX_FILE_BYTES) -> list[tuple[str, int]]:
    result = []
    for name in paths:
        parts = Path(name).parts
        if len(parts) < 4 or parts[0] != "specs" or parts[2] != "evidence":
            continue
        size = (root / name).stat().st_size
        if size > limit:
            result.append((name, size))
    return result


class EvidenceSizeTests(unittest.TestCase):
    def test_boundary_and_nested_capture(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            names = ["specs/feature/evidence/receipt.json", "specs/feature/evidence/nested/capture.bin"]
            for name, size in zip(names, [10, 11]):
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"x" * size)
            self.assertEqual([(names[1], 11)], oversized(root, names, 10))

    def test_other_paths_and_untracked_captures_are_not_evidence_inputs(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "specs/feature/evidence/hydrated.bin"
            path.parent.mkdir(parents=True)
            path.write_bytes(b"x" * 11)
            self.assertEqual([], oversized(root, ["src/evidence/large.bin", "specs/feature/notes.txt"], 10))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        return 0 if unittest.TextTestRunner().run(unittest.defaultTestLoader.loadTestsFromTestCase(EvidenceSizeTests)).wasSuccessful() else 1
    root = Path(subprocess.check_output(["git", "rev-parse", "--show-toplevel"], text=True).strip())
    names = subprocess.check_output(["git", "ls-files", "-z"], cwd=root).decode().split("\0")
    failures = oversized(root, [name for name in names if name])
    for name, size in failures[:12]:
        print(f"Evidence exceeds {MAX_FILE_BYTES} bytes: {name} ({size} bytes)")
    if failures:
        print(f"{len(failures)} oversized evidence files; retain a concise receipt and a verified artifact reference instead.")
        return 1
    print("Evidence size guard: every tracked specs/*/evidence file is at most 2 MiB.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
