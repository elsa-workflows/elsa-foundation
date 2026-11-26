#!/usr/bin/env python3
"""Create a Studio Essentials Docker build context from Studio package archives."""

import argparse
import base64
import hashlib
import json
from pathlib import Path
import re
import shutil
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile


PROFILE = Path(__file__).resolve().parent
ESSENTIALS = PROFILE.parents[2] / "src/apps/Elsa.Studio.Essentials.Host"
FEED_NAME = "studio-essentials-packages"
PROFILE_ROOT = "/studio-profile"
PROFILE_TIME = "1970-01-01T00:00:00Z"


def write_json(path, data):
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def stage_packages(archives, version, output):
    package_ids = (PROFILE / "packages.txt").read_text(encoding="utf-8").split()
    package_directory = output / "packages"
    package_directory.mkdir()
    lock_entries = []
    for package_id in package_ids:
        filename = f"{package_id}.{version}.nupkg"
        matches = [path for path in archives.iterdir() if path.name.lower() == filename.lower()]
        if len(matches) != 1:
            raise ValueError(f"Expected one {filename} in {archives}, found {len(matches)}")
        baked_archive = package_directory / filename.lower()
        shutil.copyfile(matches[0], baked_archive)
        with zipfile.ZipFile(baked_archive) as archive:
            nuspecs = [name for name in archive.namelist() if name.lower() == f"{package_id.lower()}.nuspec"]
            if len(nuspecs) != 1:
                raise ValueError(f"Expected one {package_id}.nuspec in {matches[0]}")
            spec = ET.fromstring(archive.read(nuspecs[0]))
            identity = (spec.findtext("./{*}metadata/{*}id"), spec.findtext("./{*}metadata/{*}version"))
            if identity != (package_id, version):
                raise ValueError(f"{matches[0]} contains {identity}, expected {package_id} {version}")
        digest = base64.b64encode(hashlib.sha512(baked_archive.read_bytes()).digest()).decode("ascii")
        lock_entries.append({
            "id": package_id,
            "version": version,
            "feed": FEED_NAME,
            "hash": f"sha512:{digest}",
            "timestamp": PROFILE_TIME,
        })
    write_json(output / "nuplane.lock.json", {
        "schemaVersion": "2.0", "generatedAt": PROFILE_TIME, "packages": lock_entries,
    })
    write_json(output / "desired.json", {
        "schemaVersion": "1.0",
        "generatedAtUtc": PROFILE_TIME,
        "packages": [
            {"id": entry["id"], "version": entry["version"], "sourceHint": entry["feed"]}
            for entry in lock_entries
        ],
    })
    (output / "version").write_text(version + "\n", encoding="utf-8")


def stage_configuration(output):
    settings = json.loads((ESSENTIALS / "appsettings.json").read_text(encoding="utf-8"))
    nuplane = settings.setdefault("Nuplane", {})
    nuplane["Convergence"] = {"Manifest": {"Enabled": True, "Path": f"{PROFILE_ROOT}/desired.json"}}
    nuplane["LockFile"] = {"Mode": "Enforce", "Path": f"{PROFILE_ROOT}/nuplane.lock.json", "FailOnHashMismatch": True}
    write_json(output / "appsettings.json", settings)

    management = json.loads((ESSENTIALS / "nuplane-management.json").read_text(encoding="utf-8"))
    management["Nuplane"]["Setup"]["Feeds"].append({
        "Name": FEED_NAME,
        "DirectoryPath": f"{PROFILE_ROOT}/packages",
        "Directory": {"Role": "Cache", "Watch": False},
    })
    write_json(output / "nuplane-management.json", management)
    shutil.copyfile(ESSENTIALS / "shells.json", output / "shells.json")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archives", type=Path, help="directory of official or locally packed Studio .nupkg files")
    parser.add_argument("--version", required=True, help="Studio version shared by the Foundation image and packages")
    parser.add_argument("--output", required=True, type=Path, help="new directory for the standalone Docker build context")
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9]+(?:\.[0-9]+){2,3}(?:-[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*)?", args.version):
        parser.error("Use an exact Studio package version")
    try:
        if args.output.exists():
            raise ValueError(f"Output directory already exists: {args.output}")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="elsa-studio-profile-", dir=args.output.parent) as temporary:
            directory = Path(temporary) / "context"
            payload = directory / "profile"
            payload.mkdir(parents=True)
            stage_packages(args.archives, args.version, payload)
            stage_configuration(directory)
            shutil.copyfile(PROFILE / "Dockerfile", directory / "Dockerfile")
            directory.rename(args.output)
    except (OSError, ValueError, ET.ParseError, zipfile.BadZipFile) as error:
        print(f"Studio Essentials profile preparation failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
