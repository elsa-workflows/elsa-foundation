#!/usr/bin/env python3
"""Verify the consolidated Studio NuGet project inventory and package artifact."""

from __future__ import annotations

import argparse
import json
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
    expected = {
        item["project"]: (item["id"], None)
        for item in inventory["packable_projects"]
    }
    expected.update({item["project"]: (item["id"], "false") for item in inventory["non_packable_projects"]})
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
    if len(ids) != len(set(ids)) or len(ids) != 17:
        raise ValueError("The declared Studio packable package IDs must contain exactly 17 unique IDs.")

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


def verify_packages(root: Path, package_dir: Path, expected_version: str | None = None) -> list[str]:
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
    return sorted(actual_ids)


def _write_test_package(path: Path, package_id: str, version: str, icon: str) -> None:
    nuspec = f"""<?xml version="1.0"?>
<package><metadata><id>{package_id}</id><version>{version}</version><license type="expression">MIT</license><icon>{icon}</icon></metadata></package>"""
    with zipfile.ZipFile(path, "w") as archive:
        archive.writestr(f"{package_id}.nuspec", nuspec)
        archive.writestr(icon, b"test icon")


def self_test(root: Path) -> None:
    inventory = json.loads(IDENTITY_FILE.read_text())
    ids = [item["id"] for item in inventory["packable_projects"]]
    with tempfile.TemporaryDirectory() as temp:
        package_dir = Path(temp)
        for package_id in ids:
            _write_test_package(package_dir / f"{package_id}.nupkg", package_id, "4.0.0-preview.1", inventory["package_icon"])
        if len(verify_packages(root, package_dir, "4.0.0-preview.1")) != 17:
            raise AssertionError("Complete package fixture was not accepted.")
        (package_dir / f"{ids[-1]}.nupkg").unlink()
        try:
            verify_packages(root, package_dir, "4.0.0-preview.1")
        except ValueError as error:
            if "missing=" not in str(error):
                raise AssertionError("Missing-package control failed for the wrong reason.") from error
        else:
            raise AssertionError("An artifact missing a Studio package was accepted.")
        extra = package_dir / "Elsa.Studio.Extra.nupkg"
        _write_test_package(extra, "Elsa.Studio.Extra", "4.0.0-preview.1", inventory["package_icon"])
        _write_test_package(package_dir / f"{ids[-1]}.nupkg", ids[-1], "4.0.0-preview.1", inventory["package_icon"])
        try:
            verify_packages(root, package_dir, "4.0.0-preview.1")
        except ValueError as error:
            if "unexpected=" not in str(error):
                raise AssertionError("Extra-package control failed for the wrong reason.") from error
        else:
            raise AssertionError("An artifact with an extra Studio package was accepted.")
        extra.unlink()

        duplicate = package_dir / "duplicate-id.nupkg"
        _write_test_package(duplicate, ids[0], "4.0.0-preview.1", inventory["package_icon"])
        try:
            verify_packages(root, package_dir, "4.0.0-preview.1")
        except ValueError as error:
            if "Duplicate Studio package IDs" not in str(error):
                raise AssertionError("Duplicate-ID control failed for the wrong reason.") from error
        else:
            raise AssertionError("An artifact with a duplicate Studio package ID was accepted.")
        duplicate.unlink()

        _write_test_package(package_dir / f"{ids[0]}.nupkg", ids[0], "4.0.0-preview.2", inventory["package_icon"])
        try:
            verify_packages(root, package_dir, "4.0.0-preview.1")
        except ValueError as error:
            if "do not match provenance version" not in str(error):
                raise AssertionError("Mismatched-version control failed for the wrong reason.") from error
        else:
            raise AssertionError("An artifact with a mismatched package version was accepted.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path.cwd(), help="consolidated repository root")
    parser.add_argument("--packages", type=Path, required=False, help="NuGet package output/artifact directory")
    parser.add_argument("--version", help="required package version from build provenance")
    parser.add_argument("--self-test", action="store_true", help="test complete, missing, and extra package sets")
    args = parser.parse_args()
    if args.self_test:
        verify_project_inventory(args.root)
        self_test(args.root)
        print("Studio NuGet verifier self-test passed.")
        return
    if args.packages is None:
        parser.error("--packages is required unless --self-test is used")
    ids = verify_packages(args.root, args.packages, args.version)
    print(f"Verified the exact {len(ids)} Studio NuGet package IDs, version set, MIT license, and package icon.")


if __name__ == "__main__":
    main()
