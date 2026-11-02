"""Fail-closed verification of this witness's retained receipts; never changes report status.

validate_report(report, root, allowed_controls) returns None or raises EvidenceError.
Consumers backend/studio: source/status, assets/lock/frontendAudit file refs,
frontendInvocations=0, sourceReferences=[], publishedDirectory and baseline/after
maps covering every top-level published DLL and wwwroot file.
Authors backend: source/status/assets/lock/sourceReferences; studio: source/status,
sdkSha256/graphAudit ref. Graph audit: sdkSha256, sdkTgz, sourceReferences=[],
workspaceImports=[], installedSdk ref (the package.json actually consumed).
Cases: id/source/status/receipt ref. Receipt repeats identity/status, named true
observations, ownerBefore/ownerAfter refs, nonempty evidence ref list. Controls
also retain expectedFailureObserved=true on record+receipt and exact actual
stage/diagnostic matching DRIVER-owned allowed_controls[id]={stage,diagnostic}.
All file refs are {path,sha256} under the owned root. No pending data qualifies.

validate_complete_witness(report, root, allowed_controls) is the final gate for
independentPackageWitness: validate_report plus the declared current manifest, the four
passed current-authority records, every #3440 autonomy case exactly once per actual adapter
with its expected cause and retained owner/correlation evidence, and each manifest-named
matrix stage exactly once. witness_gaps(report) is its file-free structural reader.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re
import tarfile
import xml.etree.ElementTree as ET
import zipfile

PROVIDERS = ("copilot", "anthropic")
RESOURCE = ("context", "read", "review", "ownerReadback", "homeReload", "unrelatedPreserved", "actualAdapter")
WORKFLOW = ("workingStateChanged", "validated", "wholeUndo", "noImplicitSave", "explicitSaveReadback", "actualAdapter")
REQUIRED_CASES = {f"{prefix}{kind}-{provider}": checks for prefix in ("", "restored-")
                  for provider in PROVIDERS for kind, checks in (("resource", RESOURCE), ("workflow", WORKFLOW))}
REQUIRED_CASES.update({"module-add": ("membership", "servingGeneration", "contributions"),
                      "module-remove": ("membership", "servingGeneration", "contributions", "providerAvailable"),
                      "module-restore": ("originalArchive", "membership", "servingGeneration", "contributions"),
                      "consumed-intent-remount": ("nativeReconciliation", "noResubmission"),
                      "duplicate-execute": ("retainedResult", "oneMutation"),
                      "duplicate-ack": ("retainedResult", "oneMutation"),
                      "renderer-unavailable": ("safeGenericPresentation", "commonControls", "ownerObserved"),
                      "redaction-opacity": ("canariesOmitted", "unrelatedPreserved", "opaqueCommonPayloads", "separateProtocolFixture")})
CONTROL_IDS = frozenset(("module-missing", "sdk-incompatible", "pending-module-removed", "read-denied", "write-denied",
                        "cross-user", "cross-tenant", "cross-host", "foreign-scope", "malformed-scope", "malformed-arguments",
                        "missing-resource", "default-resource", "no-op-resource", "invalid-schema", "stale-revision",
                        "concurrent-cas", "malformed-result", "client-binding-removed",
                        "deny-before-effect", "cancel-before-effect", "lost-confirmation"))
REQUIRED_CASES.update({name: ("ownerObserved", "noFalseSuccess", "noReplay") for name in CONTROL_IDS})
REQUIRED_CASES["concurrent-cas"] += ("oneWinner", "oneConflict409", "winnerStatePreserved")
REQUIRED_CASES["lost-confirmation"] += ("responseLost", "uncertain")
UNCHANGED_CONTROLS = CONTROL_IDS - {"concurrent-cas", "lost-confirmation", "malformed-result"}


class EvidenceError(ValueError):
    """A retained witness input is absent, inconsistent or does not prove its claim."""


def _need(condition, message):
    if not condition:
        raise EvidenceError(message)


def validate_report(report, root, allowed_controls):
    """Validate all mandatory evidence using explicit, nonempty driver control expectations."""
    try:
        _validate(report, Path(root).resolve(), allowed_controls)
    except (KeyError, TypeError, AttributeError, OSError, ValueError, zipfile.BadZipFile, tarfile.TarError, ET.ParseError) as error:
        if isinstance(error, EvidenceError):
            raise
        raise EvidenceError("Malformed or unreadable retained evidence: " + str(error)) from error


def _validate(report, root, allowed_controls):
    def file_ref(value):
        _need(isinstance(value, dict), "Missing retained file reference")
        path = Path(value["path"])
        _need(path.is_absolute() and path.resolve().is_relative_to(root) and path.is_file(), "Missing/foreign retained file: " + str(path))
        digest = value["sha256"]
        _need(isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest), "Invalid SHA-256")
        _need(hashlib.sha256(path.read_bytes()).hexdigest() == digest, "Retained file hash mismatch: " + str(path))
        return path

    def json_ref(value):
        return json.loads(file_ref(value).read_text())

    source = report["source"]
    _need(report.get("status") in ("preparing", "running", "verifying", "passed"), "Failed/skipped report cannot qualify")
    _need(isinstance(source, str) and re.fullmatch(r"[0-9a-f]{40}", source), "Invalid committed source identity")
    _need(report["producer"]["source"] == source, "Producer source identity mismatch")
    _need(report.get("evidenceCategory") == "deterministic-provider-protocol" and report.get("liveProvider") is False, "Unsupported provider evidence claim")
    _need(isinstance(allowed_controls, dict) and set(allowed_controls) == CONTROL_IDS, "Explicit complete driver control expectations required")
    for expected in allowed_controls.values():
        _need(isinstance(expected["stage"], str) and bool(expected["stage"].strip()), "Control stage must be explicit")
        diagnostics = expected["diagnostic"] if isinstance(expected["diagnostic"], list) else [expected["diagnostic"]]
        _need(bool(diagnostics) and all(isinstance(item, str) and item.strip() for item in diagnostics), "Control diagnostic must be precise")
    sdk = report["producer"]["sdk"]
    sdk_path = file_ref(sdk)
    with tarfile.open(sdk_path) as archive:
        manifest_bytes = archive.extractfile("package/package.json").read()
    manifest = json.loads(manifest_bytes)
    _need(manifest["name"] == "@elsa-workflows/studio-sdk" and manifest["version"] == report["version"], "SDK archive identity mismatch")
    packages = report["packages"]
    _need(isinstance(packages, dict) and bool(packages), "No retained NuGet distributions")
    for package_id, package in packages.items():
        with zipfile.ZipFile(file_ref(package)) as archive:
            names = [name for name in archive.namelist() if name.endswith(".nuspec")]
            _need(len(names) == 1, "NuGet archive needs exactly one nuspec")
            spec = ET.fromstring(archive.read(names[0]))
        values = {node.tag.rsplit("}", 1)[-1]: node.text for node in spec.iter()}
        dependencies = [dict(node.attrib) for node in spec.iter() if node.tag.rsplit("}", 1)[-1] == "dependency"]
        _need(package_id == package["id"] == values["id"] and package["version"] == values["version"], "NuGet archive identity mismatch")
        _need(package["dependencies"] == dependencies, "NuGet dependency identity mismatch")

    def closure(record):
        _need(record["source"] == source and record["status"] == "passed" and record["sourceReferences"] == [], "Unverified/source-linked consumer")
        assets = json_ref(record["assets"])
        file_ref(record["lock"])
        libraries = assets["libraries"]
        _need(bool(libraries) and all(value.get("type") == "package" for value in libraries.values()), "Product project/source reference in assets")
        selected = {key.rsplit("/", 1)[0]: key.rsplit("/", 1)[1] for key in libraries}
        _need(bool(set(selected) & set(packages)), "Consumer did not restore retained packages")
        _need(all(selected[name] == package["version"] for name, package in packages.items() if name in selected), "Restored candidate version mismatch")

    _need(set(report["consumers"]) == {"backend", "studio"}, "Both real consumers required")
    for record in report["consumers"].values():
        closure(record)
        _need(type(record["frontendInvocations"]) is int and record["frontendInvocations"] == 0, "Consumer invoked frontend tooling")
        _need(file_ref(record["frontendAudit"]).read_bytes() == b"", "Frontend denial audit observed an invocation")
        before, after = record["baseline"], record["after"]
        _need(bool(before) and set(before) == set(after), "Incomplete common baseline comparison")
        published = Path(record["publishedDirectory"]).resolve()
        _need(published.is_dir() and published.is_relative_to(root), "Missing/foreign published consumer")
        current = {path.relative_to(published).as_posix(): path for path in published.iterdir() if path.is_file() and path.suffix == ".dll"}
        current.update({path.relative_to(published).as_posix(): path for path in (published / "wwwroot").rglob("*") if path.is_file()})
        _need(set(before) == set(current), "Common baseline omits published implementation files")
        for name in before:
            file_ref(before[name]); file_ref(after[name])
            _need(Path(after[name]["path"]).resolve() == current[name].resolve(), "After comparison is not the actual published file")
            _need(before[name]["sha256"] == after[name]["sha256"], "Common implementation changed: " + name)
    authors = report["authors"]
    closure(authors["backend"])
    studio = authors["studio"]
    _need(studio["source"] == source and studio["status"] == "passed" and studio["sdkSha256"] == sdk["sha256"], "External SDK author identity mismatch")
    audit = json_ref(studio["graphAudit"])
    _need(audit["sdkSha256"] == sdk["sha256"] and Path(audit["sdkTgz"]).resolve() == sdk_path.resolve(), "External author did not consume retained SDK")
    _need(audit["sourceReferences"] == [] and audit["workspaceImports"] == [], "External author used source/workspace imports")
    _need(file_ref(audit["installedSdk"]).read_bytes() == manifest_bytes, "Installed SDK manifest differs from retained tgz")
    cases = report["cases"]
    _need(isinstance(cases, list) and len(cases) == len(REQUIRED_CASES), "Missing, duplicate or extra required cases")
    _need({case["id"] for case in cases} == set(REQUIRED_CASES), "Required case coverage mismatch")
    for case in cases:
        name = case["id"]
        receipt = json_ref(case["receipt"])
        expected_status = "failed" if name in CONTROL_IDS else "passed"
        _need(case["source"] == receipt["source"] == source and receipt["id"] == name, "Case source/identity mismatch: " + name)
        _need(case["status"] == receipt["status"] == expected_status, "Unrun/skipped/wrong-status case: " + name)
        _need(all(receipt["observations"].get(item) is True for item in REQUIRED_CASES[name]), "Missing actual observations: " + name)
        file_ref(receipt["ownerBefore"]); file_ref(receipt["ownerAfter"])
        if name in UNCHANGED_CONTROLS:
            _need(receipt["ownerBefore"]["sha256"] == receipt["ownerAfter"]["sha256"], "Pre-effect/read-only control changed owner: " + name)
        _need(isinstance(receipt["evidence"], list) and bool(receipt["evidence"]), "Owner markers alone are insufficient: " + name)
        for value in receipt["evidence"]:
            file_ref(value)
        if name in CONTROL_IDS:
            expected = allowed_controls[name]
            diagnostics = expected["diagnostic"] if isinstance(expected["diagnostic"], list) else [expected["diagnostic"]]
            _need(case.get("expectedFailureObserved") is True and receipt.get("expectedFailureObserved") is True, "Failure recognition missing: " + name)
            _need(receipt["stage"] == expected["stage"] and receipt["diagnostic"] in diagnostics, "Wrong-cause control failure: " + name)
        else:
            _need(case.get("expectedFailureObserved") in (None, False), "Positive case cannot be an expected failure")


def _retained_ref(root, value):
    _need(isinstance(value, dict) and isinstance(value.get("path"), str) and isinstance(value.get("sha256"), str), "Missing retained file reference")
    path = Path(value["path"])
    _need(path.is_absolute() and path.resolve().is_relative_to(root) and path.is_file(), "Missing/foreign retained file: " + str(path))
    _need(hashlib.sha256(path.read_bytes()).hexdigest() == value["sha256"], "Retained file hash mismatch: " + str(path))
    return path


def witness_manifest():
    """The complete witness inventory: original38 + four current-authority + 13 cases per actual adapter.

    Stage expectations are only the matrix's own named browser stages. No total stage count
    (the original run's 135) is part of the contract; production/author stages may change.
    """
    from autonomy_journeys import EXPECTED_CASES as AUTONOMY_CASES, autonomy_stages
    from disclosure_journeys import EXPECTED_CASES as AUTHORITY_CASES
    return {"originalCases": sorted(REQUIRED_CASES), "currentAuthorityCases": sorted(AUTHORITY_CASES),
            "autonomyCases": sorted(AUTONOMY_CASES), "autonomyStages": sorted(autonomy_stages()),
            "expectedCaseCount": len(REQUIRED_CASES) + len(AUTHORITY_CASES) + len(AUTONOMY_CASES)}


def witness_gaps(report):
    """Structural completeness against the current manifest; reads no files and never accepts a report."""
    from autonomy_journeys import MATRIX_QUALIFICATION, MODE_QUALIFICATION, autonomy_manifest_gaps
    manifest, gaps = witness_manifest(), []
    if not isinstance(report, dict):
        return ["No report"]
    if report.get("witnessManifest") != manifest:
        gaps.append("The run did not declare the current complete witness manifest")
    cases = report.get("cases") if isinstance(report.get("cases"), list) else []
    identities = [case.get("id") if isinstance(case, dict) else None for case in cases]
    if len(identities) != len(REQUIRED_CASES) or set(identities) != set(REQUIRED_CASES):
        gaps.append(f"Original cases are missing, duplicated or extra ({len(identities)} of {len(REQUIRED_CASES)})")
    disclosure = report.get("contextDisclosureQualification")
    disclosure = disclosure if isinstance(disclosure, dict) else {}
    authority = disclosure.get("caseIDs") if isinstance(disclosure.get("caseIDs"), list) else []
    records = disclosure.get("records") if isinstance(disclosure.get("records"), list) else []
    if (disclosure.get("status") != "passed" or disclosure.get("source") != report.get("source")
            or disclosure.get("evidenceCategory") != "deterministic-provider-protocol" or disclosure.get("liveProvider") is not False
            or sorted(authority) != manifest["currentAuthorityCases"]
            or [row.get("id") if isinstance(row, dict) else None for row in records] != authority
            or not all(isinstance(row, dict) and row.get("status") == "passed" and isinstance(row.get("record"), dict) for row in records)):
        gaps.append("The four current-authority cases were not each observed passed exactly once")
    if MODE_QUALIFICATION in report:
        gaps.append("A diagnostic mode section cannot stand in for the complete matrix")
    gaps += autonomy_manifest_gaps(report.get(MATRIX_QUALIFICATION), report.get("source"))
    stages = report.get("stages") if isinstance(report.get("stages"), list) else []
    names = [stage.get("name") if isinstance(stage, dict) else None for stage in stages]
    gaps += [f"Matrix stage {name} ran {names.count(name)} times" for name in manifest["autonomyStages"] if names.count(name) != 1]
    gaps += ["Failed stage: " + str(stage.get("name")) for stage in stages if not isinstance(stage, dict) or stage.get("exitCode") != 0]
    return gaps


def validate_complete_witness(report, root, allowed_controls):
    """The only acceptance gate for independentPackageWitness under the extended inventory."""
    root = Path(root).resolve()
    validate_report(report, root, allowed_controls)
    try:
        gaps = witness_gaps(report)
        _need(not gaps, "Incomplete complete witness: " + "; ".join(gaps[:20]))
        for row in report["contextDisclosureQualification"]["records"]:
            observed = json.loads(_retained_ref(root, row["record"]).read_text())
            _need(observed.get("id") == row["id"] and observed.get("status") == "passed", "Current-authority record differs: " + row["id"])
        from autonomy_journeys import MATRIX_QUALIFICATION, require_complete_autonomy
        require_complete_autonomy(report[MATRIX_QUALIFICATION], root, source=report["source"])
    except EvidenceError:
        raise
    except (RuntimeError, KeyError, TypeError, AttributeError, OSError, ValueError) as error:
        raise EvidenceError("Incomplete complete witness: " + str(error)) from error
