#!/usr/bin/env python3
"""Validate real #646 plan evidence and run the four E3 medium workload matrices.

Measured children record provider-native round trips per invocation. Timed execution
fails closed if an unrelated build/test runtime is present on the host.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shlex
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any


WORKLOADS = ("checkpoint-commit", "bookmark-lookup", "queue-drain", "outbox-drain")
PROVIDERS = ("sqlite", "postgresql", "sqlserver", "mongodb")
FORM = "shared-documents-with-linked-index-tables"


def repository_root() -> Path:
    return Path(__file__).resolve().parents[2]


def fail(message: str) -> int:
    print(f"error: {message}", file=sys.stderr)
    return 2


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def ensure_external(path: Path, root: Path, name: str) -> Path:
    resolved = path.expanduser().resolve()
    if resolved == root or root in resolved.parents:
        raise ValueError(f"{name} must live outside the repository worktree: {resolved}")
    return resolved


def executable(path: Path) -> Path:
    if os.name == "nt":
        path = path.with_suffix(".exe")
    if not path.is_file():
        raise ValueError(
            f"Release adapter host is missing at {path}. Build it once before capturing evidence: "
            "dotnet build benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost/"
            "Elsa.Groundwork.StorePerformance.AdapterHost.csproj -c Release --nologo"
        )
    return path


def probe_provider(child: Path, provider: str) -> dict[str, Any]:
    result = subprocess.run(
        [str(child), "probe-provider", "--provider", provider],
        check=True,
        capture_output=True,
        text=True,
    )
    flags: dict[str, Any] = {"settings": {}}
    for line in result.stdout.splitlines():
        fields = line.split()
        if len(fields) == 2 and fields[0] == "--provider-version":
            flags["provider_version"] = fields[1]
        elif len(fields) == 2 and fields[0] == "--composition":
            flags["composition"] = fields[1]
        elif len(fields) == 2 and fields[0] == "--provider-setting":
            key, separator, value = fields[1].partition("=")
            if not separator:
                raise ValueError("probe-provider emitted a malformed provider setting")
            flags["settings"][key] = value
        elif line.startswith("topology"):
            flags["topology"] = fields[-1]
    required = ("provider_version", "composition", "topology")
    missing = [name for name in required if name not in flags]
    if missing or not flags["settings"]:
        raise ValueError(f"probe-provider omitted required values: {', '.join(missing) or 'provider settings'}")
    return flags


def groundwork_packages(root: Path) -> dict[str, str]:
    versions: dict[str, str] = {}
    for element in ET.parse(root / "Directory.Packages.props").getroot().iter():
        if element.tag.rsplit("}", 1)[-1] != "PackageVersion":
            continue
        name = element.attrib.get("Include", "")
        if name.startswith("Groundwork."):
            versions[name] = element.attrib.get("Version", "")
    if not versions or any(not version for version in versions.values()):
        raise ValueError("Directory.Packages.props does not declare complete Groundwork package provenance")
    return dict(sorted(versions.items()))


def workload_contracts(root: Path) -> dict[str, dict[str, Any]]:
    payload = json.loads(
        (root / "specs/094-harden-groundwork-stores/workloads/runtime.json").read_text(encoding="utf-8")
    )
    return {item["id"]: item for item in payload["workloads"] if item["id"] in WORKLOADS}


def validate_document(
    path: Path,
    workload: str,
    provider: str,
    commit: str,
    harness_digest: str,
    probe: dict[str, Any],
    contract: dict[str, Any],
) -> dict[str, Any]:
    try:
        document = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"cannot read native-plan evidence {path}: {error}") from error
    expected = {
        "SchemaVersion": 2,
        "WorkloadId": workload,
        "WorkloadVersion": contract["version"],
        "Provider": provider,
        "Adapter": "groundwork",
        "PhysicalForm": FORM,
        "Scale": "medium",
        "CommitSha": commit,
        "HarnessAssemblySha256": harness_digest,
        "CompositionFingerprint": probe["composition"],
        "ProviderVersion": probe["provider_version"],
        "ProviderTopology": probe["topology"],
        "ProviderConfiguration": probe["settings"],
        "Seed": contract["input"]["seed"],
        "InputFingerprintSha256": contract["input"]["fingerprintSha256"],
    }
    mismatches = [name for name, value in expected.items() if document.get(name) != value]
    if mismatches:
        raise ValueError(f"{path.name} does not match current provenance: {', '.join(mismatches)}")
    routes = document.get("Routes")
    if not isinstance(routes, list):
        raise ValueError(f"{path.name} has no Routes array")
    required_routes = contract["requiredNativeRoutes"]
    actual_routes = [route.get("RouteIdentity") for route in routes if isinstance(route, dict)]
    if sorted(actual_routes) != sorted(required_routes) or len(actual_routes) != len(set(actual_routes)):
        raise ValueError(f"{path.name} does not contain exactly the required native routes")
    for route in routes:
        reference = route.get("RawPlanReference")
        expected_digest = route.get("RawPlanSha256")
        if not isinstance(reference, str) or Path(reference).name != reference:
            raise ValueError(f"{path.name} contains an unsafe raw-plan reference")
        raw = path.parent / reference
        if not raw.is_file() or sha256(raw) != expected_digest:
            raise ValueError(f"raw native plan {reference} is missing or does not match its digest")
    return document


def matrix_command(
    root: Path,
    child: Path,
    output: Path,
    provider: str,
    document: dict[str, Any],
    packages: dict[str, str],
    evidence_path: Path,
) -> list[str]:
    harness_project = root / "benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks"
    command = [
        "dotnet", "run", "--no-build", "-c", "Release", "--project", str(harness_project), "--",
        "matrix", "medium",
        "--cohort", document["ComparisonCohortId"],
        "--measurement-set", document["MeasurementSetId"],
        "--workload", document["WorkloadId"],
        "--provider", provider,
        "--provider-version", document["ProviderVersion"],
        "--adapter", "groundwork",
        "--form", FORM,
        "--commit", document["CommitSha"],
        "--composition", document["CompositionFingerprint"],
        "--native-plan", document["Identity"],
        "--native-plan-evidence", evidence_path.name,
        "--native-plan-sha256", sha256(evidence_path),
        "--out", str(output / document["WorkloadId"]),
        "--child-command", str(child),
    ]
    for name, version in packages.items():
        command.extend(("--package", f"{name}={version}"))
    for name, value in sorted(document["ProviderConfiguration"].items()):
        command.extend(("--provider-setting", f"{name}={value}"))
    return command


def require_idle_host() -> None:
    """Fail closed when unrelated build/test runtimes would contaminate timed samples."""
    if os.name == "nt":
        result = subprocess.run(
            ["tasklist", "/fo", "csv", "/nh"],
            check=True,
            capture_output=True,
            text=True,
        )
        processes = result.stdout.splitlines()
    else:
        result = subprocess.run(
            ["ps", "-Ao", "pid=,command="],
            check=True,
            capture_output=True,
            text=True,
        )
        processes = result.stdout.splitlines()

    blocked_tokens = ("dotnet", "msbuild", "vstest", "testhost", "xunit")
    blocked = [line.strip() for line in processes if any(token in line.lower() for token in blocked_tokens)]
    if blocked:
        sample = "; ".join(blocked[:5])
        suffix = "" if len(blocked) <= 5 else f"; and {len(blocked) - 5} more"
        raise ValueError(
            "timed E3 execution requires an idle host; unrelated build/test processes are active: "
            + sample
            + suffix
        )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--provider", required=True, choices=PROVIDERS)
    parser.add_argument("--evidence-dir", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--execute", action="store_true", help="run after validation; otherwise print exact commands")
    args = parser.parse_args()
    root = repository_root()
    try:
        evidence = ensure_external(args.evidence_dir, root, "--evidence-dir")
        output = ensure_external(args.out, root, "--out")
    except ValueError as error:
        return fail(str(error))

    missing = [
        f"{workload}.{args.provider}.native-plan.json"
        for workload in WORKLOADS
        if not (evidence / f"{workload}.{args.provider}.native-plan.json").is_file()
    ]
    if missing:
        return fail(
            "required real provider evidence is missing: "
            + ", ".join(missing)
            + ". The runner will not synthesize routed plans or round-trip figures. "
              "Capture provider-native plans first; the AdapterHost capture-plan command currently emits "
              "routed evidence for SQLite and the routeless checkpoint-commit document."
        )

    try:
        child = executable(
            root / "benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost/bin/Release/net10.0/"
            "Elsa.Groundwork.StorePerformance.AdapterHost"
        )
        harness = root / "benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/bin/Release/net10.0/"
        harness /= "Elsa.Groundwork.StorePerformance.Benchmarks.dll"
        child_harness = child.parent / harness.name
        if not harness.is_file() or not child_harness.is_file() or sha256(harness) != sha256(child_harness):
            raise ValueError("Release harness and AdapterHost harness copies are missing or have different digests; build once, then recapture evidence")
        commit = subprocess.run(
            ["git", "rev-parse", "HEAD"], cwd=root, check=True, capture_output=True, text=True
        ).stdout.strip()
        if subprocess.run(
            ["git", "status", "--porcelain", "--untracked-files=all"],
            cwd=root,
            check=True,
            capture_output=True,
            text=True,
        ).stdout:
            raise ValueError("the matrix requires a clean repository; commit the exact source before running")
        probe = probe_provider(child, args.provider)
        packages = groundwork_packages(root)
        contracts = workload_contracts(root)
        commands: list[list[str]] = []
        for workload in WORKLOADS:
            path = evidence / f"{workload}.{args.provider}.native-plan.json"
            document = validate_document(
                path,
                workload,
                args.provider,
                commit,
                sha256(harness),
                probe,
                contracts[workload],
            )
            commands.append(matrix_command(root, child, output, args.provider, document, packages, path))
    except (ValueError, KeyError, subprocess.CalledProcessError) as error:
        return fail(str(error))

    environment = os.environ.copy()
    environment["ELSA_BENCH_NATIVE_PLAN_STAGING"] = str(evidence)
    for command in commands:
        print(shlex.join(command), flush=True)
        if args.execute:
            try:
                require_idle_host()
                subprocess.run(command, cwd=root, env=environment, check=True)
            except ValueError as error:
                return fail(str(error))
            except subprocess.CalledProcessError as error:
                return fail(f"matrix for {command[command.index('--workload') + 1]} exited {error.returncode}")
    if not args.execute:
        print("Validated all four evidence sets. Re-run with --execute to launch the matrices.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
