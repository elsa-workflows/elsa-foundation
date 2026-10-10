from pathlib import Path
import base64
import hashlib
import json
import sys
import xml.etree.ElementTree as ET
import zipfile


if len(sys.argv) != 2:
    raise SystemExit("Usage: python3 verify-stable-consumer.py <phase>")

root = Path(__file__).resolve().parent
phase = sys.argv[1]
evidence = root / "evidence" / phase
execution = json.loads((evidence / "execution.json").read_text(encoding="utf-8"))
cshells_version = execution["cshellsVersion"]
nuplane_version = execution["nuplaneVersion"]
cshells_source = execution["cshellsPublicSource"]
nuplane_source = execution["nuplanePublicSource"]
expected_source = "https://api.nuget.org/v3/index.json"
if cshells_source != expected_source or nuplane_source != expected_source:
    raise SystemExit("Both public package families must use the canonical NuGet.org source.")
cshells_archives = Path(execution["cshellsAuditedArchiveDirectory"])
nuplane_archives = Path(execution["nuplaneAuditedArchiveDirectory"])
cache = Path(execution["packageCache"])
program = root / "consumer" / "Program.cs"
if hashlib.sha256(program.read_bytes()).hexdigest() != execution["consumerProgramSha256"]:
    raise SystemExit("Consumer Program.cs hash differs from the pre-run snapshot.")

project = root / "consumer" / "Consumer.csproj"
project_xml = ET.parse(project)
if any(node.tag.endswith("ProjectReference") for node in project_xml.iter()):
    raise SystemExit("The public-package consumer must not contain ProjectReferences.")
assets = json.loads((root / "consumer" / "obj" / "project.assets.json").read_text(encoding="utf-8"))
libraries = assets.get("libraries", {})
if any(value.get("type") == "project" for value in libraries.values()):
    raise SystemExit("The restored package graph contains a project reference.")

resolved = []


def find_archive(directory, package_id, version):
    expected_name = f"{package_id}.{version}.nupkg"
    matches = [path for path in directory.iterdir() if path.is_file() and path.name.casefold() == expected_name.casefold()]
    if len(matches) != 1:
        raise SystemExit(f"Expected exactly one audited archive matching {expected_name} in {directory}; found {len(matches)}.")
    return matches[0]


for key, library in sorted(libraries.items()):
    if library.get("type") != "package":
        continue
    package_id, version = key.rsplit("/", 1)
    package_lower = package_id.lower()
    if package_lower.startswith("cshells"):
        expected_version, source, archive_dir = cshells_version, cshells_source, cshells_archives
    elif package_lower.startswith("nuplane"):
        expected_version, source, archive_dir = nuplane_version, nuplane_source, nuplane_archives
    else:
        continue
    if version.lower() != expected_version.lower():
        raise SystemExit(f"Unexpected package version in assets: {key}")

    metadata_path = cache / package_lower / version.lower() / ".nupkg.metadata"
    cached_archive = metadata_path.parent / f"{package_lower}.{version.lower()}.nupkg"
    audited_archive = find_archive(archive_dir, package_id, version)
    if not metadata_path.is_file() or not cached_archive.is_file() or not audited_archive.is_file():
        raise SystemExit(f"Missing restored or audited archive for {key}.")
    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
    if metadata.get("source") != source:
        raise SystemExit(f"Unexpected NuGet source for {key}: {metadata.get('source')!r}")

    cached_bytes = cached_archive.read_bytes()
    audited_bytes = audited_archive.read_bytes()
    cached_sha256 = hashlib.sha256(cached_bytes).hexdigest()
    audited_sha256 = hashlib.sha256(audited_bytes).hexdigest()
    archive_sha512 = base64.b64encode(hashlib.sha512(cached_bytes).digest()).decode("ascii")
    content_hash = metadata.get("contentHash")
    assets_hash = library.get("sha512")
    if cached_sha256 != audited_sha256:
        raise SystemExit(f"Restored bytes differ from the audited archive for {key}.")
    if not content_hash or content_hash != assets_hash or assets_hash != archive_sha512:
        raise SystemExit(f"NuGet archive/content hashes disagree for {key}.")
    resolved.append({
        "id": package_id,
        "version": version,
        "restoreSource": source,
        "auditedArchive": str(audited_archive),
        "sha256": audited_sha256,
        "sha512": archive_sha512,
    })

for major in (8, 9, 10):
    result = json.loads((evidence / "logs" / f"net{major}.log").read_text(encoding="utf-8"))
    if result["targetFramework"] != f"net{major}.0" or not result["runtime"].startswith(f".NET {major}."):
        raise SystemExit(f"Runtime/TFM mismatch for net{major}.0: {result.get('runtime')!r}")
    if result["adapterPackageVersion"] != cshells_version or result["nuplanePackageVersion"] != nuplane_version:
        raise SystemExit(f"Package version mismatch in the net{major}.0 result.")
    if len(result["scenarios"]) != 5 or len(result["loadedPackageAssets"]) != 7:
        raise SystemExit(f"Expected five behavior groups and seven loaded package assets on net{major}.0.")
    for assembly in result["loadedPackageAssets"]:
        if assembly["loadedSha256"] != assembly["packageAssetSha256"]:
            raise SystemExit(f"Loaded assembly hash mismatch for {assembly['assemblyFullName']}.")
        package_id = assembly["assemblyFullName"].split(",", 1)[0]
        if package_id.lower().startswith("cshells"):
            version, archive_dir = cshells_version, cshells_archives
        else:
            version, archive_dir = nuplane_version, nuplane_archives
        archive_path = find_archive(archive_dir, package_id, version)
        with zipfile.ZipFile(archive_path) as archive:
            asset = archive.getinfo(assembly["packageAssetPath"])
            asset_sha256 = hashlib.sha256(archive.read(asset)).hexdigest()
        if asset_sha256 != assembly["loadedSha256"]:
            raise SystemExit(f"Loaded assembly does not match {archive_path}.")

if any((root / "runs").glob("*")):
    raise SystemExit("Owned scenario run roots remain after qualification.")

result = {
    "result": "PASS",
    "phase": phase,
    "cshellsVersion": cshells_version,
    "nuplaneVersion": nuplane_version,
    "targetFrameworks": ["net8.0", "net9.0", "net10.0"],
    "resolvedPublicFamilyPackagesMatchedAuditedArchives": resolved,
    "ownedScenarioRunRootsRemaining": [],
    "scope": "real Nuplane producer/autoload to adapter, catalog, and serving shell; no pruning claim",
}
(evidence / "qualification.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, indent=2))
