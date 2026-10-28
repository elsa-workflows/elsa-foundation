#!/usr/bin/env python3
"""Capture actual owned-host legacy BPMN inputs before production writer changes.

Uses public APIs to park partial forks in the legacy BPMN activity and native parents.
This is baseline capture, not direct-model hosting or upgraded-host recovery proof.
The resulting manifest contains only relative evidence paths and binary/source identities.
"""
from __future__ import annotations

import argparse
from contextlib import closing, contextmanager
from datetime import datetime, timezone
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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    phases = parser.add_subparsers(dest="phase", required=True)
    prepare = phases.add_parser("capture-baseline")
    for name in ("assembly", "content-root", "owner-manifest", "product-versions", "output"):
        prepare.add_argument("--" + name, type=Path, required=True)
    prepare.add_argument("--base-url", required=True)
    verify = phases.add_parser("verify-capture")
    verify.add_argument("--manifest", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = capture(args) if args.phase == "capture-baseline" else verify_capture(args.manifest.resolve(strict=True))
        print(json.dumps(result, indent=2))
        return 0
    except (OSError, ValueError, KeyError, sqlite3.Error, subprocess.SubprocessError, TimeoutError, RuntimeError) as error:
        print(f"Capture unavailable: {type(error).__name__}: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
