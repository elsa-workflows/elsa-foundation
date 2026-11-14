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
import os
from pathlib import Path
import re
import shutil
import stat


SENSITIVE = re.compile(r"^(?:authorization|proxy-authorization|cookie|set-cookie|password|apiKey|accessToken|refreshToken|clientSecret|privateKey|RecoveryContinuationSigningKey)$", re.I)
JWT = re.compile(rb"\beyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")
PRIVATE_NAME = re.compile(r"(?:private|storage-state|data-protection|profiles|coordinates)", re.I)
OPAQUE = {"value", "payload", "data", "content", "arguments", "rawBody", "requestBody", "request", "response",
          "result", "state", "layout", "activityPresentation", "additionalProperties", "html"}
MAX_PROTOCOL_SOURCE_BYTES = 268435456


class DuplicateEvidenceKey(ValueError):
    pass


def _json_document(data):
    def object_pairs(pairs):
        value = {}
        for key, item in pairs:
            if key in value:
                raise DuplicateEvidenceKey("Duplicate JSON keys are not exportable")
            value[key] = item
        return value
    return json.loads(data, object_pairs_hook=object_pairs)


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
            nested = _json_document(value)
        except DuplicateEvidenceKey:
            raise
        except ValueError:
            return
        _safe_json(nested)


def _safe_protocol_bytes(data):
    """Protocol evidence is JSONL, including bodies encoded as nested JSON strings."""
    _need(not JWT.search(data), "Credential material is not exportable")
    _need(not data or data.endswith(b"\n"), "An incomplete protocol source is not exportable")
    for line in data.splitlines():
        if line.strip():
            try:
                document = _json_document(line.decode("utf-8", "strict"))
            except DuplicateEvidenceKey:
                raise
            except (ValueError, UnicodeError) as error:
                raise ValueError("Malformed protocol evidence is not exportable") from error
            _need(isinstance(document, dict), "Malformed protocol evidence is not exportable")
            _safe_json(document)


def _canary_digests(value):
    """Use retained digest bindings; never read the private browser input."""
    digests = set()
    if isinstance(value, dict):
        candidates = [value.get("canarySha256")]
        if isinstance(value.get("canarySearch"), dict):
            candidates.append(value["canarySearch"].get("sha256"))
        digests.update(item for item in candidates if isinstance(item, str) and re.fullmatch(r"[a-f0-9]{64}", item))
        for item in value.values():
            digests.update(_canary_digests(item))
    elif isinstance(value, list):
        for item in value:
            digests.update(_canary_digests(item))
    return digests


def _check_canaries(value, digests):
    if isinstance(value, dict):
        for name, item in value.items():
            _check_canaries(name, digests); _check_canaries(item, digests)
    elif isinstance(value, list):
        for item in value:
            _check_canaries(item, digests)
    elif isinstance(value, str):
        for match in re.finditer(r"(?=([a-f0-9]{48}))", value):
            _need(hashlib.sha256(match[1].encode()).hexdigest() not in digests,
                  "Private typed canary material is not exportable")
        if value.lstrip().startswith(("{", "[")):
            try:
                nested = _json_document(value)
            except ValueError:
                return
            _check_canaries(nested, digests)


def _protocol_prefix(source, value, reference):
    """Select only an observed prefix of the actual source, after the live custody check.

    Absolute references and native device/inode identities remain untouched in the
    export. The index maps their run-relative paths to copied bytes; copying must
    never pretend that the destination has the original native file identity.
    """
    from effects_journeys import _effect_restart_trace, _protocol_source_path
    try:
        _effect_restart_trace(source, reference, value.get("commandId"))
    except (KeyError, TypeError, OSError, ValueError, RuntimeError) as error:
        raise ValueError("Actual protocol custody could not be verified before export") from error
    path = _protocol_source_path(source, value["sourcePath"])
    _need(path.suffix.lower() == ".jsonl" and not PRIVATE_NAME.search(path.name), "Private runtime state is not exportable")
    end = value["nextOffset"]
    _need(end <= MAX_PROTOCOL_SOURCE_BYTES, "Protocol source exceeds its export bound")
    with path.open("rb") as stream:
        identity = os.fstat(stream.fileno())
        _need(stat.S_ISREG(identity.st_mode) and identity.st_size >= end
              and value["sourceIdentity"] == {"device": identity.st_dev, "inode": identity.st_ino},
              "Actual protocol source changed before export")
        data = stream.read(end)
        after = os.fstat(stream.fileno())
        _need((identity.st_dev, identity.st_ino) == (after.st_dev, after.st_ino)
              and len(data) == end, "Actual protocol source changed before export")
    window = data[value["offset"]:end]
    _need(hashlib.sha256(window).hexdigest() == value["observedBytesSha256"],
          "Actual protocol source changed before export")
    _safe_protocol_bytes(data)
    return path.relative_to(source).as_posix(), data


def export_evidence(source, destination):
    source, destination = Path(source).resolve(), Path(destination)
    _need(source.is_dir() and destination.is_absolute() and not destination.exists()
          and not destination.resolve().is_relative_to(source), "Use a new absolute export directory outside the run")
    report_path = source / "report.json"
    report_bytes = report_path.read_bytes()
    _need(not JWT.search(report_bytes), "Credential material is not exportable")
    report = _json_document(report_bytes)
    _safe_json(report)
    canary_digests = _canary_digests(report)
    from evidence import witness_gaps, witness_manifest
    gaps = witness_gaps(report) if isinstance(report, dict) else ["No report"]
    _need(not (isinstance(report, dict) and report.get("independentPackageWitness") is True and gaps),
          "A report claiming independentPackageWitness without the complete current inventory is not exportable as a witness")
    selected = {"report.json": report_bytes}
    protocol_sources = {}
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
                    if path.suffix.lower() == ".jsonl":
                        _safe_protocol_bytes(data)
                    elif path.suffix.lower() in (".json", ".body", ".log", ".txt"):
                        _need(not JWT.search(data), "Credential material is not exportable")
                        try:
                            document = _json_document(data)
                        except DuplicateEvidenceKey:
                            raise
                        except (ValueError, UnicodeError):
                            document = None
                        if document is not None:
                            _safe_json(document)
                            canary_digests.update(_canary_digests(document))
                            if isinstance(document, dict) and "rawTrace" in document and any(
                                    name in document for name in ("window", "sourceIdentity", "capturedAt", "sourceSizeAtObservation")):
                                name, prefix = _protocol_prefix(source, document, value)
                                original = protocol_sources.get(name)
                                _need(original is None or original.startswith(prefix) or prefix.startswith(original),
                                      "Actual protocol source changed between retained windows")
                                if original is None or len(prefix) > len(original):
                                    protocol_sources[name] = prefix
                            pending.append(document)
                    selected[relative] = data
            # A module's arbitrary path/hash data is not an evidence reference.
            # Only the driver's envelope/reference fields participate in selection.
            pending.extend(item for name, item in value.items() if name not in OPAQUE)
    for name, prefix in protocol_sources.items():
        _need(name not in selected or selected[name].startswith(prefix), "Protocol source/reference bytes differ")
        selected.setdefault(name, prefix)
    if canary_digests:
        for name, data in selected.items():
            try:
                text = data.decode("utf-8", "strict")
            except UnicodeError:
                continue
            _check_canaries(text, canary_digests)
            if Path(name).suffix.lower() == ".jsonl":
                for line in text.splitlines():
                    if line.strip(): _check_canaries(_json_document(line), canary_digests)
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
