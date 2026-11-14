#!/usr/bin/env python3
"""Pack and consume the Runtime release bridge without project references or Design packages."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from urllib.parse import urlsplit
import xml.etree.ElementTree as ET
import zipfile


def run(*args: str, cwd: Path) -> None:
    subprocess.run(args, cwd=cwd, check=True)


def package_identity(package: Path) -> tuple[str, str]:
    with zipfile.ZipFile(package) as archive:
        nuspec = next(name for name in archive.namelist() if name.endswith(".nuspec"))
        root = ET.fromstring(archive.read(nuspec))
        metadata = next(child for child in root if child.tag.rsplit("}", 1)[-1] == "metadata")
        fields = {child.tag.rsplit("}", 1)[-1]: child.text for child in metadata}
        return fields["id"], fields["version"]


def source_state(repo: Path) -> dict:
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip()
    status = subprocess.check_output(["git", "status", "--porcelain"], cwd=repo)
    fingerprint = hashlib.sha256(subprocess.check_output(["git", "diff", "--binary", "HEAD"], cwd=repo))
    untracked = subprocess.check_output(["git", "ls-files", "--others", "--exclude-standard", "-z"], cwd=repo)
    for raw_path in sorted(filter(None, untracked.split(b"\0"))):
        path = repo / os.fsdecode(raw_path)
        fingerprint.update(raw_path + b"\0")
        content = os.fsencode(os.readlink(path)) if path.is_symlink() else path.read_bytes()
        fingerprint.update(hashlib.sha256(content).digest())
    return {"commit": commit, "dirty": bool(status.strip()), "fingerprint": fingerprint.hexdigest()}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="New owned output directory; default is a retained temporary directory.")
    parser.add_argument("--graph-owner", action="store_true", help="Also pack and resolve the optional compiler-free Graph owner.")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    initial_source = source_state(repo)
    output = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix="elsa-release-runtime-"))
    if args.output:
        output.mkdir(parents=True, exist_ok=False)
    feed, consumer = output / "feed", output / "consumer"
    feed.mkdir()
    consumer.mkdir()
    root_project = repo / "Essentials/src/Workflows/Publishing/Releases/Runtime/Elsa.Workflows.Publishing.Releases.Runtime/Elsa.Workflows.Publishing.Releases.Runtime.csproj"
    projects: list[Path] = []
    visited: set[Path] = set()
    def visit(project: Path) -> None:
        project = project.resolve()
        if project in visited:
            return
        if not project.is_relative_to(repo) or "Design" in project.parts or project.stem == "Elsa.Workflows.Publishing.Core":
            raise RuntimeError("Runtime consumer reached a non-Runtime project.")
        visited.add(project)
        for reference in ET.parse(project).getroot().iter("ProjectReference"):
            if reference.get("Condition") or "$(" in reference.attrib["Include"]:
                raise RuntimeError("Consumer closure requires unconditional concrete project references.")
            visit(project.parent / reference.attrib["Include"].replace("\\", "/"))
        projects.append(project)
    visit(root_project)
    graph_project = repo / "Essentials/src/Activities/Graph/Releases/Elsa.Activities.Graph.Releases.csproj"
    if args.graph_owner:
        visit(graph_project)
    for project in projects:
        run("dotnet", "pack", str(project), "--configuration", "Release", "--output", str(feed), cwd=repo)
    packages = {package_identity(path)[0]: (package_identity(path)[1], path) for path in feed.glob("*.nupkg")}
    expected = {project.stem for project in projects}
    if set(packages) != expected:
        raise RuntimeError(f"Unexpected packed package set: {sorted(packages)}")
    versions = {package: value[0] for package, value in packages.items()}
    central = ET.parse(repo / "Directory.Packages.props").getroot()
    di_version = next(item.attrib["Version"] for item in central.iter("PackageVersion")
                      if item.attrib.get("Include") == "Microsoft.Extensions.DependencyInjection")
    consumer_project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(consumer_project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net10.0", "ImplicitUsings": "enable", "Nullable": "enable"}.items():
        ET.SubElement(properties, key).text = value
    references = ET.SubElement(consumer_project, "ItemGroup")
    ET.SubElement(references, "PackageReference", Include=root_project.stem, Version=versions[root_project.stem])
    if args.graph_owner:
        ET.SubElement(references, "PackageReference", Include=graph_project.stem, Version=versions[graph_project.stem])
    ET.SubElement(references, "PackageReference", Include="Microsoft.Extensions.DependencyInjection", Version=di_version)
    ET.ElementTree(consumer_project).write(consumer / "Consumer.csproj", encoding="unicode")
    program = (repo / "tools/releases/RuntimeConsumer/Program.cs.template").read_text()
    if args.graph_owner:
        program = program.replace("// OPTIONAL_OWNER_REGISTRATION", "new Elsa.Activities.Graph.Releases.GraphActivitiesReleaseVerificationFeature().ConfigureServices(services);")
        program = program.replace("// OPTIONAL_OWNER_ASSERTIONS", """
var graphOwner = scope.ServiceProvider.GetServices<IReleaseOccurrenceInspector>().Single(value => value.Identity.Name == "elsa.graph-release-profile");
if (!graphOwner.SupportedSites.Contains(new(ReleaseRuntimeSiteKinds.TemplateOverlay, WellKnownRuntimeActivityConsumers.GraphActivity, 1)))
    throw new InvalidOperationException("The installed Graph owner did not resolve its consumer-specific overlay claim.");
var unrelated = await graphOwner.InspectAsync(new(content.Members[0].Key, [], "root", null,
    new(ReleaseRuntimeSiteKinds.Descriptor, "fixture.unrelated", 1), JsonSerializer.SerializeToElement(new { nodeId = "root" })), content);
if (unrelated.Status != ReleaseSemanticStatus.Unsupported || !unrelated.Diagnostics.Any(value => value.Code == "graph_site_unsupported"))
    throw new InvalidOperationException("The installed Graph owner accepted another consumer's site.");
""")
    (consumer / "Program.cs").write_text(program)
    # An isolated cache prevents an older package with the same ID/version from satisfying this witness.
    # Copy only public source locations from the committed producer configuration, never credentials.
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="runtime-witness", value=str(feed))
    mappings = ET.SubElement(config, "packageSourceMapping")
    local_mapping = ET.SubElement(mappings, "packageSource", key="runtime-witness")
    for package in sorted(expected):
        ET.SubElement(local_mapping, "package", pattern=package)
    producer_config = ET.parse(repo / "NuGet.config").getroot()
    for source in producer_config.findall("./packageSources/add"):
        location, key = source.attrib["value"], source.attrib["key"]
        parsed = urlsplit(location)
        if parsed.scheme != "https" or parsed.username or parsed.password or parsed.query or parsed.fragment:
            raise RuntimeError("Consumer witness requires public HTTPS producer feed locations without embedded credentials.")
        ET.SubElement(sources, "add", key=key, value=location)
        remote_mapping = ET.SubElement(mappings, "packageSource", key=key)
        ET.SubElement(remote_mapping, "package", pattern="*")
    config_path = consumer / "NuGet.config"
    ET.ElementTree(config).write(config_path, encoding="unicode")
    isolated_packages = output / "packages"
    run("dotnet", "restore", "Consumer.csproj", "--configfile", str(config_path), "--packages", str(isolated_packages), cwd=consumer)
    assets = json.loads((consumer / "obj/project.assets.json").read_text())
    libraries = assets["libraries"]
    forbidden = [name for name in libraries if name.startswith("Elsa.") and name.split("/")[0] not in expected]
    if forbidden or any(item["type"] != "package" for item in libraries.values()):
        raise RuntimeError(f"Consumer package boundary failed: {forbidden}")
    for package, (version, packed) in packages.items():
        resolved_name = next(name for name in libraries if name.split("/")[0].casefold() == package.casefold())
        resolved = libraries[resolved_name]
        normalized_version = resolved_name.split("/", 1)[1]
        consumed = isolated_packages / resolved["path"] / f"{package.lower()}.{normalized_version.lower()}.nupkg"
        if hashlib.sha256(consumed.read_bytes()).digest() != hashlib.sha256(packed.read_bytes()).digest():
            raise RuntimeError(f"Consumer did not restore the exact locally packed {package} archive.")
    run("dotnet", "run", "--project", "Consumer.csproj", "--configuration", "Release", "--no-restore", cwd=consumer)
    for package, (_, packed) in packages.items():
        with zipfile.ZipFile(packed) as archive:
            expected_dll = archive.read(f"lib/net10.0/{package}.dll")
        consumed_dll = (consumer / "bin/Release/net10.0" / f"{package}.dll").read_bytes()
        if hashlib.sha256(consumed_dll).digest() != hashlib.sha256(expected_dll).digest():
            raise RuntimeError(f"Consumer executed a different {package} assembly from the packed artifact.")
    final_source = source_state(repo)
    unchanged = initial_source == final_source
    result = {
        "sourceCommit": initial_source["commit"], "dirty": initial_source["dirty"],
        "sourceTreeFingerprint": initial_source["fingerprint"], "sourceStateUnchanged": unchanged,
        "finalSource": final_source,
        "packages": [{"id": package, "version": version, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                     for package, (version, path) in sorted(packages.items())],
        "consumerLibraries": sorted(libraries), "outcome": "passed" if unchanged else "incomplete",
        "scope": "Runtime package closure, template hash/codec/owner and bounds only",
        "semanticValidation": "template owner only; mandatory module sites remain unverified", "promotion": "not attempted",
        "graphOwnerRegistration": args.graph_owner,
    }
    (output / "result.json").write_text(json.dumps(result, indent=2) + "\n")
    print(json.dumps({"result": str(output / "result.json"), "outcome": result["outcome"]}))
    if not unchanged:
        raise SystemExit(6)


if __name__ == "__main__":
    main()
