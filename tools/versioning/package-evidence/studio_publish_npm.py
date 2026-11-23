#!/usr/bin/env python3
"""Publish verified tarballs in dependency order and compare the registry-reported SRI."""
import argparse
import json
import subprocess
import time
from pathlib import Path

from studio_archives import check, inspect


ORDER = ["studio-sdk", "studio-ui", "studio-code-editor", "studio-workflows"]


def observed(spec, registry):
    p = subprocess.run(["npm", "view", spec, "dist.integrity", "--json", "--registry", registry],
                       capture_output=True, text=True, timeout=60)
    if p.returncode:
        if json.loads(p.stdout or "{}").get("error", {}).get("code") == "E404":
            return None
        raise ValueError("npm could not read " + spec + " from " + registry)
    return json.loads(p.stdout) if p.stdout.strip() else None


def publish(directory, registry, tag, dry_run):
    receipt = json.loads((directory / "npm-subjects.json").read_bytes())
    by_id = {r["id"]: r for r in receipt}
    order = ["@elsa-workflows/" + name for name in ORDER]
    check(len(receipt) == len(order) and set(order) == set(by_id), "Unexpected npm release set")
    candidates = []
    for name in order:
        row = by_id[name]
        check(Path(row["name"]).name == row["name"], "Invalid archive filename")
        path = directory / row["name"]
        actual, _ = inspect(path)
        check(row == actual, "Archive/receipt mismatch for " + name)
        candidates.append((row, path))
    for row, path in candidates:
        spec = row["id"] + "@" + row["version"]
        previous = None if dry_run else observed(spec, registry)
        if previous is None:
            command = ["npm", "publish", str(path.resolve()), "--registry", registry, "--access", "public", "--tag", tag]
            subprocess.run(command + (["--dry-run"] if dry_run else []), check=True)
            if dry_run:
                continue
            for attempt in range(10):
                previous = observed(spec, registry)
                if previous is not None:
                    break
                if attempt < 9:
                    time.sleep(5)
        check(previous == row["integrity"], "Registry integrity differs for " + spec)
        print("Verified", spec, registry, previous, flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("registry")
    parser.add_argument("--tag", required=True)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    publish(args.directory, args.registry, args.tag, args.dry_run)
