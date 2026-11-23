"""Verify a downloaded Foundation preview package; optionally write its signed CycloneDX SBOM."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.request import urlopen
import xml.etree.ElementTree as ET
import zipfile

REPO = "elsa-workflows/elsa-foundation"
WORKFLOW = REPO + "/.github/workflows/packages.yml"
FEED = "https://f.feedz.io/elsa-workflows/elsa-4/nuget/index.json"


def identity(path):
    with zipfile.ZipFile(path) as archive:
        nuspec, = [n for n in archive.namelist() if "/" not in n and n.endswith(".nuspec")]
        meta = ET.fromstring(archive.read(nuspec)).find("{*}metadata")
        package_id, version = meta.findtext("{*}id"), meta.findtext("{*}version")
        commit = meta.find("{*}repository").get("commit")
        if not all(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9.+-]*", v) for v in (package_id, version)):
            raise ValueError("Invalid NuGet ID or version")
        if not re.fullmatch(r"[0-9a-f]{40}", commit):
            raise ValueError("Missing repository commit")
        return package_id, version, commit


def sha256(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def signed_inventory(path):
    package_id, version, commit = identity(path)
    common = ["gh", "attestation", "verify", str(path), "--repo", REPO, "--signer-workflow", WORKFLOW,
              "--source-ref", "refs/heads/main", "--source-digest", commit]
    subprocess.run([*common, "--predicate-type", "https://slsa.dev/provenance/v1"], check=True)
    result = json.loads(subprocess.check_output(
        [*common, "--predicate-type", "https://cyclonedx.org/bom", "--format", "json"]))
    digest = sha256(path)
    for match in result:
        bom = match["verificationResult"]["statement"]["predicate"]
        root = bom["metadata"]["component"]
        if (root["name"] == package_id and root["version"] == version and
                {"alg": "SHA-256", "content": digest} in root["hashes"] and
                {"name": "elsa:commit", "value": commit} in bom["metadata"]["properties"]):
            return bom
    raise ValueError("Verified signatures did not yield the expected package inventory")


def download(service, package_id, version, path):
    with urlopen(service, timeout=30) as response:
        resources = json.load(response)["resources"]
    base = next(r["@id"] for r in resources if r["@type"] == "PackageBaseAddress/3.0.0")
    id_lower, v_lower = package_id.lower(), version.lower()
    url = f"{base.rstrip('/')}/{id_lower}/{v_lower}/{id_lower}.{v_lower}.nupkg"
    # Some feeds acknowledge a PUT before its public content endpoint is ready.
    for attempt in range(6):
        try:
            with urlopen(url, timeout=60) as source, path.open("wb") as dest:
                shutil.copyfileobj(source, dest)
            return
        except (URLError, TimeoutError) as error:
            if isinstance(error, HTTPError) and error.code not in (404, 408, 429, 500, 502, 503, 504):
                raise
            if attempt == 5:
                raise
            time.sleep(2 ** attempt)


def verify_destination(candidate, recovered, service, verify=signed_inventory):
    package_id, version, _ = identity(candidate)
    with tempfile.TemporaryDirectory() as scratch:
        remote = Path(scratch) / candidate.name
        download(service, package_id, version, remote)
        if identity(remote)[:2] != (package_id, version):
            raise ValueError("Feed returned a different package identity")
        remote_digest = sha256(remote)
        if remote_digest != sha256(candidate):
            if not recovered:
                raise ValueError(f"{package_id} {version}: public bytes differ from the attested candidate")
            # FR-018 allowed this recovery after confirming the same input fingerprint.
            # A rebuild can have different ZIP bytes. Only the previous producer can vouch for those.
            with zipfile.ZipFile(candidate) as packed, zipfile.ZipFile(remote) as published:
                def fingerprint(archive):
                    return json.loads(archive.read("elsa-input-fingerprint.json"))["fingerprint"]
                if fingerprint(packed) != fingerprint(published):
                    raise ValueError("Public retry bytes have a different input fingerprint")
            verify(remote)
        print(f"Verified public package {package_id} {version} sha256:{remote_digest}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    parser.add_argument("--output", type=Path, help="write the verified inventory")
    parser.add_argument("--destination", choices=["pushed", "recovered"], help="publisher read-back mode")
    parser.add_argument("--feed", default=FEED)
    args = parser.parse_args()
    if args.destination:
        verify_destination(args.package, args.destination == "recovered", args.feed)
    else:
        inventory = signed_inventory(args.package)
        if args.output:
            args.output.write_text(json.dumps(inventory, indent=2) + "\n")
        print(f"Verified {args.package.name} sha256:{sha256(args.package)}")
