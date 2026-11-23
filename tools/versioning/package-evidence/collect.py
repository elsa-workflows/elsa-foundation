"""Read the original pack's ZIPs and NuGet restore outputs, without another restore."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def framework(group):
    # NuGet writes netstandard2.0 as .NETStandard2.0 in nuspec XML.
    return group.get("targetFramework", "").lower().lstrip(".")


def read_package(path):
    with zipfile.ZipFile(path) as archive:
        names = [entry.filename for entry in archive.infolist() if not entry.is_dir()]
        if len(names) != len(set(names)):
            raise ValueError(f"Duplicate ZIP entries in {path}")
        nuspec, = [name for name in names if "/" not in name and name.endswith(".nuspec")]
        metadata = ET.fromstring(archive.read(nuspec)).find("{*}metadata")
        types = [item.get("name") for item in metadata.findall("{*}packageTypes/{*}packageType")]
        payload = []
        runtimes = {}
        for name in names:
            data = archive.read(name)
            entry = {"path": name, "sha256": sha256(data)}
            if "Template" in types and name.lower().endswith((".csproj", ".props", ".targets")):
                # These ranges belong to the project a consumer can generate.
                entry["template_references"] = [item.attrib for item in ET.fromstring(data).iter()
                                                if item.tag.split("}")[-1] in ("PackageReference", "PackageVersion")]
            if "DotnetTool" in types and name.endswith(".deps.json"):
                runtime = json.loads(data)
                target = runtime["runtimeTarget"]["name"]
                runtimes[name] = {key: {**definition, "type": runtime["libraries"][key]["type"]}
                                  for key, definition in runtime["targets"][target].items()}
            payload.append(entry)
        if "DotnetTool" in types and not runtimes:
            raise ValueError(f"{path}: tool has no shipped runtime dependency manifest")
        groups = {framework(group): [item.attrib for item in group.findall("{*}dependency")]
                  for group in metadata.findall("{*}dependencies/{*}group")}
        ungrouped = [item.attrib for item in metadata.findall("{*}dependencies/{*}dependency")]
        references = {framework(group): [ref.get("name") for ref in group.findall("{*}frameworkReference")]
                      for group in metadata.findall("{*}frameworkReferences/{*}group")}
        return {"id": metadata.findtext("{*}id"), "version": metadata.findtext("{*}version"),
                "commit": metadata.find("{*}repository").get("commit"),
                "fingerprint": json.loads(archive.read("elsa-input-fingerprint.json"))["fingerprint"],
                "kind": "tool" if "DotnetTool" in types else "template" if "Template" in types else "library",
                "groups": groups, "ungrouped": ungrouped, "shared": references, "runtimes": runtimes,
                "payload": payload, "name": path.name, "digest": sha256(path.read_bytes())}


def collect(source, plan, run, attempt):
    computation = json.loads((plan / "computation.json").read_bytes())
    package_ids = {row["package_id"].lower() for row in computation["packages"]}
    wanted = {row["package_id"].lower(): row for row in computation["packages"] if row["affected"]}
    output = plan / "sboms"
    output.mkdir()
    records = []
    with tempfile.TemporaryDirectory() as scratch:
        inputs = Path(scratch)
        for index, package in enumerate(sorted((plan / "nuget").glob("*.nupkg"))):
            row = read_package(package)
            computed = wanted.pop(row["id"].lower())
            project = (source / computed["path"]).resolve()
            assets_path = project.parent / "obj" / "project.assets.json"
            assets_bytes = assets_path.read_bytes()
            assets = json.loads(assets_bytes)
            restore = assets["project"]["restore"]
            if (Path(restore["projectPath"]).resolve() != project or
                    restore["projectName"].lower() != row["id"].lower() or
                    assets["project"]["version"] != row["version"] or
                    row["commit"] != computation["commit"] or row["fingerprint"] != computed["fingerprint"]):
                raise ValueError(f"{row['id']}: package, plan and original restore disagree")
            # Use only inventories and fingerprints. Absolute runner paths never enter a public SBOM.
            lock = project.parent / "packages.lock.json"
            row.update(targets=assets["targets"], frameworks=list(assets["project"]["frameworks"]),
                       lock_sha256=sha256(lock.read_bytes()), assets_sha256=sha256(assets_bytes),
                       producer_run=run, producer_attempt=attempt, package_ids=sorted(package_ids))
            (inputs / f"{index}.json").write_text(json.dumps(row))
            records.append({"id": row["id"], "version": row["version"], "name": row["name"], "sha256": row["digest"]})
        if wanted:
            raise ValueError(f"Missing affected packages: {', '.join(wanted)}")
        subprocess.run(["node", str(Path(__file__).with_name("generate.mjs")), inputs, output], check=True)
    for record in records:
        record["sbom_sha256"] = sha256((output / (record["name"] + ".cdx.json")).read_bytes())
    (output / "subjects.json").write_text(json.dumps(records, indent=2) + "\n")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("plan", type=Path)
    parser.add_argument("--run", required=True)
    parser.add_argument("--attempt", required=True)
    args = parser.parse_args()
    collect(args.source, args.plan, args.run, args.attempt)
