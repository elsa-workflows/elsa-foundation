#!/usr/bin/env python3
"""Verify embedded Studio inventories against consumer-visible package contents."""
import argparse
import base64
import hashlib
import json
import os
import tarfile
import zipfile
from pathlib import Path
from xml.etree import ElementTree


def check(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def nuget_payload(path):
    """Canonical content witness, reproducible before and after nuget.org repository signing."""
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        check(len(names) == len(set(names)), "Duplicate NuGet entries")
        entries = {name: sha256(archive.read(name)) for name in sorted(names)
                   if name != ".signature.p7s" and not name.endswith("/")}
        return (json.dumps(entries, sort_keys=True, separators=(",", ":")) + "\n").encode()


def inspect(path):
    files = {}
    seen = set()
    raw = None
    manifest = None
    package_id = version = None
    npm = path.suffix == ".tgz"
    if npm:
        with tarfile.open(path, "r|gz") as archive:
            for member in archive:
                if member.isdir():
                    continue
                name = member.name.removeprefix("package/")
                check(member.isfile() and member.name.startswith("package/") and name not in seen, str(member))
                seen.add(name)
                data = archive.extractfile(member).read()
                if name == "package.json":
                    manifest = json.loads(data)
                    package_id, version = manifest["name"], manifest["version"]
                elif name == "dist/sbom.cdx.json":
                    raw = data
                elif name.startswith("dist/"):
                    files[name] = sha256(data)
    else:
        with zipfile.ZipFile(path) as archive:
            check(len(archive.namelist()) == len(set(archive.namelist())), "Duplicate entries in " + path.name)
            specs = [n for n in archive.namelist() if n.endswith(".nuspec") and "/" not in n]
            check(len(specs) == 1, "Expected one NuGet manifest")
            meta = ElementTree.fromstring(archive.read(specs[0])).find("{*}metadata")
            package_id, version = meta.findtext("{*}id"), meta.findtext("{*}version")
            for name in archive.namelist():
                if name == "sbom/frontend.cdx.json":
                    raw = archive.read(name)
                elif name.startswith("staticwebassets/") and not name.endswith("/"):
                    files[name] = sha256(archive.read(name))
    check(package_id and version, "Package identity missing")
    payload = path.read_bytes()
    row = dict(name=path.name, id=package_id, version=version, sha256=sha256(payload))
    if npm:
        row["integrity"] = "sha512-" + base64.b64encode(hashlib.sha512(payload).digest()).decode()
    if files or npm:
        check(raw, "Missing frontend inventory for " + path.name)
        bom = json.loads(raw)
        root = bom["metadata"]["component"]
        check((bom["bomFormat"], bom["specVersion"], root["name"], root["version"]) ==
              ("CycloneDX", "1.6", package_id, version), "SBOM identity mismatch")
        entries = root.get("components", [])
        actual = {c["name"]: c["hashes"] for c in entries if c["type"] == "file"}
        check(len(entries) == len(actual) and actual == {n: [{"alg": "SHA-256", "content": v}] for n, v in files.items()},
              "SBOM payload mismatch in " + path.name)
        if npm:
            groups = ("dependencies", "optionalDependencies", "peerDependencies")
            declarations = {f"elsa:{group}:{dep}": value for group in groups
                            for dep, value in manifest.get(group, {}).items()}
            properties = {p["name"]: p["value"] for p in root.get("properties", [])
                          if p["name"].startswith(tuple("elsa:" + g + ":" for g in groups))}
            check(properties == declarations, "Packed dependency declarations differ from SBOM")
        row["sbom_sha256"] = sha256(raw)
    else:
        check(raw is None, "Unexpected frontend inventory for " + path.name)
    return row, raw


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", type=Path, help="One package, or the workflow package directory")
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument("--write", choices=["npm", "nuget"], help="Write the workflow receipt and sidecars")
    modes.add_argument("--check", choices=["npm", "nuget"], help="Check transferred archives and original producer attempt before signing")
    modes.add_argument("--payload", type=Path, help="Write a NuGet payload witness for independent provenance verification")
    parser.add_argument("--signed-retry", action="store_true", help="Allow an earlier producer attempt when verifying a previously signed receipt")
    args = parser.parse_args()
    check(not args.signed_retry or args.check, "Signed retry requires a receipt check")
    mode = args.write or args.check
    if not mode:
        row, _ = inspect(args.path)
        if args.payload:
            check(args.path.suffix == ".nupkg", "Payload witnesses apply to NuGet packages")
            args.payload.write_bytes(nuget_payload(args.path))
        print(json.dumps(row, indent=2))
        return
    expected_files = set()

    def sidecar(name, content):
        file = args.path / name
        expected_files.add(name)
        if args.write:
            file.write_bytes(content)
        else:
            check(file.read_bytes() == content, "Transferred evidence mismatch: " + name)

    rows = []
    for path in sorted(args.path.glob("*.tgz" if mode == "npm" else "*.nupkg")):
        row, raw = inspect(path)
        check(row["version"] == os.environ["VERSION"], "Unexpected release version")
        if raw:
            bom = json.loads(raw)
            properties = {p["name"]: p["value"] for p in bom["metadata"]["properties"]}
            for key, env in [("repository", "GITHUB_REPOSITORY"), ("commit", "GITHUB_SHA"),
                             ("producer-run", "GITHUB_RUN_ID"), ("producer-attempt", "GITHUB_RUN_ATTEMPT")]:
                value = properties["elsa:" + key]
                if args.signed_retry and key == "producer-attempt":
                    check(0 < int(value) <= int(os.environ[env]), "Invalid signed producer attempt")
                elif key == "producer-attempt":
                    check(value == os.environ[env], "Unexpected producer-attempt for unsigned candidates. Use Re-run all jobs to rebuild both package sets in the same attempt before signing.")
                else:
                    check(value == os.environ[env], "Unexpected producer " + key)
            sidecar(path.name + ".cdx.json", raw)
        if mode == "nuget":
            witness = nuget_payload(path)
            row["payload_sha256"] = sha256(witness)
            sidecar(path.name + ".payload.json", witness)
        rows.append(row)
    check(rows, "No candidate archives")
    sidecar(mode + "-subjects.json", (json.dumps(rows, indent=2) + "\n").encode())
    actual = {p.name for pattern in ("*.cdx.json", "*.payload.json", "*-subjects.json") for p in args.path.glob(pattern)}
    check(actual == expected_files, "Unexpected evidence file set")


if __name__ == "__main__":
    main()
