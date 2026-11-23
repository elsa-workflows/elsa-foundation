#!/usr/bin/env python3
"""Read Studio NuGets back from the selected feed and record the publicly served digests."""
import argparse
import hashlib
import json
import re
import shutil
import subprocess
import time
import zipfile
from pathlib import Path
from urllib.error import HTTPError
from urllib.request import urlopen

from studio_archives import check, inspect


def read_json(url):
    with urlopen(url, timeout=30) as response:
        return json.load(response)


def payload(path):
    with zipfile.ZipFile(path) as archive:
        entries = archive.namelist()
        check(len(entries) == len(set(entries)), "Duplicate ZIP entries")
        return {entry: hashlib.sha256(archive.read(entry)).hexdigest() for entry in entries
                if entry != ".signature.p7s" and not entry.endswith("/")}


def fetch(url, output):
    for attempt in range(40):
        try:
            with urlopen(url, timeout=30) as response, output.open("wb") as target:
                shutil.copyfileobj(response, target)
            return
        except HTTPError as error:
            if error.code != 404 or attempt == 39:
                raise
            time.sleep(10)


def collect(source, output, feed):
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
        check(inspect(original)[0] == candidate, "Candidate differs from build receipt")
        name, version = candidate["id"].lower(), candidate["version"].lower()
        check(all(re.fullmatch(r"[\w.+-]+", part) for part in (name, version)), "Invalid identity")
        path = output / filename
        fetch(f"{base}/{name}/{version}/{name}.{version}.nupkg", path)
        current, _ = inspect(path)
        if public:
            args = [arg for f in fingerprints for arg in ["--certificate-fingerprint", f]]
            subprocess.run(["dotnet", "nuget", "verify", str(path), "--all", *args], check=True)
            check(payload(original) == payload(path), "Published NuGet payload differs: " + filename)
        else:
            check(current == candidate, "Feedz returned different candidate bytes: " + filename)
        rows.append(dict(candidate, feed=feed, feed_sha256=current["sha256"]))
    (output / "nuget-feed-receipt.json").write_text(json.dumps(rows, indent=2) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("feed", choices=["https://api.nuget.org/v3/index.json",
                                        "https://f.feedz.io/elsa-workflows/elsa-4/nuget/index.json"])
    args = parser.parse_args()
    collect(args.source, args.output, args.feed)
