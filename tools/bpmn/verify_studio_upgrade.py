#!/usr/bin/env python3
"""Verify retained actual Studio upgrade journeys against their original whole stores.

Screenshots require the separately recorded visual inspection. Mechanical checks bind
those observations to actual API, authoring, XML, full pins and immutable SQLite data.
"""
from __future__ import annotations

import argparse
import base64
from contextlib import closing, contextmanager
import copy
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile
import xml.etree.ElementTree as ET

_spec = importlib.util.spec_from_file_location("qualify_hosting", Path(__file__).with_name("qualify-hosting.py"))
q = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(q)
BASELINE = q.REPO / "specs/1017-direct-bpmn-hosting/evidence/baseline"


@contextmanager
def original_store(name):
    with tempfile.TemporaryDirectory(prefix="elsa-studio-original-read-") as temporary:
        path = Path(temporary) / "original.sqlite"
        path.write_bytes(gzip.decompress((BASELINE / name).read_bytes()))
        with closing(sqlite3.connect(path.as_uri() + "?mode=ro&immutable=1", uri=True)) as connection:
            if connection.execute("PRAGMA integrity_check").fetchall() != [("ok",)]:
                raise ValueError("The actual original Studio input failed integrity verification.")
            yield connection


def materials(connection):
    native_only = " WHERE IsModelMaterial = 0" if "IsModelMaterial" in {row[1] for row in connection.execute("PRAGMA table_info(elsa_runtime_workflow_executable)")} else ""
    return {identity: (incarnation, hashlib.sha256(content.encode()).hexdigest())
        for identity, incarnation, content in connection.execute("SELECT Id, IncarnationId, ContentJson FROM elsa_runtime_workflow_executable" + native_only)}


def encoded(value):
    return base64.b64encode(value.encode("utf-16le")).decode()


def xml_shape(path, old_label=None, new_label=None):
    root = ET.fromstring(path.read_text())
    for element in root.iter():
        if element.get("id") == "route" and old_label is not None:
            if element.get("name") != old_label: raise ValueError("The edited XML lacks its exact gateway label.")
            element.set("name", new_label)
    def shape(element):
        return (element.tag, sorted(element.attrib.items()), (element.text or "").strip(), [shape(child) for child in element])
    return shape(root)


def verify(path: Path):
    manifest = json.loads(path.read_text()); root = path.parent
    if manifest.get("schemaVersion") not in (1, 2) or manifest.get("kind") != "actual-existing-studio-upgrade":
        raise ValueError("Unknown actual Studio upgrade evidence.")
    source = manifest["sourceRevision"]
    if len(source) != 40 or any(character not in "0123456789abcdef" for character in source):
        raise ValueError("The Studio upgrade source is not an exact commit.")
    baseline_manifest = BASELINE / "manifest.json"
    if manifest["baselineManifestSha256"] != q.digest(baseline_manifest):
        raise ValueError("The Studio upgrade does not identify its actual frozen baseline.")
    original_assets = {asset["path"]: asset["sha256"] for asset in json.loads(baseline_manifest.read_text())["assets"]}
    expected_baseline = {name: value for name, value in original_assets.items() if name.startswith("studio/") or name in ("multi-instance-and-studio/runtime.sqlite.gz", "multi-instance-and-studio/design.sqlite.gz")}
    if manifest["baselineAssets"] != expected_baseline: raise ValueError("The Studio evidence does not bind all its original captured assets.")
    for name, expected in manifest["baselineAssets"].items():
        if original_assets.get(name) != expected or q.digest(q.evidence_file(BASELINE, name)) != expected:
            raise ValueError("An original Studio asset differs from its actual capture.")
    assets = manifest["assets"]
    if len({asset["path"] for asset in assets}) != len(assets): raise ValueError("A Studio asset is declared twice.")
    declared = {asset["path"] for asset in assets}
    required = set()
    for family in ("original", "layout"):
        names = {"process.json.gz", "definition-after.json", "open-export.json", "open-export.bpmn", "edited-export.json", "edited-export.bpmn",
            "published-run-api.json", "test-run-api.json", "restarted-new-views.json"}
        for label in ("recovered", "restarted", "finished"):
            names.update({label + suffix for suffix in ("-runtime.sqlite.gz", "-design.sqlite.gz", "-frozen-contract.json", "-legacy-views.json")})
        for label in ("open", "published", "published-run", "test-run", "legacy-run", "restarted-test-run" if family == "original" else "restarted-published-run"):
            names.update({label + ".jpg", label + "-dom.txt"})
        required.update(family + "/" + name for name in names)
    if declared != required: raise ValueError("The actual Studio bundle has missing or unexpected required assets.")
    for asset in assets:
        if q.digest(q.evidence_file(root, asset["path"])) != asset["sha256"]:
            raise ValueError("An actual Studio evidence asset differs.")
        if asset["path"].endswith(".json.gz"):
            decoded = gzip.decompress(q.evidence_file(root, asset["path"]).read_bytes())
            if hashlib.sha256(decoded).hexdigest() != asset["decodedSha256"] or len(decoded) != asset["decodedBytes"]:
                raise ValueError("The decoded Studio process receipt differs.")
    if {case["family"] for case in manifest["cases"]} != {"original", "layout"} or len(manifest["cases"]) != 2:
        raise ValueError("Both actual saved Studio families are required.")
    counts = []; original_run_observations = 0
    for case in manifest["cases"]:
        family = case["family"]; directory = family
        def read(name):
            if name == "process.json": name += ".gz"
            key = directory + "/" + name
            if key not in declared: raise ValueError("A required Studio observation is undeclared.")
            file = q.evidence_file(root, key)
            return json.loads(gzip.decompress(file.read_bytes()) if name.endswith(".gz") else file.read_text())
        process = read("process.json")
        if process["family"] != family or process["sourceRevision"] != source:
            raise ValueError("The actual Studio process source or family differs.")
        for product_set in (process["hostProducts"], process["studioProducts"]):
            if product_set["sourceCommit"] != source or product_set["metadataOnly"] is not True or not product_set["actualProductVersions"] or any(
                    not product["productVersion"].endswith("+" + source) for product in product_set["actualProductVersions"]):
                raise ValueError("The actual Studio and Workbench products lack exact-source provenance.")
        libraries = process["hostProducts"]["releasedLibraryProducts"]
        if {product["name"] for product in libraries} != {"Bpmn.Model.dll", "Bpmn.Semantics.dll", "Bpmn.Interchange.dll"} or any(not product["productVersion"].startswith("0.5.0+") for product in libraries):
            raise ValueError("The Studio host does not identify the released BPMN 0.5.0 libraries.")
        roles = [item["role"] for item in process["processes"]]
        if sorted(roles) != ["studio", "workbench", "workbench"] or len({item["processId"] for item in process["processes"]}) != 3:
            raise ValueError("The Studio evidence lacks its distinct actual restart.")
        if len({item["controllerProcessId"] for item in process["processes"]}) != 1 or not process["browserAssets"]:
            raise ValueError("The owned Studio process or browser bundle provenance is absent.")
        if manifest["schemaVersion"] == 2 and any(not item.get("started") or item.get("executable") != "dotnet" for item in process["processes"]):
            raise ValueError("The provider-backed Studio capture lacks its owned process identities.")
        baseline_directory = "studio" if family == "original" else "multi-instance-and-studio"
        expected_inputs = {f"{baseline_directory}/{name}.sqlite.gz": original_assets[f"{baseline_directory}/{name}.sqlite.gz"] for name in ("runtime", "design")}
        if process["baselineAssets"] != expected_inputs: raise ValueError("The Studio process used a different coupled whole store.")
        baseline_export = "studio/export" + ("-layout" if family == "layout" else "")
        before = json.loads((BASELINE / (baseline_export + ".json")).read_text())
        if read("open-export.json") != before or (root / directory / "open-export.bpmn").read_bytes() != (BASELINE / (baseline_export + ".bpmn")).read_bytes():
            raise ValueError("Opening the actual saved Studio definition changed its authoring, layout or XML.")
        expected = copy.deepcopy(before)
        route = next(element for element in expected["state"]["rootActivity"]["structure"]["payload"]["elements"] if element["elementId"] == "route")
        route["name"] = "Upgraded route " + family
        if read("edited-export.json") != expected:
            raise ValueError("The Studio edit changed more than the authorized gateway label.")
        if xml_shape(root / directory / "open-export.bpmn") != xml_shape(root / directory / "edited-export.bpmn", "Upgraded route " + family, "Reviewed default route"):
            raise ValueError("The Studio XML edit changed a supported semantic or diagram boundary.")
        legacy = {"14iZvo3Gbql": json.loads((BASELINE / "studio/run-api.json").read_text())}
        if family == "layout": legacy["14iaXNhDEFV"] = json.loads((BASELINE / "studio/layout-run-api.json").read_text())
        with original_store(baseline_directory + "/runtime.sqlite.gz") as connection:
            retained = materials(connection)
            migrations = set(connection.execute("SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory_ElsaRuntime"))
            original_pins = {identity: json.loads(connection.execute("SELECT ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId = ?", (encoded(identity),)).fetchone()[0])["pinnedExecutable"] for identity in legacy}
        counts.append(len(retained))
        expected_version = "3.0.0" if family == "original" else "4.0.0"
        if case["publishedVersion"] != expected_version or case["screenshotsVisuallyInspected"] is not True:
            raise ValueError("The actual publication or visual Studio inspection is missing.")
        new_views = {case["publishedRunId"]: read("published-run-api.json"), case["testRunId"]: read("test-run-api.json")}
        for identity, view in new_views.items():
            instance = view["instance"]; published = identity == case["publishedRunId"]
            if (instance["workflowExecutionId"] != identity or instance["definitionId"] != before["definitionId"] or instance["status"] != "Completed" or
                    instance["runKind"] != ("PublishedRun" if published else "TestRun") or instance["artifactVersion"] != (expected_version if published else "draft") or
                    instance["incidentCount"] != 0 or instance["activeIncidentCount"] != 0 or view["incidents"] or len(view["activities"]) != 2 or any(activity["status"] != "Completed" for activity in view["activities"])):
                raise ValueError("The actual Studio run lacks its truthful completed kind, pin or native activity results.")
        if read("restarted-new-views.json") != new_views:
            raise ValueError("A new Studio run changed its full view after actual restart.")
        # Test Run inspection projects its retained draft source. An identical body
        # reuses the immutable executable material's existing publication pin; the
        # captured legacy reader behaved the same way. Check both boundaries exactly.
        published_instance = new_views[case["publishedRunId"]]["instance"]
        published_pin = {field: encoded(published_instance[field]) for field in ("artifactId", "artifactHash", "artifactVersion", "definitionId", "definitionVersionId")}
        if any(new_views[case["testRunId"]]["instance"][field] != published_instance[field] for field in ("artifactId", "artifactHash", "definitionId")):
            raise ValueError("The Test Run does not identify the same edited executable body.")
        if set(process["stages"]) != {"recovered", "restarted", "finished"}: raise ValueError("The actual Studio stages differ.")
        for label, stores in process["stages"].items():
            if read(label + "-legacy-views.json") != legacy:
                raise ValueError("An actual original Studio run changed after upgrade or restart.")
            original_run_observations += len(legacy)
            for name in ("runtime", "design"):
                if directory + "/" + stores[name]["file"] not in declared: raise ValueError("An actual Studio whole store is undeclared.")
                if manifest["schemaVersion"] == 2:
                    snapshot = stores[name]; observer = snapshot.get("observer", {})
                    if (snapshot.get("captureCut") != "same-owned-workbench-paused-across-provider-backups" or
                            observer.get("readOnly") is not True or observer.get("snapshotSha256") != snapshot["databaseSha256"]):
                        raise ValueError("The Studio snapshot lacks its quiesced read-only provider receipt.")
                with q.checked_upgrade_snapshot(root, directory, stores[name]) as connection:
                    if name == "runtime":
                        current = materials(connection)
                        if any(current.get(identity) != data for identity, data in retained.items()) or not migrations.issubset(set(connection.execute("SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory_ElsaRuntime"))):
                            raise ValueError("The Studio upgrade changed original material/incarnations or migration history.")
                        for identity, view in {**legacy, **(new_views if label != "recovered" else {})}.items():
                            key = encoded(identity)
                            rows = connection.execute("SELECT Status, ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId = ?", (key,)).fetchall()
                            pin = original_pins[identity] if identity in legacy else published_pin
                            if len(rows) != 1 or rows[0][0] != 3 or json.loads(rows[0][1])["pinnedExecutable"] != pin:
                                raise ValueError("A physical Studio run lost its exact complete pin or terminal state.")
                            if connection.execute("SELECT COUNT(*) FROM elsa_runtime_bookmark_state WHERE WorkflowExecutionId = ?", (identity,)).fetchone()[0] != 0:
                                raise ValueError("A completed Studio run retained an active wait.")
            frozen = read(label + "-frozen-contract.json")
            if frozen["sourceRevision"] != source or frozen["oracleSha256"] != q.FROZEN or frozen["matchesFrozenFingerprint"] is not True or frozen["immutableSnapshot"] is not True or frozen["closedSnapshotSha256"] != stores["design"]["databaseSha256"]:
                raise ValueError("The actual Studio Design store lacks its frozen authoring proof.")
        for name in ("open", "published", "published-run", "test-run", "legacy-run", "restarted-test-run" if family == "original" else "restarted-published-run"):
            if directory + "/" + name + ".jpg" not in declared or not (root / directory / (name + ".jpg")).read_bytes().startswith(b"\xff\xd8\xff"):
                raise ValueError("A required visually inspected Studio screenshot is absent.")
        run_dom = (root / directory / "published-run-dom.txt").read_text()
        if case["publishedRunId"] not in run_dom or "Published Run" not in run_dom or "No active incidents" not in run_dom or "Completed" not in run_dom:
            raise ValueError("The actual Studio DOM does not show the exact completed Published Run.")
        published_dom = (root / directory / "published-dom.txt").read_text()
        if "Version " + expected_version + " is active in default." not in published_dom:
            raise ValueError("The actual Studio DOM lacks its successful publication.")
    return {"actualStudioFamiliesVerified": 2, "completedNewRuns": 4, "originalRunObservations": original_run_observations,
        "wholeStoresVerified": 12, "retainedNativeMaterialCounts": counts, "frozenAuthoringPreserved": True,
        "sourceRevision": source, "existingStudioCompatibilityProved": True, "directModelStudioAcceptanceProved": False}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument("--manifest", type=Path, required=True)
    print(json.dumps(verify(parser.parse_args().manifest.resolve(strict=True)), indent=2))
