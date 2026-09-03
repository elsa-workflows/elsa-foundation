#!/usr/bin/env python3
"""Drive #646 evidence phases from the AdapterHost's authoritative matrix catalog.

Correctness, native-plan capture, timed measurement, comparison, and gate evaluation are
separate commands. Diagnostics measurement is explicitly ungraded evidence for budget derivation;
comparison and ratio-gate phases remain blocked until a reviewed policy exists. Every mutating or timed
command is a dry run unless --execute is present.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import os
import re
import shlex
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any


PROVIDERS = ("sqlite", "postgresql", "sqlserver", "mongodb")
SAFE_IDENTIFIER = re.compile(r"^[A-Za-z0-9._-]+$")
LOWER_SHA256 = re.compile(r"^[0-9a-f]{64}$")
SAFE_RAW_PLAN_REFERENCE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*\.(json|txt|xml)$", re.IGNORECASE)
TASKLIST_CSV_ROW = re.compile(r'^"(?:[^"]|"")*"(?:,"(?:[^"]|"")*"){4}$')
TRACE_DETAIL_CONSTITUENT_ROUTES = (
    "trace-detail/summary-by-trace-key",
    "trace-detail/spans-by-trace-key-start-id",
    "trace-detail/logs-by-trace-key-timestamp-id",
    "trace-detail/resources-by-id",
)
TRACE_DETAIL_POINT_READ_ROUTES = {
    "trace-detail/summary-by-trace-key",
    "trace-detail/resources-by-id",
}
ALLOWED_RESULT_FILES = {
    "comparison.v1.json",
    "comparison.from-gate.v1.json",
    "gate.v1.json",
    "measurement.v1.json",
    "budget-gate.v1.json",
}
CAPTURE_MARKER_PREFIX = ".groundwork-capture-in-progress-"


def repository_root() -> Path:
    return Path(__file__).resolve().parents[2]


def fail(message: str) -> int:
    print(f"error: {message}", file=sys.stderr)
    return 2


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def safe_raw_plan_reference(value: Any) -> bool:
    if not isinstance(value, str) or not value or "/" in value or "\\" in value:
        return False
    lowered = value.lower()
    return (
        SAFE_RAW_PLAN_REFERENCE.fullmatch(value) is not None
        and not lowered.endswith((".process.json", ".native-plan.json"))
        and not lowered.startswith("artifact-manifest.")
        and lowered not in ALLOWED_RESULT_FILES
    )


def ensure_external(path: Path, root: Path, name: str) -> Path:
    resolved = path.expanduser().resolve()
    if resolved == root or root in resolved.parents or resolved in root.parents:
        raise ValueError(f"{name} must be disjoint from the repository worktree: {resolved}")
    return resolved


def capture_marker(evidence: Path) -> Path:
    identity = hashlib.sha256(str(evidence).encode("utf-8")).hexdigest()[:32]
    return evidence.parent / f"{CAPTURE_MARKER_PREFIX}{identity}"


def ensure_capture_directory_empty(evidence: Path) -> None:
    if evidence.exists() and not evidence.is_dir():
        raise ValueError(f"capture evidence directory is not a directory: {evidence}")
    marker = capture_marker(evidence)
    if marker.exists():
        raise ValueError(
            f"capture evidence directory has an incomplete prior capture: {evidence}"
        )
    if evidence.exists() and any(evidence.iterdir()):
        raise ValueError(
            f"capture requires an empty evidence directory; use a fresh directory: {evidence}"
        )


def ensure_measurement_output_admissible(output: Path) -> None:
    if output.exists() and not output.is_dir():
        raise ValueError(f"measurement output directory is not a directory: {output}")
    if output.exists() and any(output.iterdir()) and not (output / "artifact-manifest.v2.json").is_file():
        raise ValueError(
            "preexisting measurement output requires artifact-manifest.v2.json; "
            f"use a fresh directory: {output}"
        )


def begin_capture(evidence: Path) -> Path:
    if evidence.exists() and not evidence.is_dir():
        raise ValueError(f"capture evidence directory is not a directory: {evidence}")
    evidence.mkdir(parents=True, exist_ok=True)
    marker = capture_marker(evidence)
    try:
        with marker.open("x", encoding="utf-8"):
            pass
    except FileExistsError as error:
        raise ValueError(
            f"capture evidence directory has an incomplete prior capture: {evidence}"
        ) from error
    if any(evidence.iterdir()):
        raise ValueError(
            f"capture requires an empty evidence directory; use a fresh directory: {evidence}"
        )
    return marker


def complete_capture(marker: Path) -> None:
    marker.unlink()


def require_capture_complete(evidence: Path) -> None:
    marker = capture_marker(evidence)
    if marker.exists():
        raise ValueError(
            f"capture evidence is invalidated by an incomplete capture: {evidence}"
        )


def executable(path: Path) -> Path:
    candidate = path.with_suffix(".exe") if os.name == "nt" else path
    if not candidate.is_file():
        raise ValueError(
            f"Release adapter host is missing at {candidate}. Build it first with: "
            "dotnet build benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost/"
            "Elsa.Groundwork.StorePerformance.AdapterHost.csproj -c Release --nologo"
        )
    return candidate


def release_binaries(root: Path) -> tuple[Path, Path]:
    child = executable(
        root / "benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost/bin/Release/net10.0/"
        "Elsa.Groundwork.StorePerformance.AdapterHost"
    )
    harness = (
        root / "benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/bin/Release/net10.0/"
        "Elsa.Groundwork.StorePerformance.Benchmarks.dll"
    )
    child_harness = child.parent / harness.name
    if not harness.is_file() or not child_harness.is_file() or sha256(harness) != sha256(child_harness):
        raise ValueError(
            "Release harness and AdapterHost harness copies are missing or have different digests; "
            "build both from the same clean source before capturing evidence"
        )
    return child, harness


def run_text(command: list[str], *, cwd: Path) -> str:
    result = subprocess.run(command, cwd=cwd, check=False, capture_output=True, text=True)
    if result.returncode != 0:
        raise ValueError(f"{Path(command[0]).name} failed with exit code {result.returncode}")
    return result.stdout.strip()


def strict_json(text: str, source: str) -> Any:
    def object_without_duplicates(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        document: dict[str, Any] = {}
        for name, value in pairs:
            if name in document:
                raise ValueError(f"{source} contains duplicate JSON property '{name}'")
            document[name] = value
        return document

    try:
        return json.loads(text, object_pairs_hook=object_without_duplicates)
    except json.JSONDecodeError as error:
        raise ValueError(f"{source} contains invalid JSON: {error}") from error


def matrix_catalog(root: Path, child: Path) -> dict[str, Any]:
    try:
        output = run_text([str(child), "describe-matrix"], cwd=root)
    except ValueError as error:
        raise ValueError(
            "AdapterHost describe-matrix failed; rebuild the Release AdapterHost and harness from current HEAD"
        ) from error
    document = strict_json(output, "AdapterHost describe-matrix")
    if document.get("SchemaVersion") != 3 or not isinstance(document.get("Registrations"), list):
        raise ValueError("AdapterHost describe-matrix did not emit the schema-v3 registration catalog")
    revision = source_provenance(root)
    build = document.get("Build")
    if not isinstance(build, dict) or build.get("AdapterHostRevision") != revision or build.get("HarnessRevision") != revision:
        raise ValueError(
            "Release AdapterHost and benchmark harness must both be rebuilt from the clean current repository HEAD"
        )
    return document


def select_registration(catalog: dict[str, Any], args: argparse.Namespace) -> dict[str, Any]:
    matches = [
        item
        for item in catalog["Registrations"]
        if item.get("WorkloadId") == args.workload
        and item.get("Adapter") == args.adapter
        and item.get("PhysicalForm") == args.form
        and args.provider in item.get("Providers", [])
    ]
    if len(matches) != 1:
        raise ValueError(
            "no unique current registration matches "
            f"{args.workload}/{args.adapter}/{args.form}/{args.provider}; run the status command"
        )
    return matches[0]


def probe_provider(root: Path, child: Path, provider: str) -> dict[str, Any]:
    output = run_text([str(child), "probe-provider", "--provider", provider], cwd=root)
    probe: dict[str, Any] = {"settings": {}}
    for line in output.splitlines():
        name, separator, value = line.partition("=")
        if not separator:
            raise ValueError(f"probe-provider emitted a malformed line: {line}")
        if name == "provider":
            probe["provider"] = value
        elif name == "connection-type":
            probe["connection_type"] = value
        elif name == "provider-version":
            probe["provider_version"] = value
        elif name == "provider-topology":
            probe["topology"] = value
        elif name == "provider-setting":
            key, setting_separator, setting_value = value.partition("=")
            if not setting_separator or not key or not setting_value:
                raise ValueError("probe-provider emitted a malformed provider setting")
            probe["settings"][key] = setting_value
    required = ("provider", "connection_type", "provider_version", "topology")
    missing = [name for name in required if not probe.get(name)]
    if missing or not probe["settings"]:
        raise ValueError(
            f"probe-provider omitted required values: {', '.join(missing) or 'provider settings'}"
        )
    if probe["provider"] != provider:
        raise ValueError(f"probe-provider returned '{probe['provider']}' for requested provider '{provider}'")
    return probe


def provider_packages(root: Path, registration: dict[str, Any], provider: str) -> dict[str, str]:
    central_versions: dict[str, str] = {}
    for element in ET.parse(root / "Directory.Packages.props").getroot().iter():
        if element.tag.rsplit("}", 1)[-1] != "PackageVersion":
            continue
        name = element.attrib.get("Include", "")
        central_versions[name] = element.attrib.get("Version", "")

    names = registration.get("ProviderPackages", {}).get(provider)
    if not isinstance(names, list) or not names or any(not isinstance(name, str) or not name for name in names):
        raise ValueError(
            f"the matrix catalog has no provider package provenance for "
            f"{registration['Adapter']}/{provider}"
        )

    versions = {name: central_versions.get(name, "") for name in names}
    if any(not version for version in versions.values()):
        missing = ", ".join(name for name, version in versions.items() if not version)
        raise ValueError(f"Directory.Packages.props does not declare provider package provenance: {missing}")
    return dict(sorted(versions.items()))


def source_provenance(root: Path) -> str:
    commit = run_text(["git", "rev-parse", "HEAD"], cwd=root)
    dirty = run_text(["git", "status", "--porcelain", "--untracked-files=all"], cwd=root)
    if dirty:
        raise ValueError("#646 evidence requires a clean repository; commit the exact source first")
    return commit


def host_fingerprint(root: Path, harness: Path) -> str:
    value = run_text(["dotnet", str(harness), "host-fingerprint"], cwd=root)
    if len(value) != 64 or any(character not in "0123456789abcdef" for character in value):
        raise ValueError("the harness did not emit a lowercase SHA-256 host fingerprint")
    return value


def evidence_reference(args: argparse.Namespace) -> str:
    return f"{args.workload}.{args.provider}.{args.measurement_set}.native-plan.json"


def validate_target_arguments(args: argparse.Namespace) -> None:
    for name in ("cohort", "measurement_set", "scale"):
        value = getattr(args, name)
        if not SAFE_IDENTIFIER.fullmatch(value):
            raise ValueError(f"--{name.replace('_', '-')} must be a safe identifier")
    if args.composition is not None and not LOWER_SHA256.fullmatch(args.composition):
        raise ValueError("--composition must be a lowercase SHA-256 fingerprint")
    if args.native_plan_identity and not SAFE_IDENTIFIER.fullmatch(args.native_plan_identity):
        raise ValueError("--native-plan-identity must be a safe identifier")


def request_document(
    registration: dict[str, Any],
    probe: dict[str, Any],
    args: argparse.Namespace,
    provenance: dict[str, Any],
    content_digest: str,
) -> dict[str, Any]:
    expected_topology = registration["RequiredProviderTopologies"].get(args.provider)
    if probe["topology"] != expected_topology:
        raise ValueError(
            f"live provider topology '{probe['topology']}' does not match current workload topology "
            f"'{expected_topology}'"
        )
    identity = args.native_plan_identity or (
        f"{args.workload}-{args.provider}-{args.measurement_set}-native-plan"
    )
    return {
        "ComparisonCohortId": args.cohort,
        "MeasurementSetId": args.measurement_set,
        "WorkloadId": registration["WorkloadId"],
        "WorkloadVersion": registration["WorkloadVersion"],
        "Provider": args.provider,
        "ProviderVersion": probe["provider_version"],
        "ProviderTopology": probe["topology"],
        "ProviderConfiguration": probe["settings"],
        "Adapter": registration["Adapter"],
        "PhysicalForm": registration["PhysicalForm"],
        "Scale": args.scale,
        "CommitSha": provenance["commit"],
        "HarnessAssemblySha256": provenance["harness"],
        "CompositionFingerprint": provenance["composition"],
        "HostFingerprintSha256": provenance["host"],
        "PackageVersions": provenance["packages"],
        "Seed": registration["Seed"],
        "InputFingerprintSha256": registration["InputFingerprintSha256"],
        "NativePlanIdentity": identity,
        "NativePlanEvidenceReference": evidence_reference(args),
        "NativePlanContentSha256": content_digest,
        "ProcessKind": 1,
        "ProcessIndex": 0,
    }


def validate_evidence(
    path: Path,
    request: dict[str, Any],
    registration: dict[str, Any],
    *,
    timing: bool,
) -> dict[str, Any]:
    try:
        document = strict_json(path.read_text(encoding="utf-8"), path.name)
    except OSError as error:
        raise ValueError(f"cannot read native-plan evidence {path}: {error}") from error
    expected = {
        "SchemaVersion": 2,
        "ComparisonCohortId": request["ComparisonCohortId"],
        "MeasurementSetId": request["MeasurementSetId"],
        "WorkloadId": request["WorkloadId"],
        "WorkloadVersion": request["WorkloadVersion"],
        "Provider": request["Provider"],
        "Adapter": request["Adapter"],
        "PhysicalForm": request["PhysicalForm"],
        "Scale": request["Scale"],
        "CommitSha": request["CommitSha"],
        "HarnessAssemblySha256": request["HarnessAssemblySha256"],
        "CompositionFingerprint": request["CompositionFingerprint"],
        "HostFingerprintSha256": request["HostFingerprintSha256"],
        "ProviderVersion": request["ProviderVersion"],
        "ProviderTopology": request["ProviderTopology"],
        "ProviderConfiguration": request["ProviderConfiguration"],
        "Seed": request["Seed"],
        "InputFingerprintSha256": request["InputFingerprintSha256"],
        "Identity": request["NativePlanIdentity"],
    }
    mismatches = [name for name, value in expected.items() if document.get(name) != value]
    if mismatches:
        raise ValueError(f"{path.name} does not match current request provenance: {', '.join(mismatches)}")

    routes = document.get("Routes")
    blocked = document.get("BlockedRoutes")
    trace_detail = document.get("TraceDetailConstituents")
    if blocked is None:
        blocked = []
    if trace_detail is None:
        trace_detail = []
    if not isinstance(routes, list) or not isinstance(blocked, list) or not isinstance(trace_detail, list):
        raise ValueError(f"{path.name} must contain Routes, BlockedRoutes, and TraceDetailConstituents arrays")
    if any(not isinstance(route, dict) or not isinstance(route.get("RouteIdentity"), str) for route in routes):
        raise ValueError(f"{path.name} contains an invalid native route entry")
    if any(not isinstance(route, str) or not route for route in blocked):
        raise ValueError(f"{path.name} contains an invalid blocked route identity")
    route_names = [route["RouteIdentity"] for route in routes]
    admitted_route_names = route_names + (["trace-detail"] if trace_detail else [])
    required = registration["RequiredNativeRoutes"]
    if timing and sorted(admitted_route_names) != sorted(required):
        raise ValueError(f"{path.name} does not capture every required native route for timing")
    if sorted(admitted_route_names + blocked) != sorted(required):
        raise ValueError(f"{path.name} does not account for every required route as captured or blocked")
    if len(admitted_route_names + blocked) != len(set(admitted_route_names + blocked)):
        raise ValueError(f"{path.name} contains duplicate captured/blocked route identities")
    raw_references = []
    for route in routes:
        reference = route.get("RawPlanReference")
        expected_digest = route.get("RawPlanSha256")
        if not safe_raw_plan_reference(reference) or not isinstance(expected_digest, str) or not LOWER_SHA256.fullmatch(expected_digest):
            raise ValueError(f"{path.name} contains an unsafe raw-plan reference")
        raw = path.parent / reference
        if not raw.is_file() or sha256(raw) != expected_digest:
            raise ValueError(f"raw native plan {reference} is missing or does not match its digest")
        raw_references.append(reference)

    constituent_names = []
    for constituent in trace_detail:
        if (
            not isinstance(constituent, dict)
            or not isinstance(constituent.get("RouteIdentity"), str)
            or not constituent["RouteIdentity"]
            or not isinstance(constituent.get("RawPlanReference"), str)
            or not isinstance(constituent.get("RawPlanSha256"), str)
            or not isinstance(constituent.get("PlanClassification"), str)
            or not constituent["PlanClassification"].strip()
            or not isinstance(constituent.get("PhysicalIndexName"), str)
            or not isinstance(constituent.get("CommandText"), str)
            or not constituent["CommandText"].strip()
        ):
            raise ValueError(f"{path.name} contains an invalid trace-detail constituent entry")

        constituent_names.append(constituent["RouteIdentity"])
        reference = constituent["RawPlanReference"]
        digest = constituent["RawPlanSha256"]
        pages = constituent.get("Pages")
        is_point_read = constituent["RouteIdentity"] in TRACE_DETAIL_POINT_READ_ROUTES
        integer_fields = (
            "PhysicalCardinality",
            "FiniteLimit",
            "PublicRowBound",
            "MaterializedCandidateCount",
            "ObservedCommandCount",
            "MaxInvocationCount",
        )
        if any(
            isinstance(constituent.get(name), bool)
            or not isinstance(constituent.get(name), int)
            or constituent[name] <= 0
            for name in integer_fields
        ) or any(not isinstance(constituent.get(name), bool) for name in ("HasStorageScopePredicate", "HasRoutePredicate")):
            raise ValueError(f"{path.name} contains invalid trace-detail constituent bounds or predicates")
        if not constituent["HasStorageScopePredicate"] or not constituent["HasRoutePredicate"]:
            raise ValueError(f"{path.name} contains a trace-detail constituent without required predicates")
        if pages is not None and not isinstance(pages, list):
            raise ValueError(f"{path.name} contains invalid trace-detail continuation pages")
        if is_point_read:
            if constituent["PlanClassification"] != "primary-key-read" or constituent["PhysicalIndexName"]:
                raise ValueError(f"{path.name} contains an invalid trace-detail point-read classification")
            if reference or digest or pages:
                raise ValueError(
                    f"{path.name} contains a trace-detail point read with an explain artifact or continuation page"
                )
        else:
            if (
                constituent["PlanClassification"] != "index-search"
                or not constituent["PhysicalIndexName"].strip()
                or not safe_raw_plan_reference(reference)
                or not LOWER_SHA256.fullmatch(digest)
            ):
                raise ValueError(f"{path.name} contains an unsafe or undigested trace-detail raw-plan reference")
            raw = path.parent / reference
            if not raw.is_file() or sha256(raw) != digest:
                raise ValueError(f"trace-detail raw native plan {reference} is missing or does not match its digest")
            raw_references.append(reference)

        page_entries = [] if pages is None else pages
        page_indices = []
        for page in page_entries:
            if not isinstance(page, dict):
                raise ValueError(f"{path.name} contains an invalid trace-detail continuation page entry")
            page_index = page.get("PageIndex")
            page_reference = page.get("RawPlanReference")
            page_digest = page.get("RawPlanSha256")
            command_text = page.get("CommandText")
            if (
                isinstance(page_index, bool)
                or not isinstance(page_index, int)
                or page_index <= 0
                or not safe_raw_plan_reference(page_reference)
                or not isinstance(page_digest, str)
                or not LOWER_SHA256.fullmatch(page_digest)
                or not isinstance(command_text, str)
                or not command_text.strip()
            ):
                raise ValueError(f"{path.name} contains an invalid trace-detail continuation page entry")
            page_indices.append(page_index)
            raw = path.parent / page_reference
            if not raw.is_file() or sha256(raw) != page_digest:
                raise ValueError(
                    f"trace-detail page {page_index} raw native plan {page_reference} is missing or does not match its digest"
                )
            raw_references.append(page_reference)
        expected_page_count = 1 if is_point_read else (
            constituent["PublicRowBound"] + constituent["FiniteLimit"] - 1
        ) // constituent["FiniteLimit"]
        if page_indices != list(range(1, expected_page_count)):
            raise ValueError(f"{path.name} contains non-sequential trace-detail continuation page indexes")

    if trace_detail and sorted(constituent_names) != sorted(TRACE_DETAIL_CONSTITUENT_ROUTES):
        raise ValueError(f"{path.name} does not account for every trace-detail constituent exactly once")
    if len(raw_references) != len(set(raw_references)):
        raise ValueError(f"{path.name} contains duplicate raw provider-plan references")
    return document


def require_phase(registration: dict[str, Any], phase: str) -> None:
    if phase == "capture" and registration["CapturePlanStatus"] == "unsupported":
        raise ValueError(
            f"capture is blocked: {registration['CapturePlanReason']} for "
            f"{registration['WorkloadId']}/{registration['Adapter']}"
        )
    if phase == "correctness" and registration["CorrectnessStatus"] != "ready":
        raise ValueError(
            f"correctness is blocked: {registration['CorrectnessReason']} for "
            f"{registration['WorkloadId']}/{registration['Adapter']}"
        )
    if phase == "measure" and registration["MeasurementStatus"] not in {"ready", "ungraded"}:
        raise ValueError(
            f"measurement is blocked: {registration['MeasurementReason']} for "
            f"{registration['WorkloadId']}/{registration['Adapter']}"
        )
    if phase == "measure" and registration["MeasurementStatus"] == "ungraded" and registration["WorkloadId"] != "diagnostics-durable-history":
        raise ValueError(
            f"only the diagnostics workload may use an ungraded measurement phase for "
            f"{registration['WorkloadId']}/{registration['Adapter']}"
        )


def process_pid(line: str, *, windows: bool) -> int | None:
    stripped = line.strip()
    if not stripped:
        return None
    if windows:
        if TASKLIST_CSV_ROW.fullmatch(stripped) is None:
            return None
        try:
            fields = next(csv.reader([stripped], strict=True))
        except csv.Error:
            return None
        if len(fields) != 5:
            return None
        raw_pid = fields[1]
    else:
        raw_pid = stripped.split(maxsplit=1)[0]
    if not (raw_pid.isascii() and raw_pid.isdecimal()):
        return None
    return int(raw_pid)


def require_idle_host() -> None:
    root = repository_root()
    windows = os.name == "nt"
    if windows:
        processes = run_text(["tasklist", "/fo", "csv", "/nh"], cwd=root).splitlines()
    else:
        processes = run_text(["ps", "-Ao", "pid=,command="], cwd=root).splitlines()
    own_pid = os.getpid()
    blocked_tokens = ("dotnet", "msbuild", "vstest", "testhost", "xunit")
    blocked = [
        line.strip()
        for line in processes
        if process_pid(line, windows=windows) != own_pid
        and any(token in line.lower() for token in blocked_tokens)
    ]
    if blocked:
        sample = "; ".join(blocked[:5])
        suffix = "" if len(blocked) <= 5 else f"; and {len(blocked) - 5} more"
        raise ValueError(f"timed execution requires an idle host; active build/test processes: {sample}{suffix}")


def command_text(command: list[str]) -> None:
    print(shlex.join(command), flush=True)


def target_context(
    args: argparse.Namespace,
    phase: str | None = None,
    root: Path | None = None,
) -> tuple[Path, Path, Path, dict[str, Any], dict[str, Any], dict[str, Any]]:
    validate_target_arguments(args)
    root = root or repository_root()
    child, harness = release_binaries(root)
    registration = select_registration(matrix_catalog(root, child), args)
    if phase is not None:
        require_phase(registration, phase)
    probe = probe_provider(root, child, args.provider)
    provenance = {
        "commit": source_provenance(root),
        "harness": sha256(harness),
        "host": host_fingerprint(root, harness),
        "packages": provider_packages(root, registration, args.provider),
        # The first request is only the identity envelope used by the side-effect-free
        # describe-composition command. The resulting digest replaces this placeholder below.
        "composition": "0" * 64,
    }
    request = request_document(registration, probe, args, provenance, "0" * 64)
    output = run_text(
        [str(child), "describe-composition", "--request", json.dumps(request, separators=(",", ":"))],
        cwd=root,
    )
    document = strict_json(output, "AdapterHost describe-composition")
    composition = document.get("Fingerprint")
    if not isinstance(composition, str) or not LOWER_SHA256.fullmatch(composition):
        raise ValueError("AdapterHost describe-composition did not emit a lowercase SHA-256 fingerprint")
    if args.composition is not None and args.composition != composition:
        raise ValueError(
            f"--composition '{args.composition}' does not match the current adapter composition '{composition}'"
        )
    provenance["composition"] = composition
    return root, child, harness, registration, provenance, probe


def status(args: argparse.Namespace) -> int:
    root = repository_root()
    child, _ = release_binaries(root)
    catalog = matrix_catalog(root, child)
    if args.json:
        print(json.dumps(catalog, indent=2))
        return 0
    print("workload\tversion\tadapter\tform\tproviders\tcapture\tcorrectness\tmeasurement\tmeasurement-reason\ttiming\ttiming-reason")
    for item in catalog["Registrations"]:
        print("\t".join([
            item["WorkloadId"], item["WorkloadVersion"], item["Adapter"], item["PhysicalForm"],
            ",".join(item["Providers"]), item["CapturePlanStatus"], item["CorrectnessStatus"],
            item["MeasurementStatus"], item["MeasurementReason"], item["TimingStatus"], item["TimingReason"],
        ]))
    return 0


def capture(args: argparse.Namespace) -> int:
    root = repository_root()
    evidence = ensure_external(args.evidence_dir, root, "--evidence-dir")
    if args.execute:
        marker = begin_capture(evidence)
    else:
        ensure_capture_directory_empty(evidence)
        marker = None
    root, child, _, registration, provenance, probe = target_context(args, "capture", root)
    request = request_document(registration, probe, args, provenance, "0" * 64)
    command = [str(child), "capture-plan", "--request", json.dumps(request, separators=(",", ":")), "--out", str(evidence)]
    command_text(command)
    if args.execute:
        subprocess.run(command, cwd=root, check=True)
        if marker is None:
            raise ValueError("capture marker is missing")
        complete_capture(marker)
    else:
        print("Dry run only. Re-run with --execute to capture provider-native evidence.")
    return 0


def correctness(args: argparse.Namespace) -> int:
    root = repository_root()
    evidence = ensure_external(args.evidence_dir, root, "--evidence-dir")
    require_capture_complete(evidence)
    output = ensure_external(args.out, root, "--out")
    root, child, _, registration, provenance, probe = target_context(args, "correctness", root)
    path = evidence / evidence_reference(args)
    request = request_document(registration, probe, args, provenance, sha256(path))
    validate_evidence(path, request, registration, timing=False)
    command = [str(child), "verify-correctness", "--request", json.dumps(request, separators=(",", ":")), "--out", str(output)]
    command_text(command)
    if args.execute:
        environment = os.environ.copy()
        environment["ELSA_BENCH_NATIVE_PLAN_STAGING"] = str(evidence)
        subprocess.run(command, cwd=root, env=environment, check=True)
    else:
        print("Dry run only. Re-run with --execute to verify correctness; no timing will run.")
    return 0


def measure(args: argparse.Namespace) -> int:
    root = repository_root()
    evidence = ensure_external(args.evidence_dir, root, "--evidence-dir")
    require_capture_complete(evidence)
    output = ensure_external(args.out, root, "--out")
    ensure_measurement_output_admissible(output)
    root, child, harness, registration, provenance, probe = target_context(args, "measure", root)
    path = evidence / evidence_reference(args)
    request = request_document(registration, probe, args, provenance, sha256(path))
    document = validate_evidence(path, request, registration, timing=True)
    command = [
        "dotnet", str(harness), "matrix", args.scale,
        "--cohort", args.cohort,
        "--measurement-set", args.measurement_set,
        "--workload", registration["WorkloadId"],
        "--provider", args.provider,
        "--provider-version", probe["provider_version"],
        "--adapter", registration["Adapter"],
        "--form", registration["PhysicalForm"],
        "--commit", request["CommitSha"],
        "--composition", request["CompositionFingerprint"],
        "--native-plan", document["Identity"],
        "--native-plan-evidence", path.name,
        "--native-plan-sha256", sha256(path),
        "--out", str(output),
        "--child-command", str(child),
    ]
    for name, version in request["PackageVersions"].items():
        command.extend(("--package", f"{name}={version}"))
    for name, value in sorted(probe["settings"].items()):
        command.extend(("--provider-setting", f"{name}={value}"))
    measurement_command = ["dotnet", str(harness), "measure", "--out", str(output)]
    command_text(command)
    command_text(measurement_command)
    if args.execute:
        require_idle_host()
        environment = os.environ.copy()
        environment["ELSA_BENCH_NATIVE_PLAN_STAGING"] = str(evidence)
        subprocess.run(command, cwd=root, env=environment, check=True)
        subprocess.run(measurement_command, cwd=root, check=True)
    else:
        print("Dry run only. Re-run with --execute on an idle host to launch timed measurement and emit its ungraded result.")
    return 0


def compare_or_gate(args: argparse.Namespace) -> int:
    root = repository_root()
    child, harness = release_binaries(root)
    matrix_catalog(root, child)
    output = ensure_external(args.out, root, "--out")
    command = ["dotnet", str(harness), args.command, "--out", str(output), "--oracle", args.oracle, "--target", args.target]
    if args.command == "gate" and args.replacement:
        command.extend(("--replacement", str(args.replacement.expanduser().resolve())))
    command_text(command)
    if args.execute:
        subprocess.run(command, cwd=root, check=True)
    else:
        print(f"Dry run only. Re-run with --execute to {args.command} retained measurement sets.")
    return 0


def add_target_arguments(parser: argparse.ArgumentParser, *, output: bool) -> None:
    parser.add_argument("--provider", required=True, choices=PROVIDERS)
    parser.add_argument("--workload", required=True)
    parser.add_argument("--adapter", required=True)
    parser.add_argument("--form", required=True)
    parser.add_argument("--cohort", required=True)
    parser.add_argument("--measurement-set", required=True)
    parser.add_argument("--composition")
    parser.add_argument("--native-plan-identity")
    parser.add_argument("--scale", default="medium")
    parser.add_argument("--evidence-dir", required=True, type=Path)
    if output:
        parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--execute", action="store_true")


def parser() -> argparse.ArgumentParser:
    root = argparse.ArgumentParser(description=__doc__)
    commands = root.add_subparsers(dest="command", required=True)
    status_parser = commands.add_parser("status", help="show current registration and phase readiness")
    status_parser.add_argument("--json", action="store_true")
    add_target_arguments(commands.add_parser("capture", help="capture provider-native plan evidence"), output=False)
    add_target_arguments(commands.add_parser("correctness", help="run correctness only"), output=True)
    add_target_arguments(commands.add_parser("measure", help="run the timed matrix only"), output=True)
    for name in ("compare", "gate"):
        phase = commands.add_parser(name, help=f"{name} retained measurement sets")
        phase.add_argument("--out", required=True, type=Path)
        phase.add_argument("--oracle", required=True)
        phase.add_argument("--target", required=True)
        phase.add_argument("--execute", action="store_true")
        if name == "gate":
            phase.add_argument("--replacement", type=Path)
    return root


def main() -> int:
    args = parser().parse_args()
    try:
        if args.command == "status":
            return status(args)
        if args.command == "capture":
            return capture(args)
        if args.command == "correctness":
            return correctness(args)
        if args.command == "measure":
            return measure(args)
        return compare_or_gate(args)
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError) as error:
        return fail(str(error))


if __name__ == "__main__":
    raise SystemExit(main())
