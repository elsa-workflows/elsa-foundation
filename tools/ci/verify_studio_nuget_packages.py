#!/usr/bin/env python3
"""Verify the consolidated Studio NuGet project inventory and package artifact."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile
from collections import Counter
from pathlib import Path
from xml.etree import ElementTree


IDENTITY_FILE = Path(__file__).with_name("studio-package-identities.json")


def _local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def _child_text(parent: ElementTree.Element, name: str) -> str | None:
    for child in parent:
        if _local_name(child.tag) == name:
            return (child.text or "").strip()
    return None


def _studio_projects(root: Path) -> set[str]:
    paths = set((root / "Studio").rglob("*.csproj"))
    paths.update(path for extension in (root / "Extensions").glob("*/studio") for path in extension.rglob("*.csproj"))
    return {path.relative_to(root).as_posix() for path in paths}


def _verify_extension_studio_props(root: Path, project: Path) -> None:
    wrapper = root.joinpath(*project.parts[:3], "Directory.Build.props")
    if not wrapper.is_file():
        raise ValueError(f"Extension Studio project is missing its shared props wrapper: {wrapper.relative_to(root)}.")
    document = ElementTree.parse(wrapper).getroot()
    imports = [node for node in document.iter() if _local_name(node.tag) == "Import"]
    if len(imports) != 1:
        raise ValueError(f"{wrapper.relative_to(root)} must contain exactly one shared Studio props import.")
    expression = imports[0].attrib.get("Project", "")
    prefix = wrapper.parent.as_posix() + "/"
    resolved = Path(expression.replace("$(MSBuildThisFileDirectory)", prefix)).resolve()
    if resolved != (root / "Studio/Directory.Build.props").resolve():
        raise ValueError(f"{wrapper.relative_to(root)} does not import Studio/Directory.Build.props.")
    overrides = {"PackageId", "AssemblyName", "PackageIcon", "PackageLicenseExpression"}
    if any(_local_name(node.tag) in overrides for node in document.iter()):
        raise ValueError(f"{wrapper.relative_to(root)} overrides shared Studio package metadata.")


def _verify_pinned_layout(root: Path, inventory: dict) -> None:
    manifest_path = root / inventory["layout_manifest"]
    if not manifest_path.is_file():
        raise ValueError(f"Pinned Studio layout manifest is missing: {inventory['layout_manifest']}.")
    manifest = json.loads(manifest_path.read_text())
    if manifest.get("studio_source") != inventory["studio_source_commit"]:
        raise ValueError("Studio package identity list is not pinned to the layout manifest source commit.")
    actual = {
        item["target"]: (item["package_id"], item["explicit_is_packable"])
        for item in manifest["projects"]
        if item.get("product") == "studio"
    }
    # The consolidation manifest is historical. New modules belong in the current
    # inventory without inventing source paths in that frozen snapshot.
    additions = {
        item["project"] for item in inventory["packable_projects"] + inventory["non_packable_projects"]
        if item.get("introduced_after_consolidation", False)
    }
    if additions & set(actual):
        raise ValueError("A consolidated Studio project cannot be declared a later addition.")
    expected = {
        item["project"]: (item["id"], None)
        for item in inventory["packable_projects"] if item["project"] not in additions
    }
    expected.update({item["project"]: (item["id"], "false")
                     for item in inventory["non_packable_projects"] if item["project"] not in additions})
    if actual != expected:
        missing = sorted(set(expected) - set(actual))
        unexpected = sorted(set(actual) - set(expected))
        mismatched = sorted(path for path in set(actual) & set(expected) if actual[path] != expected[path])
        raise ValueError(f"Pinned Studio project identity mismatch; missing={missing}, unexpected={unexpected}, mismatched={mismatched}.")


def verify_project_inventory(root: Path) -> dict:
    root = root.resolve()
    inventory = json.loads(IDENTITY_FILE.read_text())
    _verify_pinned_layout(root, inventory)
    packable = inventory["packable_projects"]
    non_packable = inventory["non_packable_projects"]
    expected_paths = {item["project"] for item in packable + non_packable}
    actual_paths = _studio_projects(root)
    if actual_paths != expected_paths:
        missing = sorted(expected_paths - actual_paths)
        unexpected = sorted(actual_paths - expected_paths)
        raise ValueError(f"Studio project inventory mismatch; missing={missing}, unexpected={unexpected}.")

    ids = [item["id"] for item in packable]
    if not ids or len(ids) != len(set(ids)):
        raise ValueError("The declared Studio packable package IDs must be nonempty and unique.")

    for item in packable + non_packable:
        project_path = root / item["project"]
        project = ElementTree.parse(project_path).getroot()
        properties = {
            _local_name(node.tag): (node.text or "").strip()
            for node in project.iter()
            if _local_name(node.tag) in {"PackageId", "AssemblyName", "IsPackable"}
        }
        if "PackageId" in properties or "AssemblyName" in properties:
            raise ValueError(f"{item['project']} overrides the declared SDK package identity defaults.")
        if project_path.stem != item["id"]:
            raise ValueError(f"{item['project']} does not use the declared SDK-default package ID {item['id']}.")
        if item in packable and properties.get("IsPackable", "true").lower() == "false":
            raise ValueError(f"Packable Studio project {item['project']} sets IsPackable=false.")
        if item in non_packable and properties.get("IsPackable", "").lower() != "false":
            raise ValueError(f"Non-packable Studio project {item['project']} must explicitly set IsPackable=false.")

    studio_props = ElementTree.parse(root / "Studio/Directory.Build.props").getroot()
    studio_properties = {
        _local_name(node.tag): (node.text or "").strip()
        for node in studio_props.iter()
        if _local_name(node.tag) in {"PackageId", "AssemblyName", "PackageIcon", "PackageLicenseExpression"}
    }
    if "PackageId" in studio_properties or "AssemblyName" in studio_properties:
        raise ValueError("Studio/Directory.Build.props overrides SDK-default package identity values.")
    if studio_properties.get("PackageLicenseExpression") != inventory["package_license_expression"]:
        raise ValueError("Studio package license metadata does not match the package identity manifest.")
    if studio_properties.get("PackageIcon") != inventory["package_icon"]:
        raise ValueError("Studio package icon metadata does not match the package identity manifest.")
    none_items = [node for node in studio_props.iter() if _local_name(node.tag) == "None"]
    if not any(
        node.attrib.get("Include") == "$(MSBuildThisFileDirectory)icon.png"
        and node.attrib.get("Pack") == "true"
        and node.attrib.get("PackagePath") == ""
        for node in none_items
    ):
        raise ValueError("Studio/Directory.Build.props must pack its shared icon.png at the package root.")
    for item in packable:
        if item["project"].startswith("Extensions/"):
            _verify_extension_studio_props(root, Path(item["project"]))
    icon = root / "Studio" / inventory["package_icon"]
    if not icon.is_file():
        raise ValueError(f"Studio package icon source is missing: Studio/{inventory['package_icon']}.")
    return inventory


def _read_package(path: Path, expected_license: str, expected_icon: str) -> tuple[str, str]:
    try:
        with zipfile.ZipFile(path) as archive:
            if len(archive.namelist()) != len(set(archive.namelist())):
                raise ValueError("Archive has duplicate entry names.")
            nuspecs = [name for name in archive.namelist() if name.lower().endswith(".nuspec")]
            if len(nuspecs) != 1:
                raise ValueError(f"Expected one nuspec, found {len(nuspecs)}.")
            package = ElementTree.fromstring(archive.read(nuspecs[0]))
            metadata = next((node for node in package if _local_name(node.tag) == "metadata"), None)
            if metadata is None:
                raise ValueError("Nuspec has no metadata element.")
            package_id = _child_text(metadata, "id")
            version = _child_text(metadata, "version")
            license_node = next((node for node in metadata if _local_name(node.tag) == "license"), None)
            license_expression = (license_node.text or "").strip() if license_node is not None else None
            license_type = license_node.attrib.get("type") if license_node is not None else None
            icon = _child_text(metadata, "icon")
            if not package_id or not version:
                raise ValueError("Nuspec package ID or version is empty.")
            if license_expression != expected_license or license_type != "expression":
                raise ValueError(f"Expected license expression {expected_license}.")
            if icon != expected_icon or expected_icon not in archive.namelist():
                raise ValueError(f"Expected packaged icon at archive root: {expected_icon}.")
            return package_id, version
    except (OSError, zipfile.BadZipFile, ElementTree.ParseError) as error:
        raise ValueError(f"Invalid NuGet package {path.name}: {error}.") from error


def _hash(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _dependency_contract(root: Path, item: dict, version: str) -> tuple[str, dict[str, str], set[str]]:
    """Read the declared public closure of inventory-selected hosting boundaries."""
    project_path = root / item["project"]
    project = ElementTree.parse(project_path).getroot()
    if any(_local_name(node.tag) == "SuppressDependenciesWhenPacking" and (node.text or "").strip().lower() == "true"
           for node in project.iter()):
        raise ValueError(f"Required dependency metadata is suppressed by {item['project']}.")
    target = next((node.text for node in project.iter() if _local_name(node.tag) == "TargetFramework"), None)
    if not target or "$" in target:
        raise ValueError(f"Expected one literal TargetFramework for {item['id']}.")
    pins = {node.attrib["Include"]: node.attrib["Version"]
            for node in ElementTree.parse(root / "Studio/Directory.Packages.props").getroot().iter()
            if _local_name(node.tag) == "PackageVersion" and "Include" in node.attrib and "Version" in node.attrib}
    dependencies, frameworks = {}, set()
    for group in project:
        if _local_name(group.tag) != "ItemGroup":
            continue
        for node in group:
            kind = _local_name(node.tag)
            if kind not in {"PackageReference", "ProjectReference", "FrameworkReference"}:
                continue
            private = node.attrib.get("PrivateAssets", _child_text(node, "PrivateAssets") or "")
            if "all" in private.lower().split(";") or node.attrib.get("ReferenceOutputAssembly", "true").lower() == "false":
                continue
            if group.attrib.get("Condition") or node.attrib.get("Condition"):
                raise ValueError(f"Conditional public dependency needs an explicit qualification contract: {item['id']}.")
            name = node.attrib.get("Include", "")
            if not name or "$" in name:
                raise ValueError(f"Unresolved public dependency in {item['id']}.")
            if kind == "FrameworkReference":
                frameworks.add(name)
            elif kind == "ProjectReference":
                dependencies[Path(name.replace("\\", "/")).stem] = version
            else:
                pin = node.attrib.get("VersionOverride", node.attrib.get("Version", pins.get(name)))
                if not pin or "$" in pin:
                    raise ValueError(f"Unresolved package pin for {item['id']} -> {name}.")
                dependencies[name] = pin
    return target.strip(), dependencies, frameworks


def _asset_contract(root: Path, inventory: dict) -> dict[str, dict[str, Path]]:
    # This producer list is the existing authoritative minimum. Everything generated
    # beneath each public wwwroot is checked too, including CSS and lazy chunks.
    path = Path(__file__).with_name("verify-studio-assets.py")
    spec = importlib.util.spec_from_file_location("studio_asset_contract", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    roots = {item["id"]: Path(item["project"]).parent / "wwwroot" for item in inventory["packable_projects"]}
    for required in module.EXPECTED_ASSETS:
        owners = [name for name, folder in roots.items() if Path(required).is_relative_to(folder)]
        if len(owners) != 1 or not (root / required).is_file():
            raise ValueError(f"Missing or unowned generated Studio asset: {required}.")
    return {name: {"staticwebassets/" + path.relative_to(root / folder).as_posix(): path
                   for path in (root / folder).rglob("*") if path.is_file() and ".vite" not in path.relative_to(root / folder).parts}
            for name, folder in roots.items()}


def _verify_payload(path: Path, assets: dict[str, Path], contract=None) -> dict:
    with zipfile.ZipFile(path) as archive:
        document = ElementTree.fromstring(archive.read(next(n for n in archive.namelist() if n.lower().endswith(".nuspec"))))
        dependency_rows = [dict(node.attrib) for node in document.iter() if _local_name(node.tag) == "dependency"]
        if contract is not None:
            target, expected, frameworks = contract
            dependencies = next((node for node in document.iter() if _local_name(node.tag) == "dependencies"), None)
            groups = [] if dependencies is None else list(dependencies)
            if len(groups) != 1 or _local_name(groups[0].tag) != "group" or groups[0].attrib.get("targetFramework") != target:
                raise ValueError(f"Required dependency group {target} missing or ambiguous in {path.name}.")
            rows = [node for node in groups[0] if _local_name(node.tag) == "dependency"]
            actual = {node.attrib.get("id", "").lower(): node for node in rows}
            if len(actual) != len(rows) or set(actual) != {name.lower() for name in expected}:
                raise ValueError(f"Required dependency set mismatch in {path.name}; expected={sorted(expected)}, actual={sorted(actual)}.")
            for name, version in expected.items():
                node = actual[name.lower()]
                # A literal project pin packs as an inclusive, unbounded minimum.
                # Accept its two NuGet spellings only; removing delimiters would
                # also accept exclusive or impossible ranges that exclude the pin.
                packed_version = "".join(node.attrib.get("version", "").split())
                expected_ranges = {version, f"[{version},)"}
                excluded = {part.strip().lower() for part in node.attrib.get("exclude", "").split(",")}
                included = {part.strip().lower() for part in node.attrib.get("include", "all").split(",")}
                if packed_version not in expected_ranges or excluded & {"all", "compile", "runtime"} or not ("all" in included or {"compile", "runtime"} <= included):
                    raise ValueError(f"Required dependency version/assets mismatch in {path.name}: {name}, expected {version}, actual={node.attrib}.")
            framework_root = next((node for node in document.iter() if _local_name(node.tag) == "frameworkReferences"), None)
            framework_groups = [] if framework_root is None else list(framework_root)
            actual_frameworks = {node.attrib.get("name") for group in framework_groups if group.attrib.get("targetFramework") == target
                                 for node in group if _local_name(node.tag) == "frameworkReference"}
            if actual_frameworks != frameworks:
                raise ValueError(f"Required framework dependency mismatch in {path.name}: expected={sorted(frameworks)}, actual={sorted(actual_frameworks)}.")
        actual_assets = {name for name in archive.namelist() if name.startswith("staticwebassets/") and not name.endswith("/")}
        if actual_assets != set(assets):
            raise ValueError(f"Static asset set mismatch in {path.name}; missing={sorted(set(assets) - actual_assets)}, unexpected={sorted(actual_assets - set(assets))}.")
        hashes = {}
        for name, source in assets.items():
            body = archive.read(name)
            if not body or body != source.read_bytes():
                raise ValueError(f"Empty or mismatched static asset bytes in {path.name}: {name}.")
            hashes[name] = _hash(body)
        repository = next((dict(node.attrib) for node in document.iter() if _local_name(node.tag) == "repository"), {})
        return {"archiveSha256": _hash(path.read_bytes()), "repository": repository, "dependencies": dependency_rows,
                "dependencyMetadataRequired": contract is not None, "staticAssetHashes": hashes}


def verify_packages(root: Path, package_dir: Path, expected_version: str | None = None, *, verify_payload: bool = True,
                    report: dict | None = None) -> list[str]:
    inventory = verify_project_inventory(root)
    package_dir = package_dir.resolve()
    if not package_dir.is_dir():
        raise ValueError(f"NuGet package directory is missing: {package_dir}.")
    packages = sorted(package_dir.rglob("*.nupkg"))
    if not packages:
        raise ValueError("Studio package artifact contains no NuGet packages.")

    entries = [
        (path, *_read_package(path, inventory["package_license_expression"], inventory["package_icon"]))
        for path in packages
    ]
    ids = [package_id for _, package_id, _ in entries]
    duplicate_ids = sorted(package_id for package_id, count in Counter(ids).items() if count > 1)
    if duplicate_ids:
        raise ValueError(f"Duplicate Studio package IDs in artifact: {duplicate_ids}.")
    expected_ids = {item["id"] for item in inventory["packable_projects"]}
    actual_ids = set(ids)
    if actual_ids != expected_ids:
        missing = sorted(expected_ids - actual_ids)
        unexpected = sorted(actual_ids - expected_ids)
        raise ValueError(f"Studio NuGet package ID set mismatch; missing={missing}, unexpected={unexpected}.")

    versions = {version for _, _, version in entries}
    if expected_version is not None:
        if versions != {expected_version}:
            raise ValueError(f"Studio package versions {sorted(versions)} do not match provenance version {expected_version}.")
    elif len(versions) != 1:
        raise ValueError(f"Studio packages have multiple versions: {sorted(versions)}.")
    if verify_payload:
        assets = _asset_contract(root, inventory)
        items = {item["id"]: item for item in inventory["packable_projects"]}
        observations = {}
        for path, name, version in entries:
            contract = _dependency_contract(root, items[name], version) if items[name].get("verify_dependency_metadata", False) else None
            observations[name] = dict(version=version, **_verify_payload(path, assets[name], contract))
        if report is not None:
            report.update(packages=observations, inventorySha256=_hash(IDENTITY_FILE.read_bytes()),
                          assetContractSha256=_hash(Path(__file__).with_name("verify-studio-assets.py").read_bytes()))
    return sorted(actual_ids)


def _write_test_package(path: Path, package_id: str, version: str, icon: str) -> None:
    nuspec = f"""<?xml version="1.0"?>
<package><metadata><id>{package_id}</id><version>{version}</version><license type="expression">MIT</license><icon>{icon}</icon></metadata></package>"""
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr(f"{package_id}.nuspec", nuspec)
        archive.writestr(icon, b"test icon")


def _payload_self_test() -> None:
    """Small guard fixtures; genuine product mutations are a separate CI proof."""
    with tempfile.TemporaryDirectory() as temp:
        folder = Path(temp)
        source = folder / "module.js"
        source.write_bytes(b"export const qualification = true;\n")
        resource = "staticwebassets/studio/module.js"
        contract = ("net10.0", {"Elsa.Studio.Core": "4.0.0-preview.1"}, {"Microsoft.AspNetCore.App"})
        package = folder / "guard-fixture.nupkg"

        def write(fault=None):
            document = ElementTree.fromstring('''<package><metadata><id>GuardFixture</id><version>4.0.0-preview.1</version>
<dependencies><group targetFramework="net10.0"><dependency id="Elsa.Studio.Core" version="4.0.0-preview.1" exclude="Build,Analyzers"/></group></dependencies>
<frameworkReferences><group targetFramework="net10.0"><frameworkReference name="Microsoft.AspNetCore.App"/></group></frameworkReferences>
</metadata></package>''')
            metadata = document.find("metadata")
            group = metadata.find("dependencies/group")
            dependency = group.find("dependency")
            payload = {resource: source.read_bytes()}
            if fault == "dependency": group.remove(dependency)
            elif fault == "version": dependency.set("version", "0.0.0")
            elif fault == "exclusive-version": dependency.set("version", "(4.0.0-preview.1,)")
            elif fault == "empty-version-range": dependency.set("version", "(4.0.0-preview.1,4.0.0-preview.1)")
            elif fault == "runtime": dependency.set("exclude", "Runtime")
            elif fault == "duplicate": group.append(ElementTree.fromstring(ElementTree.tostring(dependency)))
            elif fault == "target": group.set("targetFramework", "net9.0")
            elif fault == "framework": metadata.remove(metadata.find("frameworkReferences"))
            elif fault == "missing": payload.pop(resource)
            elif fault == "empty": payload[resource] = b""
            elif fault == "changed": payload[resource] = b"export const qualification = false;\n"
            elif fault == "unexpected": payload["staticwebassets/extra.js"] = b"extra"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("GuardFixture.nuspec", ElementTree.tostring(document))
                for name, body in payload.items(): archive.writestr(name, body)

        for fault, diagnostic in [("dependency", "Required dependency set"), ("version", "Required dependency version/assets"),
                                  ("exclusive-version", "Required dependency version/assets"), ("empty-version-range", "Required dependency version/assets"),
                                  ("runtime", "Required dependency version/assets"), ("duplicate", "Required dependency set"),
                                  ("target", "Required dependency group"), ("framework", "Required framework dependency"),
                                  ("missing", "Static asset set"), ("empty", "Empty or mismatched static asset"),
                                  ("changed", "Empty or mismatched static asset"), ("unexpected", "Static asset set")]:
            write()
            _verify_payload(package, {resource: source}, contract)
            write(fault)
            try:
                _verify_payload(package, {resource: source}, contract)
            except ValueError as error:
                if not str(error).startswith(diagnostic):
                    raise AssertionError(f"{fault} failed for the wrong reason: {error}") from error
            else:
                raise AssertionError(f"{fault} was accepted by the payload guard.")
            write()
            _verify_payload(package, {resource: source}, contract)


def self_test(root: Path) -> None:
    inventory = json.loads(IDENTITY_FILE.read_text())
    ids = [item["id"] for item in inventory["packable_projects"]]
    for index, item in enumerate(inventory["packable_projects"]):
        changed = json.loads(json.dumps(inventory))
        changed["packable_projects"][index]["introduced_after_consolidation"] = not item.get("introduced_after_consolidation", False)
        try:
            _verify_pinned_layout(root, changed)
        except ValueError:
            pass
        else:
            raise AssertionError(f"Incorrect consolidation provenance accepted for {item['id']}.")
    with tempfile.TemporaryDirectory() as temp:
        package_dir = Path(temp)
        for package_id in ids:
            _write_test_package(package_dir / f"{package_id}.nupkg", package_id, "4.0.0-preview.1", inventory["package_icon"])
        if verify_packages(root, package_dir, "4.0.0-preview.1", verify_payload=False) != sorted(ids):
            raise AssertionError("Complete package fixture was not accepted.")
        (package_dir / f"{ids[-1]}.nupkg").unlink()
        try:
            verify_packages(root, package_dir, "4.0.0-preview.1", verify_payload=False)
        except ValueError as error:
            if "missing=" not in str(error):
                raise AssertionError("Missing-package control failed for the wrong reason.") from error
        else:
            raise AssertionError("An artifact missing a Studio package was accepted.")
        extra = package_dir / "Elsa.Studio.Extra.nupkg"
        _write_test_package(extra, "Elsa.Studio.Extra", "4.0.0-preview.1", inventory["package_icon"])
        _write_test_package(package_dir / f"{ids[-1]}.nupkg", ids[-1], "4.0.0-preview.1", inventory["package_icon"])
        try:
            verify_packages(root, package_dir, "4.0.0-preview.1", verify_payload=False)
        except ValueError as error:
            if "unexpected=" not in str(error):
                raise AssertionError("Extra-package control failed for the wrong reason.") from error
        else:
            raise AssertionError("An artifact with an extra Studio package was accepted.")
        extra.unlink()

        duplicate = package_dir / "duplicate-id.nupkg"
        _write_test_package(duplicate, ids[0], "4.0.0-preview.1", inventory["package_icon"])
        try:
            verify_packages(root, package_dir, "4.0.0-preview.1", verify_payload=False)
        except ValueError as error:
            if "Duplicate Studio package IDs" not in str(error):
                raise AssertionError("Duplicate-ID control failed for the wrong reason.") from error
        else:
            raise AssertionError("An artifact with a duplicate Studio package ID was accepted.")
        duplicate.unlink()

        _write_test_package(package_dir / f"{ids[0]}.nupkg", ids[0], "4.0.0-preview.2", inventory["package_icon"])
        try:
            verify_packages(root, package_dir, "4.0.0-preview.1", verify_payload=False)
        except ValueError as error:
            if "do not match provenance version" not in str(error):
                raise AssertionError("Mismatched-version control failed for the wrong reason.") from error
        else:
            raise AssertionError("An artifact with a mismatched package version was accepted.")
    _payload_self_test()


def qualify_archive_controls(root: Path, package_dir: Path, output: Path, version: str | None) -> dict:
    """Exercise the public verifier against private mutations of genuine candidates."""
    output.mkdir(parents=True, exist_ok=False)
    original = output / "originals"
    original.mkdir()
    # Reject an incomplete/extra input set before selecting anything for controls.
    verify_packages(root, package_dir, version)
    inventory = verify_project_inventory(root)
    for path in package_dir.rglob("*.nupkg"):
        shutil.copy2(path, original / path.name)
    hashes = {path.name: _hash(path.read_bytes()) for path in original.glob("*.nupkg")}
    identities = {name: (path, value) for path in original.glob("*.nupkg")
                  for name, value in [_read_package(path, inventory["package_license_expression"], inventory["package_icon"]) ]}
    version = identities["Elsa.Studio.Api"][1]
    report = {"status": "running", "stage": "archive-controls", "root": str(output.resolve()),
              "toolSha256": _hash(Path(__file__).read_bytes()), "originalArchiveHashes": hashes, "observations": {}}

    def unchanged():
        if any(_hash((original / name).read_bytes()) != value for name, value in hashes.items()):
            raise ValueError("Original candidate archive changed during a control.")

    def run(label, feed, expected_error=None):
        child = output / (label + ".json")
        command = [sys.executable, str(Path(__file__).resolve()), "--root", str(root.resolve()), "--packages", str(feed.resolve()),
                   "--version", version, "--report", str(child.resolve())]
        result = subprocess.run(command, capture_output=True, text=True, timeout=120)
        (output / (label + ".log")).write_text(result.stdout + result.stderr)
        data = json.loads(child.read_text()) if child.exists() else {}
        valid_tool = data.get("toolSha256") == report["toolSha256"]
        if expected_error:
            accepted = result.returncode == 1 and data.get("status") == "failed" and data.get("stage") == "archive" and valid_tool
            accepted = accepted and all(part in data.get("error", "") for part in expected_error)
        else:
            accepted = result.returncode == 0 and data.get("status") == "passed" and data.get("stage") == "complete" and valid_tool
        row = {"exitCode": result.returncode, "report": str(child), "reportSha256": _hash(child.read_bytes()) if child.exists() else None,
               "status": data.get("status"), "expectedFailureObserved": bool(expected_error and accepted)}
        report["observations"][label] = row
        (output / "report.json").write_text(json.dumps(report, indent=2) + "\n")
        unchanged()
        if not accepted:
            raise ValueError(f"Archive control {label} did not produce its intended observation; see {output / (label + '.log')}.")

    def mutation_feed(label, package, mutate):
        feed = output / label
        feed.mkdir()
        for path in original.glob("*.nupkg"):
            try:
                os.link(path, feed / path.name)
            except OSError:
                shutil.copy2(path, feed / path.name)
        path = identities[package][0]
        with zipfile.ZipFile(path) as archive:
            if any(name.lower().endswith(".signature.p7s") for name in archive.namelist()):
                raise ValueError("Signed candidates cannot be privately mutated for archive controls.")
            payload = {name: archive.read(name) for name in archive.namelist()}
        before = dict(payload)
        mutate(payload)
        changed = sorted(name for name in set(before) | set(payload) if before.get(name) != payload.get(name))
        if len(changed) != 1:
            raise ValueError(f"Archive control {label} must alter precisely one entry, observed={changed}.")
        # Always replace a new file; never open a hard-linked original for writing.
        temporary = feed / "mutated.zip"
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, body in payload.items(): archive.writestr(name, body)
        temporary.replace(feed / path.name)
        report.setdefault("mutations", {})[label] = {"package": package, "entry": changed[0], "originalSha256": hashes[path.name],
                                                     "mutatedSha256": _hash((feed / path.name).read_bytes())}
        return feed, path.name

    def omit_dependency(payload, dependency):
        name = next(name for name in payload if name.lower().endswith(".nuspec"))
        document = ElementTree.fromstring(payload[name])
        removed = 0
        for node in document.iter():
            for child in list(node):
                if _local_name(child.tag) == "dependency" and child.attrib.get("id") == dependency:
                    node.remove(child)
                    removed += 1
        if removed != 1:
            raise ValueError(f"Expected exactly one original dependency {dependency}, observed={removed}.")
        payload[name] = ElementTree.tostring(document)

    try:
        run("positive", original)
        def change_dependency_range(payload, empty):
            name = next(name for name in payload if name.lower().endswith(".nuspec"))
            document = ElementTree.fromstring(payload[name])
            nodes = [node for node in document.iter() if _local_name(node.tag) == "dependency" and node.attrib.get("id") == "CShells.AspNetCore"]
            if len(nodes) != 1:
                raise ValueError("Expected exactly one CShells.AspNetCore dependency.")
            pin = nodes[0].attrib["version"].lstrip("[").split(",", 1)[0]
            nodes[0].set("version", f"({pin},{pin})" if empty else f"({pin},)")
            payload[name] = ElementTree.tostring(document)
        for label, empty in [("dependency-exclusive-minimum", False), ("dependency-empty-range", True)]:
            feed, name = mutation_feed(label, "Elsa.Studio.Hosting", lambda payload: change_dependency_range(payload, empty))
            run(label, feed, ["Required dependency version/assets mismatch", name, "CShells.AspNetCore"])
        for label, package, dependency in [("api-core-omitted", "Elsa.Studio.Api", "Elsa.Studio.Core"),
                                           ("console-runtime-omitted", "Elsa.Studio.ConsoleStream", "ConsoleLogStreaming.AspNetCore")]:
            feed, name = mutation_feed(label, package, lambda payload: omit_dependency(payload, dependency))
            run(label, feed, ["Required dependency set mismatch", name, dependency])
        def omit_framework(payload):
            name = next(name for name in payload if name.lower().endswith(".nuspec"))
            document = ElementTree.fromstring(payload[name])
            for node in document.iter():
                for child in list(node):
                    if _local_name(child.tag) == "frameworkReferences": node.remove(child)
            payload[name] = ElementTree.tostring(document)
        feed, name = mutation_feed("framework-omitted", "Elsa.Studio.Hosting", omit_framework)
        run("framework-omitted", feed, ["Required framework dependency mismatch", name, "Microsoft.AspNetCore.App"])
        with zipfile.ZipFile(identities["Elsa.Studio.Hosting"][0]) as archive:
            lazy_chunks = sorted(name for name in archive.namelist()
                                 if name.startswith("staticwebassets/studio/assets/") and name.endswith(".js") and not name.endswith("/index.js"))
        if not lazy_chunks:
            raise ValueError("Actual Hosting candidate has no lazy browser chunk to qualify.")
        resource_cases = [
            ("vendor-missing", "Elsa.Studio.Hosting", "staticwebassets/studio/vendor/react.js", "missing"),
            ("module-css-missing", "Elsa.Studio.Dashboard", "staticwebassets/studio/modules/dashboard/module.css", "missing"),
            ("lazy-chunk-missing", "Elsa.Studio.Hosting", lazy_chunks[0], "missing"),
            ("entry-empty", "Elsa.Studio.Hosting", "staticwebassets/studio/assets/index.js", "empty"),
            ("sdk-changed", "Elsa.Studio.Hosting", "staticwebassets/studio/sdk/index.js", "changed"),
        ]
        for label, package, entry, fault in resource_cases:
            def alter(payload):
                if entry not in payload or not payload[entry]:
                    raise ValueError("Control requires an original nonempty resource: " + entry)
                if fault == "missing": payload.pop(entry)
                elif fault == "empty": payload[entry] = b""
                else: payload[entry] += b"\n// controlled archive corruption\n"
            feed, name = mutation_feed(label, package, alter)
            diagnostic = "Static asset set mismatch" if fault == "missing" else "Empty or mismatched static asset bytes"
            run(label, feed, [diagnostic, name, entry])
        run("restored", original)
        report.update(status="passed", stage="complete")
        return report
    except Exception as error:
        report.update(status="failed", error=str(error))
        raise
    finally:
        (output / "report.json").write_text(json.dumps(report, indent=2) + "\n")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="consolidated repository root")
    parser.add_argument("--packages", type=Path, required=False, help="NuGet package output/artifact directory")
    parser.add_argument("--version", help="required package version from build provenance")
    parser.add_argument("--self-test", action="store_true", help="exercise identity, dependency, framework and asset guards")
    parser.add_argument("--report", type=Path, help="retain complete archive observations or the precise failure")
    parser.add_argument("--controls-output", type=Path, help="new owned directory for genuine positive/fault/restored archive controls")
    args = parser.parse_args()
    if args.self_test:
        verify_project_inventory(args.root)
        self_test(args.root)
        print("Studio NuGet verifier self-test passed.")
        return
    if args.packages is None:
        parser.error("--packages is required unless --self-test is used")
    if args.controls_output:
        try:
            report = qualify_archive_controls(args.root, args.packages, args.controls_output, args.version)
        except (ValueError, OSError, subprocess.SubprocessError) as error:
            print("Studio archive controls failed: " + str(error), file=sys.stderr)
            raise SystemExit(1)
        print(json.dumps({key: report[key] for key in ["root", "status", "stage"]}))
        return
    report = {"status": "running", "stage": "archive", "toolSha256": _hash(Path(__file__).read_bytes())}
    try:
        ids = verify_packages(args.root, args.packages, args.version, report=report)
        report.update(status="passed", stage="complete")
        print(f"Verified the exact {len(ids)} Studio NuGet package IDs, version set, MIT license, package icon, dependency metadata and static payload bytes.")
    except (ValueError, OSError) as error:
        report.update(status="failed", error=str(error))
        print("Studio NuGet verification failed: " + str(error), file=sys.stderr)
        raise SystemExit(1)
    finally:
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps(report, indent=2) + "\n")


if __name__ == "__main__":
    main()
