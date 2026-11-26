#!/usr/bin/env python3
"""Assemble a locked Foundation package feed."""

import argparse
import base64
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile


TOOL_DIRECTORY = Path(__file__).resolve().parent
FOUNDATION_DIRECTORY = TOOL_DIRECTORY / "foundation"
DEPS_FILE = "Elsa.Foundation.Host.deps.json"
FOUNDATION_FILES = (DEPS_FILE, "Elsa.Foundation.Host.dll", "appsettings.json", "appsettings.Production.json")
FEED_NAME = "profile-packages"
MANIFEST_FILE = "profile-desired.json"
# A fixed timestamp keeps identical package sets byte-identical.
PROFILE_TIME = "1970-01-01T00:00:00Z"
PACKAGE_ID = r"[A-Za-z0-9_][A-Za-z0-9._-]*"
VERSION = r"[0-9]+(?:\.[0-9]+){2,3}(?:-[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*)?"
ROOT_LINE = re.compile(rf"({PACKAGE_ID})(?:\s+\[({VERSION})\])?")
PACK_REFERENCE = re.compile(r"@([a-z0-9]+(?:-[a-z0-9]+)*)")
PROJECT_CONDITION = re.compile(rf"'\$\(MSBuildProjectName\)' == '({PACKAGE_ID})'")
JSONC_TOKEN = re.compile(r'("(?:\\.|[^"\\])*")|//[^\r\n]*|/\*.*?\*/', re.DOTALL)


def json_data(path):
    return json.loads(path.read_text(encoding="utf-8"))


def json_configuration(path):
    configuration = JSONC_TOKEN.sub(lambda token: token[1] or "", path.read_text(encoding="utf-8-sig"))
    return json.loads(configuration)


def merge_configuration(base, overrides):
    if not isinstance(base, dict) or not isinstance(overrides, dict):
        raise ValueError("Configuration files must contain JSON objects")
    for key, value in overrides.items():
        current = base.get(key)
        if isinstance(current, dict) and isinstance(value, dict):
            merge_configuration(current, value)
        else:
            base[key] = value


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def run(command, **kwargs):
    return subprocess.run(command, check=True, text=True, **kwargs)


def foundation_hashes():
    return {
        name: hashlib.sha256((FOUNDATION_DIRECTORY / name).read_bytes()).hexdigest()
        for name in FOUNDATION_FILES
    }


def read_versions(path):
    versions = {}
    for group in ET.parse(path).getroot().findall("PropertyGroup"):
        version = group.findtext("ElsaComputedPackageVersion")
        if version is None:
            continue
        project = PROJECT_CONDITION.fullmatch(group.get("Condition", ""))
        if project is None or re.fullmatch(VERSION, version) is None:
            raise ValueError(f"Invalid package version group in {path}: {group.attrib}")
        package_id = project[1]
        if package_id.lower() in versions:
            raise ValueError(f"Duplicate version for {package_id} in {path}")
        versions[package_id.lower()] = (package_id, version)
    if not versions:
        raise ValueError(f"No computed package versions found in {path}")
    return versions


def manifest_lines(path):
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        line = line.partition("#")[0].strip()
        if line:
            yield line, f"{path}:{number}"


def expand_pack_references(path, packs_directory):
    included = {}
    for line, location in manifest_lines(path):
        if not line.startswith("@"):
            yield line, location
            continue
        reference = PACK_REFERENCE.fullmatch(line)
        if reference is None:
            raise ValueError(f"{location} must use a pack name like '@engine'")
        if line in included:
            raise ValueError(f"{location} repeats pack {line}, first included at {included[line]}")
        included[line] = location
        pack = packs_directory / f"{reference[1]}.txt"
        if not pack.is_file():
            raise ValueError(f"{location} references unknown pack {line}: expected {pack}")
        entries = list(manifest_lines(pack))
        if not entries:
            raise ValueError(f"{location} references empty pack {line}: {pack}")
        for entry, entry_location in entries:
            if entry.startswith("@"):
                raise ValueError(f"{entry_location} contains a nested pack reference; include it at {path} instead")
            yield entry, entry_location


def read_roots(path, versions, packs_directory):
    roots = {}
    locations = {}
    for line, location in expand_pack_references(path, packs_directory):
        root = ROOT_LINE.fullmatch(line)
        if root is None:
            raise ValueError(f"{location} must contain a package ID or an exact 'ID [version]' pin")
        package_id, explicit_version = root.groups()
        key = package_id.lower()
        computed = versions.get(key)
        if computed and explicit_version and explicit_version.lower() != computed[1].lower():
            raise ValueError(f"{location} pins {package_id} to {explicit_version}, version plan has {computed[1]}")
        if not explicit_version and not computed:
            raise ValueError(f"{location} needs an exact version pin for {package_id}; it is absent from the builder's version plan")
        resolved = computed or (package_id, explicit_version)
        if key in roots:
            first_version = roots[key][1]
            if first_version.lower() != resolved[1].lower():
                raise ValueError(
                    f"{location} pins {package_id} to {resolved[1]}, conflicting with {first_version} at {locations[key]}"
                )
            raise ValueError(f"{location} repeats {package_id}, first listed at {locations[key]}")
        roots[key] = resolved
        locations[key] = location
    if not roots:
        raise ValueError(f"{path} must name at least one package")
    return roots


def package_identities(dependency_graph):
    for identity in dependency_graph:
        package_id, version = identity.split("/", 1)
        yield identity, package_id, version


def foundation_pins(host_directory, versions, roots, configuration, production):
    deps = json_data(host_directory / DEPS_FILE)["libraries"]
    shared = production.get("Nuplane", {}).get("HostProvidedPackages", configuration["Nuplane"].get("HostProvidedPackages", []))
    host_shares = {package_id.lower() for package_id in shared if package_id}
    host_versions = {}
    for _, package_id, version in package_identities(deps):
        key = package_id.lower()
        expected = roots.get(key) or versions.get(key)
        if key in host_shares and expected and version.lower() != expected[1].lower():
            raise ValueError(f"Foundation image supplies {package_id} {version}, profile requires {expected[1]}")
        # Keep Elsa modules that may export features, including Elsa.Api.AspNetCore, and always keep explicit roots.
        if key not in roots and (not key.startswith("elsa.") or key in host_shares):
            host_versions[key] = (package_id, version)
    mandatory_shares = {key: value for key, value in host_versions.items() if key in host_shares}
    return host_versions, mandatory_shares


def default_nuget_config(path):
    configuration = ET.parse(TOOL_DIRECTORY / "NuGet.config")
    mappings = configuration.getroot().find("packageSourceMapping")
    foundation = mappings.find("packageSource[@key='Elsa 4 Preview']")
    ET.SubElement(foundation, "package", pattern="Elsa.*")
    ET.SubElement(foundation, "package", pattern="Elsa3.*")
    ET.SubElement(mappings.find("packageSource[@key='nuget.org']"), "package", pattern="*")
    configuration.write(path, encoding="utf-8", xml_declaration=True)
    return path


def restore_graph(directory, pins, roots, config, reviewed_lock):
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    ET.SubElement(properties, "TargetFramework").text = "net10.0"
    references = ET.SubElement(project, "ItemGroup")
    for package_id, _ in sorted(roots.values()):
        ET.SubElement(references, "PackageReference", Include=package_id)
    central_versions = ET.Element("Project")
    central_options = ET.SubElement(central_versions, "PropertyGroup")
    ET.SubElement(central_options, "ManagePackageVersionsCentrally").text = "true"
    ET.SubElement(central_options, "CentralPackageTransitivePinningEnabled").text = "true"
    central = ET.SubElement(central_versions, "ItemGroup")
    for package_id, version in sorted(pins.values()):
        ET.SubElement(central, "PackageVersion", Include=package_id, Version=f"[{version}]")
    ET.ElementTree(central_versions).write(directory / "Directory.Packages.props", encoding="utf-8", xml_declaration=True)
    project_path = directory / "profile.csproj"
    ET.ElementTree(project).write(project_path, encoding="utf-8", xml_declaration=True)
    lock = directory / "packages.lock.json"
    command = [
        "dotnet", "restore", str(project_path), "--configfile", str(config),
        "--packages", str(directory / "packages"), "--use-lock-file", "--lock-file-path", str(lock),
        # Nuplane resolves nuspec dependencies even when the SDK would prune shared-framework packages.
        "-p:RestoreEnablePackagePruning=false", "-p:NuGetAudit=false",
        "-p:WarningsAsErrors=NU1603%3BNU1605%3BNU1608",
    ]
    if reviewed_lock is not None:
        shutil.copyfile(reviewed_lock, lock)
        command.append("--locked-mode")
    run(command, cwd=TOOL_DIRECTORY)
    return json_data(directory / "obj" / "project.assets.json"), lock


def stage_libraries(graph, host, roots, restore_packages, output):
    feed = output / FEED_NAME
    feed.mkdir()
    activities = output / "profile-activities"
    activities.mkdir()
    assemblies = {}
    lock_entries = []
    target = graph["targets"]["net10.0"]
    for identity, package_id, version in sorted(package_identities(target), key=lambda package: package[0].lower()):
        package_key = package_id.lower()
        provided = host.get(package_key)
        if provided and provided[1].lower() == version.lower():
            continue
        library = graph["libraries"][identity]
        package_directory = restore_packages / library["path"]
        filename = f"{package_id.lower()}.{version.lower()}.nupkg"
        archive = package_directory / filename
        with archive.open("rb") as source:
            digest = hashlib.sha512()
            for chunk in iter(lambda: source.read(1024 * 1024), b""):
                digest.update(chunk)
        content_hash = base64.b64encode(digest.digest()).decode("ascii")
        # NuGet may omit repository signatures from its graph content hash. Nuplane hashes the exact archive.
        restored_hash = archive.with_suffix(".nupkg.sha512").read_text(encoding="ascii").strip()
        if content_hash != restored_hash:
            raise ValueError(f"SHA-512 of {filename} differs from the restored archive hash")
        shutil.copyfile(archive, feed / filename)
        lock_entries.append({
            "id": package_id,
            "version": version,
            "feed": FEED_NAME,
            "hash": f"sha512:{content_hash}",
            "timestamp": PROFILE_TIME,
        })
        # Scan explicit roots (including third-party activities) and Elsa packages reached through dependencies.
        if package_key in roots or package_key.startswith(("elsa.", "elsa3.")):
            with zipfile.ZipFile(archive) as package:
                for asset in sorted(target[identity].get("runtime", {})):
                    if not asset.endswith(".dll"):
                        continue
                    name = Path(asset).name
                    content = package.read(asset)
                    existing = assemblies.get(name.lower())
                    if existing:
                        if existing.read_bytes() != content:
                            raise ValueError(f"Two packages supply different copies of the activity scan assembly {name}")
                        continue
                    destination = activities / name
                    destination.write_bytes(content)
                    assemblies[name.lower()] = destination
    write_json(output / "nuplane.lock.json", {"schemaVersion": "2.0", "generatedAt": PROFILE_TIME, "packages": lock_entries})
    return len(lock_entries)


def stage_configuration(configuration, production, output, allow_add_ons):
    if production.get("Nuplane", {}).get("Setup", {}).get("Feeds"):
        raise ValueError("Foundation production settings already declare feeds. The profile would inherit extra sources.")
    nuplane = configuration["Nuplane"]
    nuplane["Setup"]["Feeds"] = [{
        "Name": FEED_NAME,
        "DirectoryPath": FEED_NAME,
        "Directory": {"Role": "Cache", "Watch": False},
    }]
    nuplane.setdefault("Convergence", {})["Manifest"] = {"Enabled": True, "Path": f"/app/{MANIFEST_FILE}"}
    if allow_add_ons:
        # Module management uses the first directory as its upload target.
        nuplane["Setup"]["Feeds"].insert(0, {
            "Name": "local-packages",
            "DirectoryPath": "packages",
            "IncludePatterns": ["*"],
            "Directory": {"Watch": True, "DebounceWindow": "00:00:01"},
        })
    nuplane.setdefault("FeedResolution", {})["OfflineMode"] = not allow_add_ons
    nuplane["LockFile"] = {
        "Mode": "Enforce" if allow_add_ons else "Strict",
        "Path": "/app/nuplane.lock.json",
        "FailOnHashMismatch": True,
    }
    nuplane.setdefault("Reconciliation", {})["StartupFailurePolicy"] = "FailHost"
    nuplane["Loading"]["DefaultLoadMode"] = "HostIntegrated"
    write_json(output / "appsettings.json", configuration)


def stage_manifest(runtime_lock, output):
    write_json(output / MANIFEST_FILE, {
        "schemaVersion": "1.0",
        "generatedAtUtc": runtime_lock["generatedAt"],
        "packages": [
            {"id": package["id"], "version": package["version"], "sourceHint": package["feed"]}
            for package in runtime_lock["packages"]
        ],
    })


def assemble(args):
    output = args.output.resolve()
    profile = Path.cwd()
    versions = read_versions(TOOL_DIRECTORY / "package-versions.props")
    roots = read_roots(profile / "packages.txt", versions, args.packs_directory)
    reviewed_lock = None
    if args.locked:
        reviewed_lock = profile / "packages.lock.json"
        reviewed_runtime_lock = profile / "nuplane.lock.json"
        for lock in (reviewed_lock, reviewed_runtime_lock):
            if not lock.is_file():
                raise ValueError(f"--locked requires {lock}")
    output.mkdir(parents=True, exist_ok=True)
    if any(output.iterdir()):
        raise ValueError(f"Output directory must be empty: {output}")
    # Stage on the output filesystem because /out may be a bind mount.
    context = output / ".assembling"
    context.mkdir()
    try:
        app = context / "app"
        app.mkdir()
        with tempfile.TemporaryDirectory(prefix="elsa-profile-restore-") as temporary:
            restore = Path(temporary)
            configuration = json_configuration(FOUNDATION_DIRECTORY / "appsettings.json")
            production = json_configuration(FOUNDATION_DIRECTORY / "appsettings.Production.json")
            host, shared = foundation_pins(FOUNDATION_DIRECTORY, versions, roots, configuration, production)
            if args.appsettings_directory:
                for name, settings in [("appsettings.json", configuration), ("appsettings.Production.json", production)]:
                    overlay = args.appsettings_directory / name
                    if overlay.is_file():
                        merge_configuration(settings, json_configuration(overlay))
                write_json(app / "appsettings.Production.json", production)
            config = args.nuget_config.resolve() if args.nuget_config else default_nuget_config(restore / "NuGet.config")
            graph, lock = restore_graph(restore, {**shared, **versions, **roots}, roots, config, reviewed_lock)
            count = stage_libraries(graph, host, roots, restore / "packages", app)
            runtime_lock = app / "nuplane.lock.json"
            runtime_lock_data = json_data(runtime_lock)
            if args.locked and json_data(reviewed_runtime_lock) != runtime_lock_data:
                raise ValueError(f"Resolved archive hashes differ from {reviewed_runtime_lock}")
            stage_manifest(runtime_lock_data, app)
            stage_configuration(configuration, production, app, args.allow_add_ons)
            shutil.copyfile(lock, context / "packages.lock.json")
            shutil.copyfile(runtime_lock, context / "nuplane.lock.json")
            (context / "foundation-base.sha256").write_text(
                "".join(f"{digest}  /app/{name}\n" for name, digest in foundation_hashes().items()),
                encoding="utf-8",
            )
            published = []
            try:
                for entry in sorted(context.iterdir()):
                    published.append(entry.rename(output / entry.name))
            except OSError:
                for entry in published:
                    entry.rename(context / entry.name)
                raise
    finally:
        shutil.rmtree(context)
    print(f"Assembled {count} Nuplane packages at {output}")


def main():
    parser = argparse.ArgumentParser(prog="elsa-profile", description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    command = commands.add_parser("assemble", help="read packages.txt from the working directory")
    command.add_argument("--output", required=True, type=Path, help="new or empty directory for the assembled feed and locks")
    command.add_argument("--allow-add-ons", action="store_true", help="allow watched add-on packages beside the locked profile")
    command.add_argument("--appsettings-directory", type=Path, help="merge the profile's appsettings and Production overrides")
    command.add_argument("--locked", action="store_true", help="require both locks beside packages.txt and reproduce them")
    command.add_argument("--packs-directory", type=Path, default=TOOL_DIRECTORY / "packs", help="use this directory of named packs")
    command.add_argument("--nuget-config", type=Path, help="override build-time NuGet sources")
    args = parser.parse_args()
    try:
        assemble(args)
    except (OSError, ValueError, KeyError, ET.ParseError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        if isinstance(error, subprocess.CalledProcessError) and error.stderr:
            print(error.stderr.strip(), file=sys.stderr)
        print(f"Profile preparation failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
