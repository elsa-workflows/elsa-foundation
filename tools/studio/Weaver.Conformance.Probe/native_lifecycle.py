"""Observed native package/feature transitions; no success is inferred from HTTP alone.

These are qualification-driver operations, not a package manager or contribution
registry. Feed inputs come from retained actual author graphs. Every transition
uses the composed host's native reconcile/feature endpoints and serving reads.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re
from urllib.parse import unquote, urlsplit
import zipfile

from artifacts import REPO, check, sha
from consumers import select_native_archives, stage_native_archives
from runtime import private_json

RETAINED_HEADERS = {"content-type", "content-length", "content-encoding", "cache-control", "etag", "last-modified", "retry-after", "content-range"}


class NativeObservationError(RuntimeError):
    def __init__(self, message, observation):
        super().__init__(message)
        self.observation = observation


class NativeObservations:
    def __init__(self, output):
        self.output = Path(output)
        check(self.output.is_absolute() and not self.output.resolve().is_relative_to(REPO), "Native observations require an external owned output")
        self.output.mkdir(parents=True, exist_ok=False, mode=0o700)
        self.sequence = 0
        self.observations = []

    def request(self, client, name, path, body=None, *, method="GET", headers=None):
        """Retain the actual wire response before a caller asserts status, bytes or JSON."""
        check(re.fullmatch(r"[a-zA-Z0-9_.-]+", name), "Observation name must be a plain local identifier")
        self.sequence += 1
        prefix = self.output / f"{self.sequence:04d}-{name}"
        record = {"origin": client.base, "path": path, "method": method}
        try:
            status, data, observed_headers = client.request(path, body, method=method, headers=headers)
        except Exception as error:
            # The request might have reached its owner. Never turn a transport failure into a replay.
            record.update(transportFailure=type(error).__name__)
            receipt_path = prefix.with_suffix(".json")
            private_json(receipt_path, record)
            record["receipt"] = {"path": str(receipt_path), "sha256": sha(receipt_path)}
            self.observations.append(record)
            raise NativeObservationError("No native response was observed; request was not replayed", record) from None
        raw_path = prefix.with_suffix(".body")
        raw_path.write_bytes(data)
        raw_path.chmod(0o600)
        record.update(status=status, headers={key: value for key, value in observed_headers.items() if key in RETAINED_HEADERS},
                      body={"path": str(raw_path), "sha256": sha(raw_path)})
        receipt_path = prefix.with_suffix(".json")
        private_json(receipt_path, record)
        record["receipt"] = {"path": str(receipt_path), "sha256": sha(receipt_path)}
        self.observations.append(record)
        return record, data

    def json(self, client, name, path, body=None, *, method="GET", headers=None, expected=200):
        record, data = self.request(client, name, path, body, method=method, headers=headers)
        if record["status"] != expected:
            raise NativeObservationError(f"{method} {path} returned {record['status']}; expected {expected}", record)
        try:
            value = json.loads(data)
        except (ValueError, UnicodeError):
            raise NativeObservationError("Native response was not JSON", record) from None
        return value, record

    def generation(self, client, name):
        value, record = self.json(client, name, "/__qualification/generation")
        check(isinstance(value.get("generation"), str) and bool(value["generation"]), "Native serving generation is absent")
        if "featureConfiguration" in value:
            check(value["featureConfiguration"]["rootGuardUses"] == 0, "Feature operations escaped their actual shell owner")
        return value, record

    def reconcile(self, client, name):
        value, record = self.json(client, name, "/__qualification/reconcile", {}, method="POST")
        check(value["outcomeCode"] == 0 and value.get("runResult") is not None, "Native reconciliation did not complete")
        check(not value["runResult"]["isDegraded"] and not value["runResult"]["failedPackages"], "Native reconciliation degraded or refused a package")
        return value, record

    def feature(self, client, name, feature_id, *, enabled, configuration=None):
        """Apply the actual full enabled state, preserving unrelated configuration and secret masks."""
        before, before_ref = self.generation(client, name + "-before")
        catalog, catalog_ref = self.json(client, name + "-catalog", "/modularity/features")
        targets = [item for item in catalog["features"] if item["id"] == feature_id]
        check(len(targets) == 1, "Native feature catalog has no unique target: " + feature_id)
        target = targets[0]
        if enabled:
            check(target["runtimeAvailable"], "Feature manifest does not establish a live runtime descriptor")
        check(target["enabled"] != enabled or configuration is not None and target["configuration"] != configuration,
              "Feature request would not change the selected native state")
        request = {"revision": catalog["revision"], "features": [
            {"id": item["id"], "enabled": enabled if item["id"] == feature_id else item["enabled"],
             "configuration": configuration if item["id"] == feature_id and configuration is not None else item["configuration"]}
            for item in catalog["features"] if item["enabled"] or item["id"] == feature_id]}
        applied, applied_ref = self.json(client, name + "-apply", "/modularity/features/apply", request, method="POST")
        check(applied["reloadedShellCount"] > 0, "Native feature apply did not reload a serving shell")
        after, after_ref = self.generation(client, name + "-after")
        check(after["generation"] != before["generation"], "Serving generation did not change after feature apply")
        selected = [item for item in applied["catalog"]["features"] if item["id"] == feature_id]
        check(len(selected) == 1 and selected[0]["enabled"] == enabled, "Applied catalog did not confirm the requested feature state")
        return {"before": before, "after": after, "applied": applied,
                "evidence": [before_ref["receipt"], catalog_ref["receipt"], applied_ref["receipt"], after_ref["receipt"]]}

    def assets(self, client, name, manifest, author_package, assembly_name):
        """Require actual protected JS/CSS bytes from the independently built RCL archive."""
        package_path = Path(author_package["path"])
        check(sha(package_path) == author_package["sha256"], "The independently authored RCL archive changed")
        urls = [manifest["entry"], *manifest.get("styles", [])]
        check(len(urls) >= 2 and len(set(urls)) == len(urls), "The fixture must contribute distinct actual script and style assets")
        evidence = []
        prefix = "/_content/" + assembly_name + "/"
        with zipfile.ZipFile(package_path) as archive:
            for index, url in enumerate(urls):
                parsed = urlsplit(url)
                check(not parsed.scheme and not parsed.netloc and parsed.path.startswith(prefix), "Module asset escaped its owning assembly route")
                relative = unquote(parsed.path.removeprefix(prefix))
                check(relative and not any(part in ("", ".", "..") or ":" in part or "\\" in part for part in relative.split("/")),
                      "Unsafe module asset reference")
                expected = archive.read("staticwebassets/" + relative)
                record, data = self.request(client, name + "-" + str(index), url, headers={"Accept-Encoding": "identity"})
                if record["status"] != 200 or data != expected:
                    raise NativeObservationError("Protected module asset did not match its independently authored archive", record)
                check(record["headers"].get("etag") == '"' + hashlib.sha256(expected).hexdigest() + '"', "Protected asset validator differs from the observed bytes")
                evidence.extend([record["receipt"], record["body"]])
        return evidence


def add_resource_archives(run, profile_name, author_name):
    """Add only explicitly resolved archive bytes; identify the delta from the baseline feed."""
    author = run.report["authors"][author_name]
    profile = run.profiles[profile_name]
    baseline = {path.name: sha(path) for path in profile["runtimeFeed"].glob("*.nupkg")}
    selected = select_native_archives(run, profile_name, [author["packed"]["package"]], author)
    stage_native_archives(run, profile_name, selected)
    added = [item for item in profile["runtimeArchiveInputs"] if Path(item["path"]).name not in baseline]
    check(bool(added), "Native feed already contained the resource archives at baseline")
    check(all(sha(profile["runtimeFeed"] / name) == digest for name, digest in baseline.items()), "Adding the module changed a baseline archive")
    return {"added": added, "baseline": baseline, "selected": selected}


def remove_resource_archives(run, profile_name, delta):
    """Remove only the added owned feed copies; preserve protocol/shared dependencies and originals."""
    feed = run.profiles[profile_name]["runtimeFeed"].resolve()
    check(feed.is_relative_to(run.root.resolve()), "Native feed escaped the owned output")
    for item in delta["added"]:
        path = Path(item["path"])
        check(path.resolve().parent == feed and sha(path) == item["sha256"], "Refuse to remove a different or changed native archive")
        check(Path(item["sourceArchive"]).resolve() != path.resolve(), "Never remove the retained author archive")
        path.unlink()
    check(all(sha(feed / name) == digest for name, digest in delta["baseline"].items()), "Removing the module changed a baseline archive")


def restore_resource_archives(run, profile_name, delta):
    """Restore exactly the retained original bytes, then let the caller observe native reconciliation."""
    stage_native_archives(run, profile_name, [{"path": item["sourceArchive"], "sha256": item["sha256"], "identity": item["identity"]}
                                            for item in delta["added"]])
