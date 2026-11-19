#!/usr/bin/env python3
"""Export referenced qualification evidence, excluding private runtime/authentication state.

This is an artifact selector, not an acceptance check. It preserves passed/failed
statuses and never promotes an incomplete report. Generated credentials, databases,
keys and host settings are deliberately not an upload artifact. The index records the
report's structural completeness against the current complete witness manifest; a report
claiming independentPackageWitness without that complete inventory is refused.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil


SENSITIVE = re.compile(r"^(?:authorization|proxy-authorization|cookie|set-cookie|password|apiKey|accessToken|refreshToken|clientSecret|privateKey|RecoveryContinuationSigningKey)$", re.I)
JWT = re.compile(rb"\beyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")
PRIVATE_NAME = re.compile(r"(?:private|storage-state|data-protection|profiles|coordinates)", re.I)
OPAQUE = {"value", "payload", "data", "content", "arguments", "rawBody", "requestBody", "request", "response",
          "result", "state", "layout", "activityPresentation", "additionalProperties", "html"}


def _need(value, message):
    if not value:
        raise ValueError(message)


def _safe_json(value):
    if isinstance(value, dict):
        for name, item in value.items():
            if SENSITIVE.fullmatch(name):
                _need(item in (None, "", "[redacted]", "********"), "Credential material is not exportable")
            _safe_json(item)
    elif isinstance(value, list):
        for item in value:
            _safe_json(item)
    elif isinstance(value, str) and value.lstrip().startswith(("{", "[")):
        # HTTP observers retain JSON bodies as strings. They do not become a
        # loophole for uploading authentication responses inside another JSON file.
        try:
            nested = json.loads(value)
        except ValueError:
            return
        _safe_json(nested)


def export_evidence(source, destination):
    source, destination = Path(source).resolve(), Path(destination)
    _need(source.is_dir() and destination.is_absolute() and not destination.exists()
          and not destination.resolve().is_relative_to(source), "Use a new absolute export directory outside the run")
    report_path = source / "report.json"
    report_bytes = report_path.read_bytes()
    _need(not JWT.search(report_bytes), "Credential material is not exportable")
    report = json.loads(report_bytes)
    _safe_json(report)
    from evidence import witness_gaps, witness_manifest
    gaps = witness_gaps(report) if isinstance(report, dict) else ["No report"]
    _need(not (isinstance(report, dict) and report.get("independentPackageWitness") is True and gaps),
          "A report claiming independentPackageWitness without the complete current inventory is not exportable as a witness")
    selected = {"report.json": report_bytes}
    pending = [report]
    visited = set()
    while pending:
        value = pending.pop()
        if isinstance(value, list):
            pending.extend(value)
        elif isinstance(value, dict):
            if isinstance(value.get("path"), str) and isinstance(value.get("sha256"), str):
                path = Path(value["path"])
                _need(path.is_absolute() and path.resolve().is_relative_to(source) and path.is_file(), "Evidence reference escaped its owning run")
                relative = path.resolve().relative_to(source).as_posix()
                _need(not PRIVATE_NAME.search(path.name) and path.name.lower() not in ("appsettings.json", "shells.json")
                      and path.suffix.lower() not in (".db", ".sqlite", ".pfx", ".key", ".pem"), "Private runtime state is not exportable")
                data = path.read_bytes()
                _need(hashlib.sha256(data).hexdigest() == value["sha256"], "Referenced evidence changed before export")
                if relative not in visited:
                    visited.add(relative)
                    if path.suffix.lower() in (".json", ".body", ".log", ".txt"):
                        _need(not JWT.search(data), "Credential material is not exportable")
                        try:
                            document = json.loads(data)
                        except (ValueError, UnicodeError):
                            document = None
                        if document is not None:
                            _safe_json(document)
                            pending.append(document)
                    selected[relative] = data
            # A module's arbitrary path/hash data is not an evidence reference.
            # Only the driver's envelope/reference fields participate in selection.
            pending.extend(item for name, item in value.items() if name not in OPAQUE)
    # All input checks precede writing any artifact. Do not leave a partial secret export.
    destination.mkdir(mode=0o700)
    try:
        for name, data in selected.items():
            path = destination / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
            path.chmod(0o600)
        index = {"status": "evidence-exported", "qualificationStatus": report.get("status"),
                 "source": report.get("source"), "files": {name: hashlib.sha256(data).hexdigest() for name, data in selected.items()},
                 "privateRuntimeStateIncluded": False, "qualificationStatusChanged": False,
                 "witnessCompleteness": {"complete": not gaps, "expectedCaseCount": witness_manifest()["expectedCaseCount"], "gaps": gaps[:100]}}
        (destination / "export-index.json").write_text(json.dumps(index, indent=2) + "\n")
    except BaseException:
        shutil.rmtree(destination)
        raise
    return index


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--destination", type=Path, required=True)
    options = parser.parse_args()
    result = export_evidence(options.source, options.destination)
    print(json.dumps({"status": result["status"], "qualificationStatus": result["qualificationStatus"], "files": len(result["files"])}))
