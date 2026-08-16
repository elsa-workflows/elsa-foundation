#!/usr/bin/env python3
"""Generate and verify the clean-break Groundwork v1 baseline for Elsa E3."""

from __future__ import annotations

import argparse
import difflib
import io
import json
import re
import subprocess
import sys
import tarfile
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any


V1_PACKAGES = (
    "Groundwork.Core",
    "Groundwork.DiagnosticRecords",
    "Groundwork.Documents",
    "Groundwork.MongoDb",
    "Groundwork.PostgreSql",
    "Groundwork.Sqlite",
    "Groundwork.SqlServer",
)

MANIFEST_SOURCE_PATTERN = re.compile(
    r"\bclass\s+\w*(?:ManifestSource|StorageManifest|StorageSchema)\b"
)
STORAGE_MANIFEST_CONSTRUCTOR_PATTERN = re.compile(r"\bnew\s+StorageManifest\s*\(")
LOGICAL_INDEX_PATTERN = re.compile(r"\bnew\s+LogicalIndexDeclaration\b")
BOUNDED_QUERY_PATTERN = re.compile(r"\bnew\s+BoundedQueryDeclaration\b")
ACCEPT_SCAN_PATTERN = re.compile(r"AcceptScan|GwAllowAcceptedScans")


def repository_root() -> Path:
    return Path(__file__).resolve().parents[2]


def relative(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def source_files(root: Path) -> list[Path]:
    return sorted(
        path
        for path in root.joinpath("src").rglob("*.cs")
        if not any(part in {"bin", "obj"} for part in path.parts)
    )


def read_source(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def line_sites(root: Path, pattern: re.Pattern[str]) -> list[dict[str, Any]]:
    sites: list[dict[str, Any]] = []
    for path in source_files(root):
        for line_number, line in enumerate(read_source(path).splitlines(), start=1):
            if pattern.search(line):
                sites.append({"path": relative(path, root), "line": line_number})
    return sites


def manifest_inventory(root: Path) -> dict[str, Any]:
    files: list[dict[str, Any]] = []
    for path in source_files(root):
        text = read_source(path)
        if not (
            MANIFEST_SOURCE_PATTERN.search(text)
            or STORAGE_MANIFEST_CONSTRUCTOR_PATTERN.search(text)
        ):
            continue
        source_types = sorted(
            set(
                match.group(0).split()[-1]
                for match in re.finditer(
                    r"\bclass\s+\w*(?:ManifestSource|StorageManifest|StorageSchema)\b",
                    text,
                )
            )
        )
        files.append(
            {
                "path": relative(path, root),
                "line_count": len(text.splitlines()),
                "source_types": source_types,
            }
        )
    return {
        "file_count": len(files),
        "line_count": sum(item["line_count"] for item in files),
        "line_count_definition": (
            "sum of source lines in src/**/*.cs files containing a manifest-source/storage-manifest/"
            "storage-schema declaration or a StorageManifest constructor"
        ),
        "files": files,
    }


def package_inventory(root: Path) -> tuple[dict[str, str], dict[str, list[str]]]:
    versions: dict[str, str] = {}
    props_path = root / "Directory.Packages.props"
    props = ET.parse(props_path).getroot()
    for element in props.iter():
        if element.tag.rsplit("}", 1)[-1] == "PackageVersion":
            package = element.attrib.get("Include")
            if package in V1_PACKAGES:
                versions[package] = element.attrib.get("Version", "")

    consumers = {package: [] for package in V1_PACKAGES}
    for path in sorted(root.rglob("*.csproj")):
        if any(part in {".git", "bin", "obj"} for part in path.parts):
            continue
        try:
            project = ET.parse(path).getroot()
        except ET.ParseError as error:
            raise RuntimeError(f"Cannot parse {relative(path, root)}: {error}") from error
        references = {
            element.attrib.get("Include")
            for element in project.iter()
            if element.tag.rsplit("}", 1)[-1] == "PackageReference"
        }
        for package in V1_PACKAGES:
            if package in references:
                consumers[package].append(relative(path, root))
    return versions, {package: sorted(paths) for package, paths in consumers.items()}


def git_revision(root: Path) -> str:
    result = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
    )
    return result.stdout.strip()


def build_inventory(root: Path, baseline_commit: str | None = None) -> dict[str, Any]:
    versions, consumers = package_inventory(root)
    logical_sites = line_sites(root, LOGICAL_INDEX_PATTERN)
    bounded_sites = line_sites(root, BOUNDED_QUERY_PATTERN)
    accept_scan_markers = line_sites(root, ACCEPT_SCAN_PATTERN)
    manifest = manifest_inventory(root)
    return {
        "schema": "elsa-groundwork-e3-baseline/v1",
        "baseline_commit": baseline_commit or git_revision(root),
        "scope": {
            "source_roots": ["src"],
            "package_consumer_scope": (
                "repository-wide *.csproj files, excluding .git/bin/obj directories"
            ),
            "fresh_catalog_policy": True,
            "migration_policy": "clean-break; no v1-to-v2 data migration or compatibility layer",
        },
        "v1_package_versions": versions,
        "v1_package_consumers": consumers,
        "manifest": manifest,
        "logical_index_declaration_sites": {
            "count": len(logical_sites),
            "sites": logical_sites,
        },
        "bounded_query_declaration_sites": {
            "count": len(bounded_sites),
            "historical_issue_baseline": 28,
            "sites": bounded_sites,
        },
        "accept_scan_markers": {
            "count": len(accept_scan_markers),
            "sites": accept_scan_markers,
        },
    }


def serialized(value: dict[str, Any]) -> str:
    return json.dumps(value, indent=2, sort_keys=False) + "\n"


def compare_inventory(root: Path, artifact: Path, expected: dict[str, Any]) -> int:
    actual = build_inventory(root, baseline_commit=expected.get("baseline_commit"))
    expected_text = serialized(expected)
    actual_text = serialized(actual)
    if expected_text != actual_text:
        diff = difflib.unified_diff(
            expected_text.splitlines(),
            actual_text.splitlines(),
            fromfile=str(artifact),
            tofile="selected source inventory",
            lineterm="",
        )
        print("E3 baseline artifact is stale:", file=sys.stderr)
        print("\n".join(diff), file=sys.stderr)
        return 1
    print(
        "E3 baseline verified: "
        f"{len(actual['v1_package_consumers'])} v1 packages, "
        f"{actual['manifest']['file_count']} manifest files / "
        f"{actual['manifest']['line_count']} lines, "
        f"{actual['logical_index_declaration_sites']['count']} logical-index sites, "
        f"{actual['bounded_query_declaration_sites']['count']} bounded-query sites, "
        f"{actual['accept_scan_markers']['count']} AcceptScan markers"
    )
    return 0


def check_current(root: Path, artifact: Path) -> int:
    if not artifact.is_file():
        print(f"E3 baseline artifact is missing: {artifact}", file=sys.stderr)
        return 2
    expected = json.loads(artifact.read_text(encoding="utf-8"))
    return compare_inventory(root, artifact, expected)


def check_frozen_ref(root: Path, artifact: Path) -> int:
    if not artifact.is_file():
        print(f"E3 baseline artifact is missing: {artifact}", file=sys.stderr)
        return 2
    expected = json.loads(artifact.read_text(encoding="utf-8"))
    revision = expected.get("baseline_commit")
    if not isinstance(revision, str) or not re.fullmatch(r"[0-9a-f]{40}", revision):
        print("E3 baseline artifact has no full recorded baseline_commit.", file=sys.stderr)
        return 2

    try:
        archive = subprocess.run(
            ["git", "archive", "--format=tar", revision],
            cwd=root,
            check=True,
            capture_output=True,
        ).stdout
    except subprocess.CalledProcessError as error:
        print(
            f"Cannot read recorded baseline commit {revision}: {error.stderr.decode('utf-8', errors='replace').strip()}",
            file=sys.stderr,
        )
        return 2

    with tempfile.TemporaryDirectory(prefix="elsa-e3-frozen-") as directory:
        snapshot = Path(directory)
        with tarfile.open(fileobj=io.BytesIO(archive), mode="r:") as contents:
            contents.extractall(snapshot, filter="data")
        return compare_inventory(snapshot, artifact, expected)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifact", type=Path, required=True)
    parser.add_argument("--check-current", action="store_true", help="fail if the artifact differs from the current source tree")
    parser.add_argument("--check-frozen-ref", action="store_true", help="verify the artifact against its recorded Git commit")
    parser.add_argument("--write", action="store_true", help="write the generated artifact")
    args = parser.parse_args()
    if sum((args.check_current, args.check_frozen_ref, args.write)) != 1:
        parser.error("choose exactly one of --check-current, --check-frozen-ref, or --write")

    root = repository_root()
    artifact = args.artifact if args.artifact.is_absolute() else root / args.artifact
    if args.check_current:
        return check_current(root, artifact)
    if args.check_frozen_ref:
        return check_frozen_ref(root, artifact)

    artifact.parent.mkdir(parents=True, exist_ok=True)
    artifact.write_text(serialized(build_inventory(root)), encoding="utf-8")
    print(f"Wrote {artifact}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
