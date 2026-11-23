#!/usr/bin/env python3
"""Read Studio NuGets back from the selected feed and record the publicly served digests."""
import argparse
import json
import re
import shutil
import subprocess
import time
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import urlopen

from studio_archives import check, inspect, nuget_payload, sha256


def read_json(url):
    with urlopen(url, timeout=30) as response:
        return json.load(response)


def fetch(url, output, allow_missing=False):
    for attempt in range(40):
        try:
            with urlopen(url, timeout=30) as response, output.open("wb") as target:
                shutil.copyfileobj(response, target)
            return True
        except HTTPError as error:
            if error.code == 404 and allow_missing:
                return False
            if error.code != 404 or attempt == 39:
                raise
            time.sleep(10)


def collect(source, output, feed, preflight=False):
    resources = {r["@type"]: r["@id"] for r in read_json(feed)["resources"]}
    base = resources["PackageBaseAddress/3.0.0"].rstrip("/")
    public = feed == "https://api.nuget.org/v3/index.json"
    fingerprints = []
    if public:
        signatures = read_json(resources["RepositorySignatures/5.0.0"])
        check(signatures["allRepositorySigned"], "Expected nuget.org repository signing")
        fingerprints = [s["fingerprints"]["2.16.840.1.101.3.4.2.1"] for s in signatures["signingCertificates"]]
        check(fingerprints and all(re.fullmatch(r"[0-9a-fA-F]{64}", f) for f in fingerprints), "Invalid repository fingerprints")
    rows = []
    output.mkdir(parents=True)
    for candidate in json.loads((source / "nuget-subjects.json").read_bytes()):
        filename = candidate["name"]
        check(Path(filename).name == filename, "Invalid package name")
        original = source / filename
        packed, _ = inspect(original)
        packed["payload_sha256"] = sha256(nuget_payload(original))
        check(packed == candidate, "Candidate differs from build receipt")
        name, version = candidate["id"].lower(), candidate["version"].lower()
        check(all(re.fullmatch(r"[\w.+-]+", part) for part in (name, version)), "Invalid identity")
        path = output / filename
        if not fetch(f"{base}/{name}/{version}/{name}.{version}.nupkg", path, allow_missing=preflight):
            continue
        current, _ = inspect(path)
        current["payload_sha256"] = sha256(nuget_payload(path))
        if public:
            args = [arg for f in fingerprints for arg in ["--certificate-fingerprint", f]]
            subprocess.run(["dotnet", "nuget", "verify", str(path), "--all", *args], check=True)
            check(current["payload_sha256"] == candidate["payload_sha256"], "Published NuGet payload differs: " + filename)
        else:
            check(current == candidate, "Feedz returned different candidate bytes: " + filename)
        rows.append(dict(candidate, feed=feed, feed_sha256=current["sha256"]))
    if preflight:
        print(f"Verified {len(rows)} existing NuGet packages before the first push.")
        return
    (output / "nuget-feed-receipt.json").write_text(json.dumps(rows, indent=2) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("feed", choices=["https://api.nuget.org/v3/index.json",
                                        "https://f.feedz.io/elsa-workflows/elsa-4/nuget/index.json"])
    parser.add_argument("--preflight", action="store_true", help="Check all existing versions before publishing any candidate")
    args = parser.parse_args()
    collect(args.source, args.output, args.feed, args.preflight)
