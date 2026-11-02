#!/usr/bin/env python3
"""Restore the exact archived BPMN test inputs; --all restores historical raw proof too."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import subprocess
import tempfile
import urllib.request
import zipfile

REPO = Path(__file__).resolve().parents[2]
CONFIG = Path("specs/1017-direct-bpmn-hosting/evidence/archive.json")
PREFIX = "specs/1017-direct-bpmn-hosting/evidence/"


def digest(path: Path) -> str:
    value = hashlib.sha256()
    with path.open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            value.update(chunk)
    return value.hexdigest()


def restore(root: Path, archive: Path, record: dict, *, all_evidence: bool) -> int:
    if archive.stat().st_size != record["bytes"] or digest(archive) != record["sha256"]:
        raise ValueError("The downloaded evidence archive does not match its pinned size and SHA-256.")
    tracked = set(subprocess.check_output(["git", "ls-files", "-z"], cwd=root).decode().split("\0")) if all_evidence else set()
    written = 0
    with zipfile.ZipFile(archive) as source:
        manifest = json.loads(source.read("ARCHIVE-MANIFEST.json"))
        if manifest["sourceCommit"] != record["sourceCommit"] or len(manifest["files"]) != record["files"]:
            raise ValueError("The archived source or file inventory differs from the pinned receipt.")
        for item in manifest["files"]:
            name = item["path"]
            relative = PurePosixPath(name)
            if not name.startswith(PREFIX) or relative.is_absolute() or ".." in relative.parts:
                raise ValueError(f"Unexpected evidence path: {name}")
            target = (root / name).resolve()
            if root.resolve() not in target.parents:
                raise ValueError(f"Evidence path escapes the checkout: {name}")
            # Current tracked receipts can evolve; a full restore never replaces them with historical prose.
            if all_evidence and name in tracked:
                continue
            data = source.read(name)
            if len(data) != item["bytes"] or hashlib.sha256(data).hexdigest() != item["sha256"]:
                raise ValueError(f"Archive member differs from its original bytes: {name}")
            if target.exists():
                if target.stat().st_size != item["bytes"] or digest(target) != item["sha256"]:
                    raise ValueError(f"Existing evidence differs from the archived input; refusing to overwrite: {name}")
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as temporary:
                temporary.write(data)
                staged = Path(temporary.name)
            try:
                staged.replace(target)
            finally:
                staged.unlink(missing_ok=True)
            written += 1
    return written


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--all", action="store_true", help="Restore raw historical captures as well as current test inputs.")
    parser.add_argument("--archive", type=Path, help="Use an already downloaded archive, with the same hash verification.")
    args = parser.parse_args()
    config = json.loads((REPO / CONFIG).read_text())
    record = config["completeArchive" if args.all else "testFixtures"]
    archive = args.archive
    if archive is None:
        cache = REPO / ".artifacts/bpmn-evidence"
        cache.mkdir(parents=True, exist_ok=True)
        archive = cache / (record["sha256"] + ".zip")
        if not archive.exists():
            with tempfile.NamedTemporaryFile(dir=cache, delete=False) as temporary:
                staged = Path(temporary.name)
            try:
                with urllib.request.urlopen(record["url"]) as source, staged.open("wb") as target:
                    while chunk := source.read(1024 * 1024):
                        target.write(chunk)
                if staged.stat().st_size != record["bytes"] or digest(staged) != record["sha256"]:
                    raise ValueError("Evidence download failed its pinned size/SHA-256 check.")
                staged.replace(archive)
            finally:
                staged.unlink(missing_ok=True)
    written = restore(REPO, archive, record, all_evidence=args.all)
    print(f"Verified pinned archive ({record['files']} recorded files); restored {written} missing files from {record['sourceCommit']}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
