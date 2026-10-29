#!/usr/bin/env python3
"""Capture legacy BPMN inputs and qualify their recovery on the changed production host.

Uses public APIs to park partial forks in the legacy BPMN activity and native parents.
Capture and upgrade phases retain separate source identities and immutable inputs.
The resulting manifest contains only relative evidence paths and binary/source identities.
"""
from __future__ import annotations

import argparse
import base64
from contextlib import closing, contextmanager
from datetime import datetime, timezone
import gzip
import hashlib
import http.cookiejar
import json
import os
from pathlib import Path
import re
import shlex
import signal
import sqlite3
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

REPO = Path(__file__).resolve().parents[2]
FROZEN = "41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92"


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        h = hashlib.sha256()
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
        return h.hexdigest()


def write_json(path: Path, value) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n")


def evidence_file(root: Path, name: str) -> Path:
    value = Path(name)
    if value.is_absolute() or ".." in value.parts or not value.parts:
        raise ValueError("Evidence paths must be relative and stay within their bundle.")
    candidate = (root / value).resolve()
    candidate.relative_to(root.resolve())
    if not candidate.is_file():
        raise ValueError("A declared evidence asset is missing.")
    return candidate


def inside(root: Path, path: Path) -> Path:
    resolved = path.resolve(strict=True)
    resolved.relative_to(root.resolve(strict=True))
    return resolved


def process_field(pid: int, field: str) -> str:
    return subprocess.check_output(
        ["ps", "-ww", "-p", str(pid), "-o", field + "="], text=True,
        env={**os.environ, "LC_ALL": "C"}, timeout=5).strip()


def has_path(arguments: list[str], expected: Path) -> bool:
    return any(Path(value).is_absolute() and Path(value).resolve() == expected.resolve() for value in arguments)


def verify_owned(args, owner: dict) -> dict:
    pid = int(owner["processId"])
    controller = int(owner["controllerProcessId"])
    if pid <= 1 or controller <= 1 or pid == os.getpid():
        raise ValueError("Invalid owned process identity.")
    if int(process_field(pid, "ppid")) != controller:
        raise ValueError("The recorded controller no longer owns this host.")
    started = process_field(pid, "lstart")
    epoch = int(datetime.strptime(started, "%a %b %d %H:%M:%S %Y").astimezone(timezone.utc).timestamp())
    if epoch != int(owner["startEpochSeconds"]):
        raise ValueError("The host PID was reused or its start identity changed.")
    command = shlex.split(process_field(pid, "command"))
    if not has_path(command, args.assembly) or not has_path(command, args.content_root):
        raise ValueError("The actual host executable/content root differs from the owned manifest.")
    parent = shlex.split(process_field(controller, "command"))
    if not any(Path(part).name == "run_owned_broadcast_probe.py" for part in parent):
        raise ValueError("The parent is not the supported explicit owned-host controller.")
    if not has_path(parent, args.owner_manifest) or not has_path(parent, args.content_root) or args.base_url not in parent:
        raise ValueError("The controller configuration differs from this capture request.")
    return {"processId": pid, "controllerProcessId": controller, "startEpochSeconds": epoch,
            "assemblyName": args.assembly.name, "assemblySha256": digest(args.assembly)}


@contextmanager
def quiesce_owned(args, owner: dict):
    if os.name != "posix":
        raise ValueError("This capture cut requires the qualified POSIX owned-host controller.")
    observed = verify_owned(args, owner)
    pid = observed["processId"]
    stopped = False
    try:
        os.kill(pid, signal.SIGSTOP)
        stopped = True
        deadline = time.monotonic() + 5
        while "T" not in process_field(pid, "stat"):
            if time.monotonic() >= deadline:
                raise TimeoutError("The owned host did not reach the stopped capture cut.")
            time.sleep(.02)
        yield observed
    finally:
        if stopped:
            # Check incarnation/executable again; never resume a reused or unrelated PID.
            verify_owned(args, owner)
            os.kill(pid, signal.SIGCONT)


def backup_sqlite(source: Path, target: Path) -> None:
    if target.exists():
        raise ValueError("Refusing to overwrite a capture database.")
    deadline = time.monotonic() + 30
    def progress(_status, _remaining, _total):
        if time.monotonic() > deadline:
            raise TimeoutError("Consistent SQLite backup exceeded its bound.")
    with closing(sqlite3.connect(source.as_uri() + "?mode=ro", uri=True, timeout=5)) as reader:
        reader.execute("pragma query_only=ON")
        with closing(sqlite3.connect(target)) as writer:
            reader.backup(writer, pages=256, progress=progress)
    with closing(sqlite3.connect(target.as_uri() + "?mode=ro&immutable=1", uri=True)) as check:
        if check.execute("pragma quick_check").fetchall() != [("ok",)]:
            raise ValueError("A captured whole database failed its integrity check.")


class Api:
    def __init__(self, base: str):
        parsed = urllib.parse.urlsplit(base)
        if parsed.scheme != "http" or parsed.hostname not in ("127.0.0.1", "localhost", "::1") or parsed.username or parsed.password or parsed.path not in ("", "/") or parsed.query or parsed.fragment:
            raise ValueError("Capture requires an explicit loopback owned-host URL.")
        self.base = base.rstrip("/")
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def request(self, path: str, value=None):
        data = None if value is None else json.dumps(value).encode()
        request = urllib.request.Request(self.base + path, data=data, headers={"Content-Type": "application/json"})
        try:
            with self.opener.open(request, timeout=30) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            # Do not persist login payloads, cookies or arbitrary error bodies.
            raise RuntimeError(f"Owned-host request {path} returned HTTP {error.code}.") from None

    def login(self, username: str, password: str):
        self.request("/health/ready")
        self.request("/_elsa/identity/login", {"username": username, "password": password})

    def activity(self, key: str) -> str:
        result = self.request("/design/activities/definitions?" + urllib.parse.urlencode({"search": key}))
        hits = [v for v in result["items"] if v["definition"]["activityTypeKey"] == key]
        if len(hits) != 1:
            raise ValueError("Expected one actual visible activity definition: " + key)
        return hits[0]["definition"]["headVersionId"]

    def view(self, execution: str):
        return self.request("/runtime/workflows/instances/" + urllib.parse.quote(execution, safe=""))

    def send(self, event: str, execution: str):
        result = self.request("/runtime/workflows/stimuli", {
            "stimulusType": "Event", "stimulusHash": "sha256:" + hashlib.sha256(event.encode()).hexdigest(),
            "mode": "ResumeOnly", "correlationId": execution, "input": {"eventName": event}})
        matches = [v for v in result.get("resumes", []) if v.get("workflowExecutionId") == execution]
        if result["startedCount"] != 0 or result["resumedCount"] != 1 or len(matches) != 1:
            raise ValueError("The normal stimulus did not address exactly the captured execution.")
        return result


def literal(key: str, value) -> dict:
    return {"referenceKey": key, "value": {"value": value, "expressionType": "Literal"}}


def node(identity: str, version: str, inputs=None, structure=None) -> dict:
    result = {"nodeId": identity, "activityVersionId": version, "inputs": inputs or [], "outputs": []}
    if structure is not None:
        result["structure"] = structure
    return result


def event_node(identity: str, version: str, event: str) -> dict:
    correlation = {"referenceKey": "CorrelationId", "value": {"expressionType": "JavaScript", "value": {
        "language": "JavaScript", "source": "args.instance", "capabilityProfile": "binding-pure-v1",
        "resultType": {"alias": "String", "collectionKind": "single"},
        "parameters": {"instance": {"kind": "WorkflowInstance", "member": "instanceId"}}, "options": {}}}}
    return node(identity, version, [literal("EventName", event), literal("CanStartWorkflow", False), correlation])


def partial_fork(versions: dict, events: dict) -> dict:
    elements = [{"elementId": name, "elementType": kind} for name, kind in (
        ("start", "startEvent"), ("fork", "parallelGateway"), ("a", "task"),
        ("b", "task"), ("join", "parallelGateway"), ("end", "endEvent"))]
    for element in elements:
        if element["elementId"] in ("a", "b"):
            element["childNodeId"] = "wait-" + element["elementId"]
    flows = [{"flowId": f"f{index}", "sourceRef": source, "targetRef": target}
             for index, (source, target) in enumerate((("start", "fork"), ("fork", "a"),
                 ("fork", "b"), ("a", "join"), ("b", "join"), ("join", "end")))]
    return node("legacy-bpmn", versions["bpmn"], structure={"kind": "elsa.bpmn.structure", "schemaVersion": "1.0.0", "payload": {
        "activities": [event_node("wait-" + key, versions["event"], events[key]) for key in ("a", "b")],
        "elements": elements, "sequenceFlows": flows}})


def wrap_parent(kind: str, process: dict, versions: dict) -> dict:
    if kind == "LegacyRoot":
        return process
    after = node("after-bpmn", versions["write"], [literal("text", "Captured parent continued after BPMN.")])
    if kind == "Sequence":
        structure = {"kind": "elsa.sequence.structure", "schemaVersion": "1.0.0", "payload": {"activities": [process, after]}}
    elif kind == "Flowchart":
        structure = {"kind": "elsa.flowchart.structure", "schemaVersion": "1.0.0", "payload": {
            "startNodeId": process["nodeId"], "activities": [process, after], "nodeMetadata": {}, "connectionMetadata": {},
            "connections": [{"id": "after", "source": {"nodeId": process["nodeId"], "port": "Done"}, "target": {"nodeId": after["nodeId"]}}]}}
    elif kind == "CompensationScope":
        structure = {"kind": "elsa.compensation-scope.structure", "schemaVersion": "1.0.0", "payload": {
            "steps": [{"name": "process", "activity": process}, {"name": "after", "activity": after}]}}
    else:
        raise ValueError("Unknown baseline parent.")
    return node("native-parent", versions[kind], structure=structure)


def observe_partial(view: dict, first_done: bool) -> dict:
    if view["instance"]["status"] != "Running" or view.get("incidents"):
        raise ValueError("The actual baseline run is not incident-free and held.")
    observed = {}
    for identity in ("wait-a", "wait-b"):
        matches = [a for a in view["activities"] if a["executableNodeId"] == identity]
        expected = "Completed" if identity == "wait-a" and first_done else "Suspended"
        if len(matches) != 1 or matches[0]["status"] != expected:
            raise ValueError("The actual partial fork does not retain its exact completed/held branches.")
        observed[identity] = {k: matches[0].get(k) for k in ("activityExecutionId", "executableNodeId", "status", "outcomeNames")}
    if any(a["executableNodeId"] == "after-bpmn" for a in view["activities"]):
        raise ValueError("The native successor ran before the held BPMN child completed.")
    return {"workflowExecutionId": view["instance"]["workflowExecutionId"], "artifactId": view["instance"]["artifactId"], "status": "Running", "branches": observed}


def await_partial(api: Api, execution: str, first_done: bool) -> dict:
    deadline = time.monotonic() + 60
    while True:
        view = api.view(execution)
        try:
            observe_partial(view, first_done)
            return view
        except ValueError:
            if view["instance"]["status"] in ("Completed", "Faulted", "Cancelled") or time.monotonic() >= deadline:
                raise
            time.sleep(.1)


def observe_case(case: dict, view: dict) -> dict:
    observed = observe_partial(view, True)
    if (observed["workflowExecutionId"] != case["workflowExecutionId"] or
            observed["artifactId"] != case["publication"]["artifactId"] or
            view.get("persistenceMode") != case["persistenceMode"]):
        raise ValueError("The observed execution, immutable pin or persistence mode differs from its case.")
    return observed


def create_cases(api: Api, output: Path) -> list:
    keys = {"bpmn": "Elsa.Activities.Bpmn.Activities.BpmnProcess", "event": "Elsa.Activities.Primitives.Activities.Event",
            "write": "Elsa.Activities.Primitives.Activities.WriteLine", "Sequence": "Elsa.Activities.Sequence.Activities.Sequence",
            "Flowchart": "Elsa.Activities.Flowchart.Activities.Flowchart", "CompensationScope": "Elsa.Activities.Compensation.Activities.CompensationScope"}
    versions = {name: api.activity(key) for name, key in keys.items()}
    cases = []
    for mode in ("Balanced", "Durable"):
        for parent in ("LegacyRoot", "Sequence", "Flowchart", "CompensationScope"):
            token = uuid.uuid4().hex
            events = {key: f"hosting-baseline-{key}-{token}" for key in ("a", "b")}
            root = wrap_parent(parent, partial_fork(versions, events), versions)
            definition = api.request("/design/workflows/definitions/submit", {"name": f"HostingBaseline-{parent}-{mode}-{token}",
                "description": "Owned actual legacy-writer capture; not direct-model hosting.", "state": {
                    "variables": [], "inputs": [], "outputs": [], "rootActivity": root, "strategyOptions": {"persistenceMode": mode}}})
            published = api.request("/publishing/workflows/" + definition["version"]["id"] + "/publish", {})
            run = api.request("/runtime/workflows/executables/" + published["artifactId"] + "/execute", {"sourceReferenceId": published["sourceReferenceId"]})
            case = {"family": "legacy-partial-fork-native-parent", "parent": parent, "persistenceMode": mode,
                    "definition": definition, "publication": published, "workflowExecutionId": run["workflowExecutionId"], "events": events}
            cases.append(case)
            write_json(output / "cases-journal.json", cases)
            case["bothHeld"] = await_partial(api, run["workflowExecutionId"], False)
            case["firstDelivery"] = api.send(events["a"], run["workflowExecutionId"])
            case["heldView"] = await_partial(api, run["workflowExecutionId"], True)
            case["observation"] = observe_case(case, case["heldView"])
            write_json(output / "cases-journal.json", cases)
    return cases


def verify_capture(path: Path) -> dict:
    manifest = json.loads(path.read_text())
    if manifest.get("schemaVersion") != 1 or manifest.get("kind") != "actual-legacy-host-capture":
        raise ValueError("Unknown capture manifest.")
    if not re.fullmatch(r"[0-9a-f]{40}", manifest.get("executingSourceCommit", "")):
        raise ValueError("Capture has no exact executing source identity.")
    root = path.parent
    declared = {a["path"]: a for a in manifest["assets"]}
    if len(declared) != len(manifest["assets"]):
        raise ValueError("Capture assets repeat a relative path.")
    required = {manifest["caseReceipt"]: "actual-api-views", manifest["frozenContractReceipt"]: "actual-frozen-authoring-comparison",
                "runtime.sqlite": "whole-sqlite-store", "design.sqlite": "whole-sqlite-store", "product-versions.json": "matched-binary-source-binding"}
    if any(name not in declared or declared[name]["role"] != role for name, role in required.items()):
        raise ValueError("Capture omits a required hashed asset or its evidence role.")
    for asset in manifest["assets"]:
        file = evidence_file(root, asset["path"])
        if digest(file) != asset["sha256"]:
            raise ValueError("A capture asset differs from its retained hash.")
        if asset["role"] == "whole-sqlite-store":
            if any(Path(str(file) + suffix).exists() for suffix in ("-wal", "-shm", "-journal")):
                raise ValueError("A closed capture has live database sidecars.")
            with closing(sqlite3.connect(file.as_uri() + "?mode=ro&immutable=1", uri=True)) as check:
                if check.execute("pragma quick_check").fetchall() != [("ok",)]:
                    raise ValueError("A capture database is not intact.")
    fingerprint = json.loads(evidence_file(root, manifest["frozenContractReceipt"]).read_text())
    if (manifest.get("frozenAuthoringFingerprint") != FROZEN or fingerprint.get("oracleSha256") != FROZEN or
            fingerprint.get("matchesFrozenFingerprint") is not True or fingerprint.get("immutableSnapshot") is not True or
            fingerprint.get("sourceRevision") != manifest["executingSourceCommit"] or
            fingerprint.get("closedSnapshotSha256") != declared["design.sqlite"]["sha256"]):
        raise ValueError("Capture does not retain frozen authoring verification.")
    cases = json.loads(evidence_file(root, manifest["caseReceipt"]).read_text())
    if len(cases) != 8 or len({c["workflowExecutionId"] for c in cases}) != 8:
        raise ValueError("Expected eight distinct actual baseline executions.")
    if {(c["parent"], c["persistenceMode"]) for c in cases} != {
            (parent, mode) for parent in ("LegacyRoot", "Sequence", "Flowchart", "CompensationScope") for mode in ("Balanced", "Durable")}:
        raise ValueError("The actual parent/persistence-mode capture matrix is incomplete.")
    for case in cases:
        if observe_case(case, case["heldView"]) != case["observation"]:
            raise ValueError("A declared baseline observation differs from its actual API view.")
    return {"captureAssetsVerified": len(manifest["assets"]), "actualHeldCases": len(cases),
            "executingSourceCommit": manifest["executingSourceCommit"], "upgradeRecoveryProved": False,
            "allWriterCaptureFamiliesComplete": False}


def capture(args) -> dict:
    args.assembly = args.assembly.resolve(strict=True)
    args.content_root = args.content_root.resolve(strict=True)
    args.owner_manifest = args.owner_manifest.resolve(strict=True)
    owner = json.loads(args.owner_manifest.read_text())
    observed = verify_owned(args, owner)
    provenance = json.loads(args.product_versions.read_text())
    tool_commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True, timeout=5).strip()
    subprocess.run(["git", "ls-files", "--error-unmatch", "tools/bpmn/qualify-hosting.py"], cwd=REPO,
        check=True, capture_output=True, timeout=5)
    subprocess.run(["git", "diff", "--quiet", "HEAD", "--", "tools/bpmn/qualify-hosting.py"], cwd=REPO,
        check=True, capture_output=True, timeout=5)
    source = provenance["sourceCommit"]
    if not re.fullmatch(r"[0-9a-f]{40}", source):
        raise ValueError("Product provenance requires a full source SHA.")
    products = provenance["actualProductVersions"]
    if not products or args.assembly.name not in {p["name"] for p in products}:
        raise ValueError("Product provenance does not include the actual host assembly.")
    for product in products:
        if Path(product["name"]).name != product["name"] or not product["productVersion"].endswith("+" + source):
            raise ValueError("Product source binding is inconsistent.")
        if digest(args.assembly.parent / product["name"]) != product["sha256"]:
            raise ValueError("An actual executing product differs from its source-bound binary hash.")
    api = Api(args.base_url)
    api.login(os.environ.get("ELSA_CAPTURE_USERNAME", "admin"), os.environ.get("ELSA_CAPTURE_PASSWORD", "Password123!"))
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    try:
        cases = create_cases(api, output)
        write_json(output / "cases.json", cases)
        assets = [{"path": "cases.json", "sha256": digest(output / "cases.json"), "role": "actual-api-views"}]
        write_json(output / "product-versions.json", provenance)
        assets.append({"path": "product-versions.json", "sha256": digest(output / "product-versions.json"), "role": "matched-binary-source-binding"})
        with quiesce_owned(args, owner):
            for name in ("runtime", "design"):
                database = inside(args.content_root, args.content_root / (name + ".db"))
                target = output / (name + ".sqlite")
                backup_sqlite(database, target)
                assets.append({"path": target.name, "sha256": digest(target), "role": "whole-sqlite-store"})
        for case in cases:
            if observe_case(case, api.view(case["workflowExecutionId"])) != case["observation"]:
                raise ValueError("The held execution changed across the consistent capture cut.")
        verifier = REPO / "e2e-tests/bpmn/verify_published_contracts.py"
        subprocess.run([sys.executable, str(verifier), "--database", str(output / "design.sqlite"),
            "--source-revision", source, "--closed-snapshot", "--output", str(output / "frozen-contract.json")],
            check=True, capture_output=True, text=True, timeout=60)
        assets.append({"path": "frozen-contract.json", "sha256": digest(output / "frozen-contract.json"), "role": "actual-frozen-authoring-comparison"})
        manifest = {"schemaVersion": 1, "kind": "actual-legacy-host-capture", "executingSourceCommit": source,
            "captureToolSourceCommit": tool_commit, "captureRunnerSha256": digest(Path(__file__)),
            "capturedAt": datetime.now(timezone.utc).isoformat(), "ownedHost": observed,
            "frozenAuthoringFingerprint": FROZEN, "frozenContractReceipt": "frozen-contract.json", "caseReceipt": "cases.json",
            "captureCut": "same-owned-host-stopped-across-whole-store-backups", "assets": assets,
            "coverage": ["actual-partial-fork", "legacy-bpmn-root", "native-Sequence", "native-Flowchart", "native-CompensationScope", "Balanced", "Durable"],
            "limits": ["Not direct-model hosting or upgraded-host recovery proof.",
                "Does not complete the writer map: pending callback/cohort/control/MI/compensation/Studio cuts need their own captures.",
                "Only the explicitly owned POSIX/SQLite host profile is exercised; no distributed or other-provider claim."]}
        write_json(output / "manifest.json", manifest)
        result = verify_capture(output / "manifest.json")
        (output / "cases-journal.json").unlink()
        return result
    except Exception as error:
        write_json(output / "capture-failure.json", {"passed": False, "failureType": type(error).__name__,
            "detail": str(error), "executingSourceCommit": source, "positiveUpgradeProof": False})
        raise


def verify_upgrade(path: Path) -> dict:
    manifest = json.loads(path.read_text())
    if manifest.get("kind") == "actual-legacy-native-parent-upgrade":
        return verify_native_parent_upgrade(path)
    if manifest.get("kind") == "actual-legacy-called-ack-upgrade":
        return verify_called_ack_upgrade(path)
    if manifest.get("kind") == "actual-legacy-broadcast-upgrade":
        return verify_broadcast_upgrade(path)
    if manifest.get("schemaVersion") != 1 or manifest.get("kind") != "actual-legacy-partial-fork-upgrade":
        raise ValueError("Unknown upgrade manifest.")
    root = path.parent
    assets = {asset["path"]: asset for asset in manifest["assets"]}
    if len(assets) != len(manifest["assets"]) or set(assets) != {
            "legacy-partial-forks/process.json", "legacy-partial-forks/upgraded-runtime.sqlite.gz",
            "legacy-partial-forks/upgraded-design.sqlite.gz", "frozen-contract.json"}:
        raise ValueError("The upgrade evidence asset set is incomplete or duplicated.")
    for asset in assets.values():
        if digest(evidence_file(root, asset["path"])) != asset["sha256"]:
            raise ValueError("An upgrade asset differs from its retained hash.")
    process = json.loads(evidence_file(root, "legacy-partial-forks/process.json").read_text())
    source = process.get("sourceRevision", "")
    if (process.get("kind") != manifest["kind"] or process.get("formatVersion") != 1 or
            not re.fullmatch(r"[0-9a-f]{40}", source) or source != manifest.get("sourceRevision") or
            not re.fullmatch(r"[0-9a-f]{40}", process.get("baselineSourceRevision", "")) or
            source == process["baselineSourceRevision"]):
        raise ValueError("The upgrade does not identify distinct exact source revisions.")
    if (process.get("baselineManifestSha256") != manifest.get("baselineManifestSha256") or
            process["baselineSourceRevision"] != manifest.get("baselineSourceRevision") or
            process.get("baselineAssets") != manifest.get("baselineAssets")):
        raise ValueError("The recovered source observations do not match the declared baseline.")
    if not process.get("binaries") or not any(binary["file"] == "Elsa.Workbench.dll" and
            binary["productVersion"].endswith("+" + source) for binary in process["binaries"]):
        raise ValueError("The upgrade has no source-bound production Workbench.")
    observations = process["observations"]
    if len(observations) != 8 or {(case["parent"], case["persistenceMode"]) for case in observations} != {
            (parent, mode) for parent in ("LegacyRoot", "Sequence", "Flowchart", "CompensationScope") for mode in ("Balanced", "Durable")}:
        raise ValueError("The actual upgraded parent/persistence-mode matrix is incomplete.")
    if len({case["executionId"] for case in observations}) != 8:
        raise ValueError("Upgraded executions are not distinct.")
    for case in observations:
        recovered, completed = case["recovered"], case["completed"]
        if observe_partial(recovered, True)["workflowExecutionId"] != case["executionId"]:
            raise ValueError("The recovered wait does not identify its actual execution.")
        if (completed["instance"]["status"] != "Completed" or completed["incidents"] or
                case["delivery"]["startedCount"] != 0 or case["delivery"]["resumedCount"] != 1):
            raise ValueError("An upgraded execution did not finish through its one normal resume.")
        for key in ("workflowExecutionId", "artifactId", "artifactHash", "artifactVersion", "definitionId", "definitionVersionId"):
            if recovered["instance"][key] != completed["instance"][key]:
                raise ValueError("An upgraded execution changed its immutable pin.")
        for node in ("wait-a", "wait-b"):
            before = [activity for activity in recovered["activities"] if activity["executableNodeId"] == node]
            after = [activity for activity in completed["activities"] if activity["executableNodeId"] == node]
            if (len(before) != 1 or len(after) != 1 or after[0]["activityExecutionId"] != before[0]["activityExecutionId"] or
                    after[0]["status"] != "Completed" or after[0]["outcomeNames"] != ["Done"]):
                raise ValueError("An upgraded branch lost or repeated its exact continuation.")
        successors = [activity for activity in completed["activities"] if activity["executableNodeId"] == "after-bpmn"]
        if case["parent"] != "LegacyRoot" and (len(successors) != 1 or successors[0]["status"] != "Completed"):
            raise ValueError("An upgraded native parent did not continue exactly once.")
    with tempfile.TemporaryDirectory(prefix="elsa-upgrade-verify-") as temporary:
        for key, name in (("snapshot", "runtime"), ("designSnapshot", "design")):
            snapshot = process[key]
            compressed = evidence_file(root, "legacy-partial-forks/" + snapshot["file"])
            if digest(compressed) != snapshot["gzipSha256"]:
                raise ValueError("An upgraded whole-store snapshot differs.")
            raw = gzip.decompress(compressed.read_bytes())
            if hashlib.sha256(raw).hexdigest() != snapshot["databaseSha256"]:
                raise ValueError("An upgraded whole-store payload differs.")
            database = Path(temporary) / (name + ".sqlite"); database.write_bytes(raw)
            with closing(sqlite3.connect(database.as_uri() + "?mode=ro&immutable=1", uri=True)) as connection:
                if connection.execute("pragma integrity_check").fetchall() != [("ok",)]:
                    raise ValueError("An upgraded whole-store snapshot is not intact.")
                if name == "runtime":
                    artifacts = {identity: incarnation + ":" + hashlib.sha256(content.encode()).hexdigest()
                        for identity, incarnation, content in connection.execute(
                            "SELECT Id, IncarnationId, ContentJson FROM elsa_runtime_workflow_executable WHERE IsModelMaterial = 0")}
                    if any(artifacts.get(identity) != value for identity, value in process["retainedArtifacts"].items()):
                        raise ValueError("An original executable's bytes or physical incarnation changed during upgrade.")
                    applied = [identity + "|" + version for identity, version in connection.execute(
                        "SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory_ElsaRuntime ORDER BY MigrationId")]
                    if applied != process["migrated"]:
                        raise ValueError("The upgraded store's applied migrations disagree with their receipt.")
                    for case in observations:
                        # The EF physical index uses lossless UTF-16 keys and the unchanged native status enum.
                        execution_key = base64.b64encode(case["executionId"].encode("utf-16-le", errors="surrogatepass")).decode()
                        artifact_key = base64.b64encode(case["completed"]["instance"]["artifactId"].encode("utf-16-le", errors="surrogatepass")).decode()
                        rows = connection.execute("SELECT Status, ArtifactId FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId = ?",
                            (execution_key,)).fetchall()
                        if rows != [(3, artifact_key)]:
                            raise ValueError("The final physical workflow row disagrees with its completed API observation.")
    frozen = json.loads(evidence_file(root, "frozen-contract.json").read_text())
    if (frozen.get("sourceRevision") != source or frozen.get("oracleSha256") != FROZEN or
            frozen.get("matchesFrozenFingerprint") is not True or frozen.get("immutableSnapshot") is not True or
            frozen.get("closedSnapshotSha256") != process["designSnapshot"]["databaseSha256"]):
        raise ValueError("The upgraded Design store has no frozen authoring verification.")
    if (not set(process["originalMigrations"]).issubset(process["migrated"]) or not process["retainedArtifactCount"] or
            len(process["retainedArtifacts"]) != process["retainedArtifactCount"]):
        raise ValueError("The upgrade lost applied migrations or retained executable material.")
    return {"actualLegacyExecutionsRecovered": 8, "wholeStoresVerified": 2, "frozenAuthoringPreserved": True,
            "sourceRevision": source, "allLegacyFamiliesRecovered": False, "studioAcceptanceProved": False}


def resume_upgrade(args) -> dict:
    if args.family == "native-parents":
        return resume_native_parent_upgrade(args)
    if args.family == "called-ack":
        return resume_called_ack_upgrade(args)
    if args.family == "broadcast":
        return resume_broadcast_upgrade(args)
    baseline = args.manifest.resolve(strict=True)
    verify_capture(baseline)
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True, timeout=5).strip()
    if subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=all"], cwd=REPO, text=True, timeout=5).strip():
        raise ValueError("Commit the qualification source before recording exact-source upgrade evidence.")
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    env = os.environ.copy()
    env.update(ELSA_BPMN_LEGACY_CAPTURE_DIRECTORY=str(baseline.parent), ELSA_BPMN_HOST_EVIDENCE_DIRECTORY=str(output),
        ELSA_BPMN_HOST_SOURCE_REVISION=source)
    project = REPO / "Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj"
    command = ["dotnet", "test", str(project), "-c", "Release", "-m:2", "-p:RestoreLockedMode=true", "--filter",
        "FullyQualifiedName~LegacyPartialForkUpgradeTests", "--logger", "trx;LogFileName=legacy-partial-fork-upgrade.trx"]
    if args.artifacts_path is not None:
        command += ["--artifacts-path", str(args.artifacts_path.resolve())]
    with (output / "run.log").open("w") as log:
        subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=3600)
    process = json.loads((output / "legacy-partial-forks/process.json").read_text())
    if (process["baselineManifestSha256"] != digest(baseline) or
            process["baselineSourceRevision"] != json.loads(baseline.read_text())["executingSourceCommit"]):
        raise ValueError("The host test did not recover the requested baseline.")
    with tempfile.TemporaryDirectory(prefix="elsa-upgraded-design-") as temporary:
        database = Path(temporary) / "design.sqlite"
        database.write_bytes(gzip.decompress((output / "legacy-partial-forks/upgraded-design.sqlite.gz").read_bytes()))
        subprocess.run([sys.executable, str(REPO / "e2e-tests/bpmn/verify_published_contracts.py"), "--database", str(database),
            "--source-revision", source, "--closed-snapshot", "--output", str(output / "frozen-contract.json")], check=True, timeout=60)
    verify_capture(baseline)
    paths = ["legacy-partial-forks/process.json", "legacy-partial-forks/upgraded-runtime.sqlite.gz",
        "legacy-partial-forks/upgraded-design.sqlite.gz", "frozen-contract.json"]
    manifest = {"schemaVersion": 1, "kind": "actual-legacy-partial-fork-upgrade", "sourceRevision": source,
        "baselineManifestSha256": digest(baseline), "baselineSourceRevision": process["baselineSourceRevision"],
        "baselineAssets": {asset["path"]: asset["sha256"] for asset in json.loads(baseline.read_text())["assets"]
            if asset["path"] in ("runtime.sqlite", "design.sqlite", "cases.json")},
        "assets": [{"path": name, "sha256": digest(output / name)} for name in paths]}
    write_json(output / "manifest.json", manifest)
    return verify_upgrade(output / "manifest.json")


def verify_native_parent_inputs(path: Path) -> dict:
    matrix = json.loads(path.read_text())
    if (matrix.get("kind") != "actual-pre-writer-baseline-matrix" or matrix.get("gateAComplete") is not True or
            matrix.get("frozenAuthoringFingerprint") != FROZEN):
        raise ValueError("The native-parent upgrade requires the actual frozen pre-writer matrix.")
    names = {"native-parents/api.json", "native-parents/runtime.sqlite.gz", "native-parents/design.sqlite.gz"}
    names.update("native-parent-pending/" + parent + "-" + mode + suffix
        for parent in ("sequence", "flowchart", "compensationscope") for mode in ("balanced", "durable")
        for suffix in ("-api.json", "-marker.json", ".sqlite.gz"))
    selected = {asset["path"]: asset for asset in matrix["assets"] if asset["path"] in names}
    if set(selected) != names or len([asset for asset in matrix["assets"] if asset["path"] in names]) != len(selected):
        raise ValueError("The native-parent matrix omits an original coupled store or observation.")
    for name, asset in selected.items():
        if digest(evidence_file(path.parent, name)) != asset["sha256"]:
            raise ValueError("An original native-parent input differs: " + name)
    return {name: asset["sha256"] for name, asset in selected.items()}


@contextmanager
def checked_upgrade_snapshot(root: Path, directory: str, snapshot: dict):
    file = evidence_file(root, directory + "/" + snapshot["file"])
    if digest(file) != snapshot["gzipSha256"]:
        raise ValueError("An upgraded native snapshot differs from its process receipt.")
    raw = gzip.decompress(file.read_bytes())
    if hashlib.sha256(raw).hexdigest() != snapshot["databaseSha256"]:
        raise ValueError("An upgraded native database differs from its retained bytes.")
    with tempfile.TemporaryDirectory(prefix="elsa-native-upgrade-verify-") as temporary:
        database = Path(temporary) / "snapshot.sqlite"; database.write_bytes(raw)
        with closing(sqlite3.connect(database.as_uri() + "?mode=ro&immutable=1", uri=True)) as connection:
            if connection.execute("pragma integrity_check").fetchall() != [("ok",)]:
                raise ValueError("An upgraded native snapshot failed integrity verification.")
            yield connection


def verify_retained_native_material(connection, process):
    material = {identity: incarnation + ":" + hashlib.sha256(content.encode()).hexdigest()
        for identity, incarnation, content in connection.execute(
            "SELECT Id, IncarnationId, ContentJson FROM elsa_runtime_workflow_executable WHERE IsModelMaterial = 0")}
    if not process["retainedArtifacts"] or any(material.get(identity) != value for identity, value in process["retainedArtifacts"].items()):
        raise ValueError("An original native executable's bytes or incarnation changed.")
    applied = [identity + "|" + version for identity, version in connection.execute(
        "SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory_ElsaRuntime ORDER BY MigrationId")]
    if applied != process["migrations"] or not set(process["originalMigrations"]).issubset(applied):
        raise ValueError("The native upgrade lost its applied migration history.")


def verify_native_parent_upgrade(path: Path) -> dict:
    manifest = json.loads(path.read_text()); root = path.parent
    if manifest.get("schemaVersion") != 1 or manifest.get("kind") != "actual-legacy-native-parent-upgrade":
        raise ValueError("Unknown native-parent upgrade evidence.")
    names = {"legacy-pending-parent/process.json", "legacy-native-terminals/process.json", "frozen-contract.json",
        "legacy-native-terminals/native-terminals-runtime.sqlite.gz", "legacy-native-terminals/native-terminals-design.sqlite.gz"}
    names.update("legacy-pending-parent/" + parent + "-" + mode + "-runtime.sqlite.gz"
        for parent in ("sequence", "flowchart", "compensationscope") for mode in ("balanced", "durable"))
    declared = {asset["path"]: asset for asset in manifest["assets"]}
    if set(declared) != names or len(declared) != len(manifest["assets"]):
        raise ValueError("The native upgrade evidence set is incomplete or duplicated.")
    for name, asset in declared.items():
        if digest(evidence_file(root, name)) != asset["sha256"]:
            raise ValueError("A native upgrade asset differs from its retained hash.")
    pending = json.loads(evidence_file(root, "legacy-pending-parent/process.json").read_text())
    terminals = json.loads(evidence_file(root, "legacy-native-terminals/process.json").read_text())
    source = manifest.get("sourceRevision", "")
    if not re.fullmatch(r"[0-9a-f]{40}", source): raise ValueError("The native upgrade has no exact source identity.")
    for process, kind in ((pending, "actual-legacy-pending-parent-upgrade"), (terminals, "actual-legacy-native-terminal-upgrade")):
        if (process.get("kind") != kind or process.get("formatVersion") != 1 or
                process.get("sourceRevision") != source or process.get("testOnlyRecoveryProbePresent") is not False or
                process.get("baselineManifestSha256") != manifest.get("baselineManifestSha256") or
                process.get("referenceReceiptSha256") != manifest["baselineAssets"].get("native-parents/api.json") or
                not any(binary["file"] == "Elsa.Workbench.dll" and binary["productVersion"].endswith("+" + source)
                    for binary in process.get("binaries", process.get("observations", [{}])[0].get("binaries", [])))):
            raise ValueError("The native upgrade lacks its standard-host source or baseline binding.")
    observations = pending["observations"]
    if len(observations) != 6 or {(case["parent"], case["persistenceMode"]) for case in observations} != {
            (parent, mode) for parent in ("Sequence", "Flowchart", "CompensationScope") for mode in ("Balanced", "Durable")}:
        raise ValueError("The pending native-parent upgrade matrix is incomplete.")
    key = lambda text: base64.b64encode(text.encode("utf-16-le", errors="surrogatepass")).decode()
    statuses = {"Completed": 3, "Faulted": 4, "Cancelled": 5}
    for case in observations:
        parent = case["parent"]; view = case["completed"]
        expected = "Completed" if parent == "CompensationScope" else "Faulted"
        native = [activity for activity in view["activities"] if activity["executableNodeId"] == "native-parent"]
        child = [activity for activity in view["activities"] if activity["executableNodeId"] == "legacy-bpmn"]
        expected_parent = {"Sequence": "Running", "Flowchart": "Faulted", "CompensationScope": "Completed"}[parent]
        if (view["instance"]["status"] != expected or len(native) != 1 or native[0]["status"] != expected_parent or
                len(child) != 1 or child[0]["status"] != "Faulted" or
                native[0]["outcomeNames"] != (["Compensated"] if parent == "CompensationScope" else []) or
                any(activity["executableNodeId"] == "after-bpmn" for activity in view["activities"])):
            raise ValueError("An upgraded pending parent changed its original composite projection.")
        if case.get("parentOutcomeApplied") is not (parent != "Sequence") or parent == "Sequence" and "not claimed" not in case["callbackDisposition"]:
            raise ValueError("Native terminal disposition cannot be called parent consumption.")
        for field in ("workflowExecutionId", "artifactId", "artifactHash", "artifactVersion", "definitionId", "definitionVersionId"):
            if case["captured"]["instance"][field] != view["instance"][field]: raise ValueError("An upgraded native callback changed its complete pin.")
        for node in ("native-parent", "legacy-bpmn", "node-held"):
            before = [activity for activity in case["captured"]["activities"] if activity["executableNodeId"] == node]
            after = [activity for activity in view["activities"] if activity["executableNodeId"] == node]
            if len(before) != 1 or len(after) != 1 or before[0]["activityExecutionId"] != after[0]["activityExecutionId"]:
                raise ValueError("An upgraded native callback replaced its original invocation.")
        if (case["completed"] != case["replayed"] or case["originalProcessId"] == case["restartedProcessId"] or
                case["acceptedSource"]["status"] != 2 or case["originalSource"]["status"] != 0 or
                case["acceptedSource"]["document"]["intent"] != case["originalSource"]["document"]["intent"] or
                case["acceptedSource"] != case["replayedSource"]):
            raise ValueError("The original callback changed, remained owed or repeated after restart.")
        name = "native-parent-pending/" + case["caseId"] + ".sqlite.gz"
        if case["inputGzipSha256"] != manifest["baselineAssets"].get(name) or case["baselineSourceRevision"] == source:
            raise ValueError("A native parent was not recovered from its original captured store.")
        original = case["originalSource"]["document"]
        intent = original["intent"]; work = intent["payload"]; payload = work["Payload"]
        if (original["outboxItemId"] != key(case["sourceOutboxItemId"]) or intent["workflowExecutionId"] != key(case["executionId"]) or
                work["WorkflowExecutionId"] != case["executionId"] or work["CommandKind"] != 8 or
                payload["ActivityExecutionId"] != native[0]["activityExecutionId"] or payload["CompletedChildActivityExecutionId"] != child[0]["activityExecutionId"]):
            raise ValueError("The original callback does not address its exact native parent and BPMN child.")
        for stored, api in (("ArtifactId", "artifactId"), ("ArtifactHash", "artifactHash"), ("ArtifactVersion", "artifactVersion"),
                ("DefinitionId", "definitionId"), ("DefinitionVersionId", "definitionVersionId")):
            if payload["PinnedExecutable"][stored] != view["instance"][api]:
                raise ValueError("The original callback's complete executable pin differs.")
        with checked_upgrade_snapshot(root, "legacy-pending-parent", case["snapshot"]) as connection:
            verify_retained_native_material(connection, case)
            row = connection.execute("SELECT Status, Revision, ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId = ?",
                (key(case["sourceOutboxItemId"]),)).fetchall()
            accepted = case["acceptedSource"]
            if (len(row) != 1 or row[0][0] != 2 or row[0][1] != accepted["revision"] or
                    hashlib.sha256(row[0][2].encode()).hexdigest() != accepted["contentSha256"] or json.loads(row[0][2]) != accepted["document"]):
                raise ValueError("The physical original callback disagrees with its delivery receipt.")
            row = connection.execute("SELECT Status, ArtifactId FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId = ?",
                (key(case["executionId"]),)).fetchall()
            if row != [(statuses[expected], key(view["instance"]["artifactId"]))]:
                raise ValueError("The physical native terminal row disagrees with its outcome.")
            if connection.execute("SELECT COUNT(*) FROM elsa_runtime_scheduler_work_item WHERE WorkflowExecutionId = ?", (key(case["executionId"]),)).fetchone() != (0,):
                raise ValueError("An upgraded native terminal still has queued callback work.")
    if len(terminals["observations"]) != 12 or {(case["parent"], case["persistenceMode"], case["outcome"]) for case in terminals["observations"]} != {
            (parent, mode, outcome) for parent in ("Sequence", "Flowchart", "CompensationScope") for mode in ("Balanced", "Durable") for outcome in ("Fault", "Cancel")}:
        raise ValueError("The retained native terminal matrix is incomplete.")
    if (terminals["inputGzipSha256"] != manifest["baselineAssets"].get("native-parents/runtime.sqlite.gz") or
            terminals["inputDesignGzipSha256"] != manifest["baselineAssets"].get("native-parents/design.sqlite.gz") or terminals["baselineSourceRevision"] == source):
        raise ValueError("The retained native terminals do not identify their original coupled stores.")
    with checked_upgrade_snapshot(root, "legacy-native-terminals", terminals["snapshot"]) as connection:
        verify_retained_native_material(connection, terminals)
        for case in terminals["observations"]:
            before, after = case["baseline"], case["recovered"]
            for name in ("workflowExecutionId", "artifactId", "artifactHash", "artifactVersion", "definitionId", "definitionVersionId", "status"):
                if before["instance"][name] != after["instance"][name]: raise ValueError("An existing native terminal changed its pin or status.")
            rows = connection.execute("SELECT Status FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId = ?",
                (key(case["executionId"]),)).fetchall()
            if rows != [(statuses[after["instance"]["status"]],)]: raise ValueError("A retained native terminal disagrees with its physical state.")
    with checked_upgrade_snapshot(root, "legacy-native-terminals", terminals["designSnapshot"]): pass
    frozen = json.loads(evidence_file(root, "frozen-contract.json").read_text())
    if (frozen.get("sourceRevision") != source or frozen.get("oracleSha256") != FROZEN or frozen.get("matchesFrozenFingerprint") is not True or frozen.get("immutableSnapshot") is not True or
            frozen.get("closedSnapshotSha256") != terminals["designSnapshot"]["databaseSha256"]):
        raise ValueError("The native terminal upgrade lost its frozen authoring verification.")
    return {"pendingParentRecoveries": 6, "nativeParentOutcomesApplied": 4, "terminalDispositionsWithoutParentConsumption": 2,
        "retainedNativeTerminals": 12, "wholeStoresVerified": 8,
        "frozenAuthoringPreserved": True, "sourceRevision": source, "allLegacyFamiliesRecovered": False, "studioAcceptanceProved": False}


def resume_native_parent_upgrade(args) -> dict:
    baseline = args.manifest.resolve(strict=True); inputs = verify_native_parent_inputs(baseline)
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True, timeout=5).strip()
    if subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=all"], cwd=REPO, text=True, timeout=5).strip():
        raise ValueError("Commit the qualification source before recording exact-source upgrade evidence.")
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False); env = os.environ.copy()
    env.update(ELSA_BPMN_LEGACY_MATRIX_DIRECTORY=str(baseline.parent), ELSA_BPMN_HOST_EVIDENCE_DIRECTORY=str(output), ELSA_BPMN_HOST_SOURCE_REVISION=source)
    command = ["dotnet", "test", str(REPO / "Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj"),
        "-c", "Release", "-m:2", "-p:RestoreLockedMode=true", "-p:ElsaBpmnRecoveryProbe=false", "--filter", "FullyQualifiedName~LegacyNativeParentUpgradeTests",
        "--logger", "trx;LogFileName=legacy-native-parent-upgrade.trx"]
    if args.artifacts_path is not None: command += ["--artifacts-path", str(args.artifacts_path.resolve())]
    with (output / "run.log").open("w") as log:
        subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=3600)
    terminals = json.loads((output / "legacy-native-terminals/process.json").read_text())
    with tempfile.TemporaryDirectory(prefix="elsa-native-upgraded-design-") as temporary:
        database = Path(temporary) / "design.sqlite"; database.write_bytes(gzip.decompress((output / "legacy-native-terminals/native-terminals-design.sqlite.gz").read_bytes()))
        subprocess.run([sys.executable, str(REPO / "e2e-tests/bpmn/verify_published_contracts.py"), "--database", str(database), "--source-revision", source,
            "--closed-snapshot", "--output", str(output / "frozen-contract.json")], check=True, timeout=60)
    if inputs != verify_native_parent_inputs(baseline): raise ValueError("The native upgrade changed an original input.")
    paths = ["legacy-pending-parent/process.json", "legacy-native-terminals/process.json", "frozen-contract.json",
        "legacy-native-terminals/native-terminals-runtime.sqlite.gz", "legacy-native-terminals/native-terminals-design.sqlite.gz"]
    paths += ["legacy-pending-parent/" + case["caseId"] + "-runtime.sqlite.gz" for case in json.loads((output / "legacy-pending-parent/process.json").read_text())["observations"]]
    manifest = {"schemaVersion": 1, "kind": "actual-legacy-native-parent-upgrade", "sourceRevision": source,
        "baselineManifestSha256": digest(baseline), "baselineAssets": inputs, "assets": [{"path": name, "sha256": digest(output / name)} for name in paths]}
    write_json(output / "manifest.json", manifest); return verify_native_parent_upgrade(output / "manifest.json")


def verify_called_ack_inputs(path: Path) -> dict:
    matrix = json.loads(path.read_text())
    if (matrix.get("kind") != "actual-pre-writer-baseline-matrix" or matrix.get("gateAComplete") is not True or
            matrix.get("frozenAuthoringFingerprint") != FROZEN):
        raise ValueError("Called acknowledgment recovery requires the actual frozen pre-writer matrix.")
    names = {"called-ack/" + cut + suffix for cut in ("notice", "terminal")
        for suffix in ("-held-api.json", "-marker.json", "-held.sqlite.gz")}
    selected = {asset["path"]: asset["sha256"] for asset in matrix["assets"] if asset["path"] in names}
    if set(selected) != names or len([asset for asset in matrix["assets"] if asset["path"] in names]) != len(names):
        raise ValueError("The actual called acknowledgment inputs are incomplete or duplicated.")
    for name, sha in selected.items():
        if digest(evidence_file(path.parent, name)) != sha: raise ValueError("An original called acknowledgment input differs: " + name)
    return selected


def verify_called_ack_upgrade(path: Path) -> dict:
    manifest = json.loads(path.read_text()); root = path.parent
    if manifest.get("schemaVersion") != 1 or manifest.get("kind") != "actual-legacy-called-ack-upgrade":
        raise ValueError("Unknown called acknowledgment upgrade evidence.")
    names = {"legacy-called-ack/process.json", "frozen-contract.json"}
    names.update("legacy-called-ack/" + cut + "-" + phase + ".sqlite.gz"
        for cut in ("notice", "terminal") for phase in ("recovered", "finished"))
    declared = {asset["path"]: asset for asset in manifest["assets"]}
    if set(declared) != names or len(declared) != len(manifest["assets"]):
        raise ValueError("The called acknowledgment evidence is incomplete or duplicated.")
    for name, asset in declared.items():
        if digest(evidence_file(root, name)) != asset["sha256"]: raise ValueError("A called acknowledgment asset differs.")
    process = json.loads(evidence_file(root, "legacy-called-ack/process.json").read_text()); source = manifest.get("sourceRevision", "")
    if (not re.fullmatch(r"[0-9a-f]{40}", source) or process.get("sourceRevision") != source or
            process.get("formatVersion") != 1 or process.get("kind") != manifest["kind"] or
            process.get("testOnlyRecoveryProbePresent") is not False or
            process.get("baselineManifestSha256") != manifest.get("baselineManifestSha256") or
            not process.get("reader", {}).get("productVersion", "").endswith("+" + source)):
        raise ValueError("The called acknowledgment upgrade lacks its standard host/checked reader source binding.")
    cases = process["observations"]
    if len(cases) != 2 or {case["cut"] for case in cases} != {"notice", "terminal"}:
        raise ValueError("Both actual notice and terminal acknowledgment cuts are required.")
    key = lambda value: base64.b64encode(value.encode("utf-16-le", errors="surrogatepass")).decode()
    for case in cases:
        cut = case["cut"]; original = case["original"]; marker = case["marker"]
        if (not re.fullmatch(r"[0-9a-f]{40}", case.get("baselineSourceRevision", "")) or case["baselineSourceRevision"] == source or
                case["inputGzipSha256"] != manifest["baselineAssets"].get("called-ack/" + cut + "-held.sqlite.gz") or
                case["baselineApiSha256"] != manifest["baselineAssets"].get("called-ack/" + cut + "-held-api.json") or
                case["markerSha256"] != manifest["baselineAssets"].get("called-ack/" + cut + "-marker.json") or
                not any(binary["file"] == "Elsa.Workbench.dll" and binary["productVersion"].endswith("+" + source) for binary in case["binaries"]) or
                len({case["firstProcessId"], case["secondProcessId"], case["thirdProcessId"]}) != 3):
            raise ValueError("The actual called cut lost its baseline, host or three-process identity.")
        if (marker["phase"] != "called-control-before-acknowledgment" or
                marker["providerEvent"] != "ActualIntentDispatcherReturnedBeforeDeliveryResult" or
                marker["transport"]["batch"]["ordinal"] != (0 if cut == "notice" else 1)):
            raise ValueError("The actual dispatcher acknowledgment cut differs.")
        for phase in ("recovered", "finished"):
            api = case[phase + "Api"]; proof = case[phase + "Proof"]; snapshot = case[phase + "Snapshot"]
            checked = proof["calledWorkflow"]; facts = checked["facts"]; receipt = api["cases"][0]; completed_pair = cut == "terminal" or phase == "finished"
            if (proof.get("providerChecked") is not True or proof.get("readOnly") is not True or
                    proof["snapshotSha256"] != snapshot["databaseSha256"] or checked.get("exactCommittedAcceptanceVerified") is not True or
                    checked.get("exactPreAcknowledgmentSourceVerified") is not True or checked["sourceIntentFingerprint"] != marker["intentFingerprint"] or
                    checked["source"]["outboxItemId"] != marker["sourceOutboxItemId"]):
                raise ValueError("The upgraded cut has no exact checked acceptance/source proof.")
            source_item = checked["source"]
            if (source_item["status"] != 2 or source_item["deliveryFencingToken"] <= marker["fencingToken"] or
                    source_item["deliveringOwnerId"] is not None or source_item["deliveredAt"] is None):
                raise ValueError("The original source has not been acknowledged by a fresh owner.")
            transport = source_item["intent"]["payload"]
            if cut == "terminal": transport = transport["result"]["control"]
            if transport != marker["transport"]: raise ValueError("The original called transport changed.")
            for name in ("parent", "child"):
                view = receipt[name]; before = original[name]["instance"]; observed = view["instance"]; state = facts[name]
                if (observed["workflowExecutionId"] != before["workflowExecutionId"] or state["workflowExecutionId"] != before["workflowExecutionId"] or
                        any(observed[field] != before[field] or state["pinnedExecutable"][field] != before[field]
                            for field in ("artifactId", "artifactHash", "artifactVersion", "definitionId", "definitionVersionId")) or view["incidents"]):
                    raise ValueError("The actual called execution changed its complete pin or acquired incidents.")
                expected = "Completed" if phase == "finished" or name == "child" and completed_pair else "Running"
                if observed["status"] != expected or state["status"] != (3 if expected == "Completed" else 1):
                    raise ValueError("The actual called cut changed its expected workflow outcome.")
            calls = [item for item in facts["parentActivities"] if item["execution"]["executableNodeId"] == "node-call"]
            if len(calls) != 1: raise ValueError("The called invocation was repeated.")
            call = calls[0]; inbox = call["childControlInbox"]
            if (inbox["nextOrdinal"] != (2 if completed_pair else 1) or inbox["isTerminalConsumed"] != completed_pair or inbox["pending"] or
                    inbox["target"]["parentExecutable"] != facts["parent"]["pinnedExecutable"]):
                raise ValueError("The committed caller inbox does not prove exact acceptance.")
            if len([item for item in facts["parentActivities"] if item["execution"]["executableNodeId"] == "node-normal"]) != (1 if completed_pair else 0):
                raise ValueError("The actual normal successor was lost or repeated.")
            with checked_upgrade_snapshot(root, "legacy-called-ack", snapshot) as connection:
                verify_retained_native_material(connection, case)
                rows = connection.execute("SELECT Status, ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId = ?", (key(marker["sourceOutboxItemId"]),)).fetchall()
                if len(rows) != 1: raise ValueError("The physical original acknowledgment source disappeared.")
                physical_source = json.loads(rows[0][1])
                if (rows[0][0] != 2 or physical_source["status"] != "Delivered" or physical_source["deliveryFencingToken"] != source_item["deliveryFencingToken"] or
                        physical_source["deliveredAt"] != source_item["deliveredAt"] or physical_source["intent"]["payload"] != source_item["intent"]["payload"]):
                    raise ValueError("The physical original acknowledgment source differs from checked delivery.")
                rows = connection.execute("SELECT ContentJson FROM elsa_runtime_activity_execution_state WHERE ActivityExecutionId = ?", (key(call["invocationId"]),)).fetchall()
                if len(rows) != 1: raise ValueError("The physical actual caller disappeared.")
                physical_call = json.loads(rows[0][0]); stored = physical_call["childControlInbox"]
                if (stored["nextOrdinal"] != inbox["nextOrdinal"] or stored["isTerminalConsumed"] != inbox["isTerminalConsumed"] or stored["pending"] or
                        stored["target"]["parentExecutable"] != {field: key(value) for field, value in inbox["target"]["parentExecutable"].items()}):
                    raise ValueError("The physical caller inbox does not prove exact consumption.")
                for name in ("parent", "child"):
                    state = facts[name]; rows = connection.execute("SELECT Status, ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId = ?", (key(state["workflowExecutionId"]),)).fetchall()
                    if len(rows) != 1 or rows[0][0] != state["status"] or json.loads(rows[0][1])["pinnedExecutable"] != {field: key(value) for field, value in state["pinnedExecutable"].items()}:
                        raise ValueError("The physical caller/callee outcome or full pin differs.")
            if phase == "finished" and (case["warningDelivery"]["startedCount"] != 0 or case["warningDelivery"]["resumedCount"] != 1 or
                    cut == "notice" and (case["childDelivery"]["startedCount"] != 0 or case["childDelivery"]["resumedCount"] != 1)):
                raise ValueError("The original called pair did not finish through its normal Events.")
    frozen = json.loads(evidence_file(root, "frozen-contract.json").read_text())
    notice = next(case for case in cases if case["cut"] == "notice")
    if (frozen.get("sourceRevision") != source or frozen.get("oracleSha256") != FROZEN or
            frozen.get("matchesFrozenFingerprint") is not True or frozen.get("immutableSnapshot") is not True or
            frozen.get("closedSnapshotSha256") != notice["finishedSnapshot"]["databaseSha256"]):
        raise ValueError("The upgraded called Design store has no frozen authoring verification.")
    return {"actualCalledAcknowledgmentCutsRecovered": 2, "wholeStoresVerified": 4, "exactCommittedAcceptanceVerified": True,
        "frozenAuthoringPreserved": True,
        "sourceRevision": source, "allLegacyFamiliesRecovered": False, "studioAcceptanceProved": False, "directModelSettlementProved": False}


def resume_called_ack_upgrade(args) -> dict:
    baseline = args.manifest.resolve(strict=True); inputs = verify_called_ack_inputs(baseline)
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True, timeout=5).strip()
    if subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=all"], cwd=REPO, text=True, timeout=5).strip():
        raise ValueError("Commit the qualification source before recording exact-source upgrade evidence.")
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False); env = os.environ.copy()
    env.update(ELSA_BPMN_LEGACY_MATRIX_DIRECTORY=str(baseline.parent), ELSA_BPMN_HOST_EVIDENCE_DIRECTORY=str(output), ELSA_BPMN_HOST_SOURCE_REVISION=source)
    command = ["dotnet", "test", str(REPO / "Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj"),
        "-c", "Release", "-m:2", "-p:RestoreLockedMode=true", "-p:ElsaBpmnRecoveryProbe=false", "--filter", "FullyQualifiedName~LegacyCalledAcknowledgmentUpgradeTests",
        "--logger", "trx;LogFileName=legacy-called-ack-upgrade.trx"]
    if args.artifacts_path is not None: command += ["--artifacts-path", str(args.artifacts_path.resolve())]
    with (output / "run.log").open("w") as log:
        subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=3600)
    if inputs != verify_called_ack_inputs(baseline): raise ValueError("The called upgrade changed an original input.")
    with tempfile.TemporaryDirectory(prefix="elsa-called-upgraded-design-") as temporary:
        database = Path(temporary) / "design.sqlite"; database.write_bytes(gzip.decompress((output / "legacy-called-ack/notice-finished.sqlite.gz").read_bytes()))
        subprocess.run([sys.executable, str(REPO / "e2e-tests/bpmn/verify_published_contracts.py"), "--database", str(database), "--source-revision", source,
            "--closed-snapshot", "--output", str(output / "frozen-contract.json")], check=True, timeout=60)
    paths = ["legacy-called-ack/process.json", "frozen-contract.json"] + ["legacy-called-ack/" + cut + "-" + phase + ".sqlite.gz"
        for cut in ("notice", "terminal") for phase in ("recovered", "finished")]
    manifest = {"schemaVersion": 1, "kind": "actual-legacy-called-ack-upgrade", "sourceRevision": source,
        "baselineManifestSha256": digest(baseline), "baselineAssets": inputs, "assets": [{"path": name, "sha256": digest(output / name)} for name in paths]}
    write_json(output / "manifest.json", manifest); return verify_called_ack_upgrade(output / "manifest.json")


def verify_broadcast_inputs(path: Path) -> dict:
    matrix = json.loads(path.read_text())
    if (matrix.get("kind") != "actual-pre-writer-baseline-matrix" or matrix.get("gateAComplete") is not True or matrix.get("frozenAuthoringFingerprint") != FROZEN):
        raise ValueError("Broadcast recovery requires the actual frozen pre-writer matrix.")
    names = {"broadcast/" + cut + suffix for cut in ("before-selection", "after-cohort", "after-resume", "empty-cohort") for suffix in (".json", ".sqlite.gz")}
    selected = {asset["path"]: asset["sha256"] for asset in matrix["assets"] if asset["path"] in names}
    if set(selected) != names or len([asset for asset in matrix["assets"] if asset["path"] in names]) != len(names):
        raise ValueError("The actual broadcast inputs are incomplete or duplicated.")
    for name, sha in selected.items():
        if digest(evidence_file(path.parent, name)) != sha: raise ValueError("An original broadcast input differs: " + name)
    return selected


def verify_broadcast_upgrade(path: Path) -> dict:
    manifest = json.loads(path.read_text()); root = path.parent
    if manifest.get("schemaVersion") != 1 or manifest.get("kind") != "actual-legacy-broadcast-upgrade": raise ValueError("Unknown broadcast upgrade evidence.")
    cuts = {"before-selection", "after-cohort", "after-resume", "empty-cohort"}
    names = {"legacy-broadcast/process.json", "frozen-contract.json"} | {"legacy-broadcast/" + cut + "-" + phase + ".sqlite.gz" for cut in cuts for phase in ("recovered", "finished")}
    assets = {asset["path"]: asset for asset in manifest["assets"]}
    if set(assets) != names or len(assets) != len(manifest["assets"]): raise ValueError("The broadcast evidence is incomplete or duplicated.")
    for name, asset in assets.items():
        if digest(evidence_file(root, name)) != asset["sha256"]: raise ValueError("A broadcast evidence asset differs.")
    process = json.loads(evidence_file(root, "legacy-broadcast/process.json").read_text()); source_revision = manifest.get("sourceRevision", "")
    if (not re.fullmatch(r"[0-9a-f]{40}", source_revision) or process.get("sourceRevision") != source_revision or process.get("kind") != manifest["kind"] or
            process.get("formatVersion") != 1 or process.get("testOnlyRecoveryProbePresent") is not False or process.get("baselineManifestSha256") != manifest.get("baselineManifestSha256") or
            not process.get("reader", {}).get("productVersion", "").endswith("+" + source_revision)):
        raise ValueError("The broadcast upgrade lacks its source-bound standard host and checked reader.")
    cases = process["observations"]
    if len(cases) != 4 or {case["cut"] for case in cases} != cuts: raise ValueError("All four actual broadcast cuts are required.")
    key = lambda value: base64.b64encode(value.encode("utf-16-le", errors="surrogatepass")).decode()
    for case in cases:
        cut = case["cut"]; original = case["originalStopped"]; reference = case["referenceSource"]
        if (not re.fullmatch(r"[0-9a-f]{40}", case.get("baselineSourceRevision", "")) or case["baselineSourceRevision"] == source_revision or
                case["baselineApiSha256"] != manifest["baselineAssets"].get("broadcast/" + cut + ".json") or
                case["inputGzipSha256"] != manifest["baselineAssets"].get("broadcast/" + cut + ".sqlite.gz") or
                not any(binary["file"] == "Elsa.Workbench.dll" and binary["productVersion"].endswith("+" + source_revision) for binary in case["binaries"]) or
                len({case["firstProcessId"], case["secondProcessId"], case["thirdProcessId"]}) != 3):
            raise ValueError("The broadcast cut lacks its actual baseline or three-process host identity.")
        recovered = case["recoveredProof"]; current = recovered["source"]; before = original["source"]
        if (current["status"] != "Delivered" or current["deliveryFencingToken"] <= before["deliveryFencingToken"] or current["deliveringOwnerId"] is not None or
                any(current[field] != before[field] for field in ("outboxItemId", "workflowExecutionId", "intentPayload", "intentFingerprint")) or
                current["selectedStarts"] != reference["selectedStarts"] or current["selectedResumes"] != reference["selectedResumes"]):
            raise ValueError("The original broadcast source/selected recipient identities changed.")
        if current["snapshot"] is None: raise ValueError("A durable broadcast recipient decision is required, including empty decisions.")
        if cut == "before-selection":
            if (before["snapshot"] is not None or len(current["selectedStarts"]) != 2 or len(current["selectedResumes"]) != 4 or
                    current["snapshot"]["starts"] != reference["snapshot"]["starts"] or current["snapshot"]["resumes"] != reference["snapshot"]["resumes"]):
                raise ValueError("Delayed first delivery must select the actual listeners armed after publisher commit.")
        elif current["snapshot"] != before["snapshot"]: raise ValueError("Recovery changed the original immutable broadcast cohort.")
        workflows = {item["workflowExecutionId"]: item for item in recovered["workflows"]}
        for old in original["workflows"]:
            if not old["exists"]: continue
            now = workflows[old["workflowExecutionId"]]
            if old["pinnedExecutable"] != now["pinnedExecutable"]: raise ValueError("An original broadcast workflow changed its complete pin.")
            if not any(item["workflowExecutionId"] == old["workflowExecutionId"] for item in current["selectedResumes"]) and old["bookmarks"] != now["bookmarks"]:
                raise ValueError("A saved empty or fixed cohort changed an excluded listener.")
        for recipient in current["selectedResumes"]:
            now = workflows[recipient["workflowExecutionId"]]
            completed = [item for item in now["selectedActivities"] if item["activityExecutionId"] == recipient["activityExecutionId"]]
            if len(completed) != 1 or completed[0]["status"] != "Completed" or any(wait["bookmarkId"] == recipient["bookmarkId"] for wait in now["bookmarks"]):
                raise ValueError("A selected saved callback lacks durable completion/registration retirement.")
        if cut == "after-resume":
            for old in original["workflows"]:
                if not old["exists"]: continue
                now = workflows[old["workflowExecutionId"]]
                for wait in old["bookmarks"]:
                    if not any(item["bookmarkId"] == wait["bookmarkId"] for item in current["selectedResumes"]) and wait not in now["bookmarks"]:
                        raise ValueError("Source retry consumed or changed an already re-armed listener.")
                for old_activity in old["selectedActivities"]:
                    new_activity = next(item for item in now["selectedActivities"] if item["activityExecutionId"] == old_activity["activityExecutionId"])
                    if old_activity["status"] == "Completed" and old_activity != new_activity:
                        raise ValueError("Source retry repeated an already accepted callback.")
        if case["secondProof"]["source"] != current or case["secondProof"]["workflows"] != recovered["workflows"]:
            raise ValueError("A second process changed the recovered broadcast boundary.")
        for phase in ("recovered", "finished"):
            proof = case[phase + "Proof"]; snapshot = case[phase + "Snapshot"]
            if proof.get("providerChecked") is not True or proof.get("readOnly") is not True or proof["snapshotSha256"] != snapshot["databaseSha256"] or proof["source"] != current:
                raise ValueError("The upgraded broadcast cut has no exact checked snapshot/source proof.")
            with checked_upgrade_snapshot(root, "legacy-broadcast", snapshot) as connection:
                verify_retained_native_material(connection, case)
                rows = connection.execute("SELECT Status, ContentJson, Revision FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId = ?", (key(current["outboxItemId"]),)).fetchall()
                if (len(rows) != 1 or rows[0][0] != 2 or hashlib.sha256(rows[0][1].encode()).hexdigest() != current["contentSha256"] or rows[0][2] != current["rowRevision"]):
                    raise ValueError("The physical original broadcast delivery differs from its checked source.")
                document = json.loads(rows[0][1]); stored = document["recipientSnapshot"]
                if stored is None or document["status"] != "Delivered": raise ValueError("The physical saved recipient decision is missing.")
                physical_resumes = [(entry["workflowExecutionId"], entry["bookmarkId"], entry["activityExecutionId"], entry["registrationCapture"]["generation"]) for entry in stored["resumes"]]
                expected_resumes = [(key(entry["workflowExecutionId"]), key(entry["bookmarkId"]), key(entry["activityExecutionId"]), entry["generation"]) for entry in current["selectedResumes"]]
                if physical_resumes != expected_resumes or len(stored["starts"]) != len(current["selectedStarts"]):
                    raise ValueError("The physical broadcast recipient identities/generations differ.")
                for recipient in current["selectedResumes"]:
                    rows = connection.execute("SELECT Status FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId = ? AND ActivityExecutionId = ?", (key(recipient["workflowExecutionId"]), key(recipient["activityExecutionId"]))).fetchall()
                    if rows != [("Completed",)]: raise ValueError("The physical saved callback did not complete.")
                for workflow in proof["workflows"]:
                    if workflow.get("exists") is not True or not isinstance(workflow.get("pinnedExecutable"), dict): raise ValueError("An actual broadcast recipient disappeared.")
                    rows = connection.execute("SELECT Status, ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId = ?", (key(workflow["workflowExecutionId"]),)).fetchall()
                    expected_status = {"Pending": 0, "Running": 1, "Suspended": 2, "Completed": 3, "Faulted": 4, "Cancelled": 5}.get(workflow["status"])
                    if len(rows) != 1 or rows[0][0] != expected_status or json.loads(rows[0][1])["status"] != workflow["status"] or json.loads(rows[0][1])["pinnedExecutable"] != {field: key(value) for field, value in workflow["pinnedExecutable"].items()}:
                        raise ValueError("A physical broadcast workflow changed its outcome or complete pin.")
                    # BookmarkState's established adapter uses raw bounded identities and Revision as generation.
                    physical_waits = dict(connection.execute("SELECT BookmarkId, Revision FROM elsa_runtime_bookmark_state WHERE WorkflowExecutionId = ?", (workflow["workflowExecutionId"],)))
                    if physical_waits != {wait["bookmarkId"]: wait["generation"] for wait in workflow["bookmarks"]}: raise ValueError("Physical broadcast wait identities/generations differ from the checked boundary.")
            if phase == "finished" and (any(workflow["bookmarks"] for workflow in proof["workflows"]) or
                    any(workflow["status"] not in ("Completed", "Cancelled") for workflow in proof["allWorkflows"])):
                raise ValueError("A recovered broadcast execution did not retire through its normal command.")
        if cut == "after-resume":
            if len(case["loopingIds"]) != 2 or case["cancellation"]["status"] != "Completed": raise ValueError("The authored infinite loops lack completed normal native cancellation.")
        elif case["loopingIds"]: raise ValueError("A finite broadcast fixture was relabeled as an infinite loop.")
        expected_cancellation = set(case["loopingIds"])
        for command in case["finishCommands"]:
            if command["startedCount"] != 0: raise ValueError("A normal finish Event started new work.")
            if command["resumedCount"] == 0:
                if not command["resumes"] or any(item["status"] != "Ambiguous" for item in command["resumes"]): raise ValueError("An excluded native listener lacks its explicit generic-HTTP ambiguity refusal.")
                expected_cancellation.update(item["workflowExecutionId"] for item in command["resumes"])
        if set(case["cancelledIds"]) != expected_cancellation or expected_cancellation and case["cancellation"]["status"] != "Completed":
            raise ValueError("The exact native retirement targets differ from infinite or explicitly ambiguous listeners.")
    frozen = json.loads(evidence_file(root, "frozen-contract.json").read_text()); first = next(case for case in cases if case["cut"] == "before-selection")
    if (frozen.get("sourceRevision") != source_revision or frozen.get("oracleSha256") != FROZEN or frozen.get("matchesFrozenFingerprint") is not True or
            frozen.get("immutableSnapshot") is not True or frozen.get("closedSnapshotSha256") != first["finishedSnapshot"]["databaseSha256"]):
        raise ValueError("The upgraded broadcast Design store has no frozen authoring proof.")
    return {"actualBroadcastCutsRecovered": 4, "wholeStoresVerified": 8, "deliveryTimeMatchingPreserved": True, "emptyAndRearmedDecisionsPreserved": True,
        "frozenAuthoringPreserved": True, "sourceRevision": source_revision, "allLegacyFamiliesRecovered": False, "studioAcceptanceProved": False, "directModelBroadcastProved": False}


def resume_broadcast_upgrade(args) -> dict:
    baseline = args.manifest.resolve(strict=True); inputs = verify_broadcast_inputs(baseline)
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True, timeout=5).strip()
    if subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=all"], cwd=REPO, text=True, timeout=5).strip(): raise ValueError("Commit the qualification source before recording exact-source upgrade evidence.")
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False); env = os.environ.copy()
    env.update(ELSA_BPMN_LEGACY_MATRIX_DIRECTORY=str(baseline.parent), ELSA_BPMN_HOST_EVIDENCE_DIRECTORY=str(output), ELSA_BPMN_HOST_SOURCE_REVISION=source)
    command = ["dotnet", "test", str(REPO / "Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj"), "-c", "Release", "-m:2", "-p:RestoreLockedMode=true", "-p:ElsaBpmnRecoveryProbe=false", "--filter", "FullyQualifiedName~LegacyBroadcastUpgradeTests", "--logger", "trx;LogFileName=legacy-broadcast-upgrade.trx"]
    if args.artifacts_path is not None: command += ["--artifacts-path", str(args.artifacts_path.resolve())]
    with (output / "run.log").open("w") as log: subprocess.run(command, cwd=REPO, env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=3600)
    if inputs != verify_broadcast_inputs(baseline): raise ValueError("Broadcast upgrade changed an original input.")
    with tempfile.TemporaryDirectory(prefix="elsa-broadcast-upgraded-design-") as temporary:
        database = Path(temporary) / "design.sqlite"; database.write_bytes(gzip.decompress((output / "legacy-broadcast/before-selection-finished.sqlite.gz").read_bytes()))
        subprocess.run([sys.executable, str(REPO / "e2e-tests/bpmn/verify_published_contracts.py"), "--database", str(database), "--source-revision", source, "--closed-snapshot", "--output", str(output / "frozen-contract.json")], check=True, timeout=60)
    names = ["legacy-broadcast/process.json", "frozen-contract.json"] + ["legacy-broadcast/" + cut + "-" + phase + ".sqlite.gz" for cut in ("before-selection", "after-cohort", "after-resume", "empty-cohort") for phase in ("recovered", "finished")]
    write_json(output / "manifest.json", {"schemaVersion": 1, "kind": "actual-legacy-broadcast-upgrade", "sourceRevision": source,
        "baselineManifestSha256": digest(baseline), "baselineAssets": inputs, "assets": [{"path": name, "sha256": digest(output / name)} for name in names]})
    return verify_broadcast_upgrade(output / "manifest.json")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    phases = parser.add_subparsers(dest="phase", required=True)
    prepare = phases.add_parser("capture-baseline")
    for name in ("assembly", "content-root", "owner-manifest", "product-versions", "output"):
        prepare.add_argument("--" + name, type=Path, required=True)
    prepare.add_argument("--base-url", required=True)
    verify = phases.add_parser("verify-capture")
    verify.add_argument("--manifest", type=Path, required=True)
    upgrade = phases.add_parser("resume-upgrade")
    upgrade.add_argument("--manifest", type=Path, required=True)
    upgrade.add_argument("--output", type=Path, required=True)
    upgrade.add_argument("--artifacts-path", type=Path)
    upgrade.add_argument("--family", choices=("partial-forks", "native-parents", "called-ack", "broadcast"), default="partial-forks")
    verified_upgrade = phases.add_parser("verify-upgrade")
    verified_upgrade.add_argument("--manifest", type=Path, required=True)
    args = parser.parse_args()
    try:
        if args.phase == "capture-baseline": result = capture(args)
        elif args.phase == "resume-upgrade": result = resume_upgrade(args)
        elif args.phase == "verify-upgrade": result = verify_upgrade(args.manifest.resolve(strict=True))
        else: result = verify_capture(args.manifest.resolve(strict=True))
        print(json.dumps(result, indent=2))
        return 0
    except (OSError, ValueError, KeyError, sqlite3.Error, subprocess.SubprocessError, TimeoutError, RuntimeError) as error:
        print(f"Capture unavailable: {type(error).__name__}: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
