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
        if not all(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._+-]*", v) for v in (package_id, version)):
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


class NoAttestations(ValueError):
    """The lookup completed with no attestations of the requested type."""


def signed_inventory(path):
    release = subprocess.check_output(["gh", "--version"], text=True, timeout=15)
    parsed = re.match(r"gh version (\d+)\.(\d+)\.(\d+)\b", release)
    if not parsed or tuple(map(int, parsed.groups())) < (2, 102, 0):
        raise ValueError("GitHub CLI 2.102.0 or later is required for exact workflow and ref matching.")
    package_id, version, commit = identity(path)
    common = ["gh", "attestation", "verify", str(path), "--repo", REPO, "--signer-workflow", WORKFLOW,
              "--source-ref", "refs/heads/main", "--source-digest", commit]
    def statements(predicate):
        try:
            raw = subprocess.check_output([*common, "--predicate-type", predicate, "--limit", "100", "--format", "json"],
                                          stderr=subprocess.PIPE, timeout=55)
        except subprocess.CalledProcessError as error:
            detail = (error.stderr or b"").decode(errors="replace").strip()
            if detail == "Error: no attestations found":
                raise NoAttestations(f"No {predicate} records were found for these bytes.") from error
            raise ValueError(f"GitHub could not verify {predicate}: {detail}") from error
        records = json.loads(raw)
        if not records:
            raise NoAttestations(f"No {predicate} records were found for these bytes.")
        return [(result["statement"]["predicate"], result["signature"]["certificate"].get("runInvocationURI"))
                for result in (match["verificationResult"] for match in records)]

    provenance = {invocation for predicate, invocation in statements("https://slsa.dev/provenance/v1")
                  if invocation == predicate["runDetails"]["metadata"]["invocationId"]}
    digest = sha256(path)
    matched = []
    for bom, certificate_run in statements("https://cyclonedx.org/bom"):
        meta = bom["metadata"]
        root = meta["component"]
        properties = {p["name"]: p["value"] for p in meta["properties"]}
        run, attempt = properties.get("elsa:producer-run", ""), properties.get("elsa:producer-attempt", "")
        if not all(re.fullmatch(r"[1-9][0-9]*", n) for n in (run, attempt)):
            continue
        invocation = f"https://github.com/{REPO}/actions/runs/{run}/attempts/{attempt}"
        if (invocation == certificate_run and invocation in provenance and bom.get("bomFormat") == "CycloneDX" and bom.get("specVersion") == "1.6" and
                root["name"] == package_id and root["version"] == version and
                {"alg": "SHA-256", "content": digest} in root["hashes"] and properties.get("elsa:commit") == commit):
            matched.append((int(run), int(attempt), bom))
    if matched:
        return min(matched, key=lambda item: item[:2])[2]
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
            try:
                verify(remote)
            except subprocess.TimeoutExpired as error:
                raise ValueError("GitHub verification timed out; retry public verification before taking further action.") from error
            except NoAttestations as error:
                raise ValueError("No original attestations were found. For a confirmed unsigned pre-cutover push, "
                                 "follow the manual repair procedure in tools/versioning/README.md.") from error
        mode = "recovered" if recovered else "pushed"
        print(f"Verified {mode} public package {package_id} {version} sha256:{remote_digest}")


def verify_subjects(plan, expected):
    manifest = plan / "sboms" / "subjects.json"
    if not re.fullmatch(r"[a-f0-9]{64}", expected) or sha256(manifest) != expected:
        raise ValueError("The signed job's subject manifest digest differs")
    records = json.loads(manifest.read_bytes())
    packages = {p.name: p for p in (plan / "nuget").glob("*.nupkg")}
    if sorted(packages) != sorted(r["name"] for r in records):
        raise ValueError("Candidates differ from signed subject set")
    for record in records:
        if sha256(packages[record["name"]]) != record["sha256"]:
            raise ValueError(f"Candidate differs from signed subject: {record['name']}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", type=Path)
    parser.add_argument("--output", type=Path, help="write the verified inventory")
    parser.add_argument("--destination", choices=["pushed", "recovered"], help="publisher read-back mode")
    parser.add_argument("--feed", default=FEED)
    parser.add_argument("--manifest-digest", help="publisher preflight for the downloaded plan directory")
    args = parser.parse_args()
    try:
        if args.manifest_digest is not None:
            verify_subjects(args.package, args.manifest_digest)
        elif args.destination:
            verify_destination(args.package, args.destination == "recovered", args.feed)
        else:
            inventory = signed_inventory(args.package)
            if args.output:
                args.output.write_text(json.dumps(inventory, indent=2) + "\n")
            print(f"Verified {args.package.name} sha256:{sha256(args.package)}")
    except (ValueError, OSError, subprocess.SubprocessError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Package evidence verification failed: {error}\n")
