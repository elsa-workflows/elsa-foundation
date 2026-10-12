#!/usr/bin/env python3
"""Verify the pinned consolidation inventory and resolved project references.

Uses the two original repository inventories rather than discovery selectors from
maps or architecture guards. Run on a candidate checkout, including clean CI.
"""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET


def verify(root: Path, manifest: dict) -> tuple[list[str], dict]:
    errors: list[str] = []
    tracked = subprocess.check_output(["git", "-C", str(root), "ls-files", "-z"], text=True).split("\0")
    actual = {p for p in tracked if p.endswith(".csproj")}
    expected = {p["target"] for p in manifest["projects"]}
    errors.extend(f"Missing project: {p}" for p in sorted(expected - actual))
    errors.extend(f"Unaccounted project: {p}" for p in sorted(actual - expected))
    refs = 0
    for entry in manifest["projects"]:
        path = root / entry["target"]
        if not path.is_file():
            if entry["target"] in actual:
                errors.append(f"Tracked project absent on disk: {entry['target']}")
            continue
        project = ET.parse(path).getroot()
        def prop(name: str) -> str | None:
            return next((node.text for node in project.iter(name) if node.text), None)
        for name, key in (("PackageId", "package_id"), ("AssemblyName", "assembly_name")):
            identity = prop(name) or path.stem
            if identity != entry[key]:
                errors.append(f"Changed {name}: {entry['target']}: {entry[key]} -> {identity}")
        if prop("IsPackable") != entry["explicit_is_packable"]:
            errors.append(f"Changed explicit IsPackable: {entry['target']}")
        for ref in project.iter("ProjectReference"):
            value = ref.get("Include", "").replace("\\", "/")
            if "$" in value or "*" in value:
                errors.append(f"Reference needs explicit evaluation: {entry['target']}: {value}")
                continue
            refs += 1
            if not (path.parent / value).is_file():
                errors.append(f"Missing reference: {entry['target']}: {value}")
    old_roots = [p for p in actual if p.startswith(("src/", "tests/"))]
    errors.extend(f"Old root still contains project: {p}" for p in old_roots)
    return errors, {"projects": len(expected), "resolved_project_references": refs,
                    "foundation_projects": sum(p["product"] == "foundation" for p in manifest["projects"]),
                    "studio_projects": sum(p["product"] == "studio" for p in manifest["projects"])}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--self-test", action="store_true", help="Prove missing/invented baseline entries fail")
    args = parser.parse_args()
    manifest = json.loads((Path(__file__).with_name("layout-manifest.json")).read_text())
    errors, result = verify(args.root.resolve(), manifest)
    if args.self_test:
        # Independent failing controls mutate only the in-memory baseline, never source files.
        omitted = {**manifest, "projects": manifest["projects"][1:]}
        missing = {**manifest, "projects": [*manifest["projects"],
                   {**manifest["projects"][0], "target": "Essentials/src/Missing/Missing.csproj"}]}
        assert any(e.startswith("Unaccounted project:") for e in verify(args.root, omitted)[0])
        assert any(e.startswith("Missing project:") for e in verify(args.root, missing)[0])
        result["failing_controls"] = 2
    result["errors"] = errors
    print(json.dumps(result, indent=2))
    return bool(errors)


if __name__ == "__main__":
    sys.exit(main())
