#!/usr/bin/env python3
"""Fetch only the package IDs and versions in the reviewed manifest; verify size and SHA512."""
import base64
import hashlib
import json
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent
FEED = ROOT / "feed"
MANIFEST = ROOT / "feed-manifest.json"
BASE = "https://api.nuget.org/v3-flatcontainer"


def main() -> None:
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    FEED.mkdir(exist_ok=True)
    for package in manifest["packages"]:
        package_id = package["id"]
        version = package["version"]
        name = f"{package_id}.{version}.nupkg"
        url = f"{BASE}/{package_id.lower()}/{version.lower()}/{name.lower()}"
        with urllib.request.urlopen(url) as response:
            payload = response.read()

        actual_hash = base64.b64encode(hashlib.sha512(payload).digest()).decode("ascii")
        if len(payload) != package["size"] or actual_hash != package["sha512Base64"]:
            raise ValueError(f"Manifest verification failed for {name}: size={len(payload)}, SHA512={actual_hash}")

        (FEED / name).write_bytes(payload)
        print(f"Verified {name} size={len(payload)} SHA512={actual_hash}")


if __name__ == "__main__":
    main()
