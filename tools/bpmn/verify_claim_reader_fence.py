#!/usr/bin/env python3
"""Run an actual pre-claim Workbench against captured claim-bearing stores and prove it refuses them unchanged.

The claimed stores come from BpmnHumanClaimUpgradeTests with ELSA_BPMN_HOST_EVIDENCE_DIRECTORY. The predecessor
binary is built from a pre-claim checkout; its own configuration composes the same direct BPMN host profile.
The witness owns and stops only its child, and compares the model execution/input rows before and after.
"""
from __future__ import annotations

import argparse
import gzip
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request

_spec = importlib.util.spec_from_file_location("qualify_hosting", Path(__file__).with_name("qualify-hosting.py"))
_qualification = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_qualification)
Api, REPO, digest, write_json = _qualification.Api, _qualification.REPO, _qualification.digest, _qualification.write_json
_controller = importlib.util.spec_from_file_location("owned_studio_upgrade", Path(__file__).with_name("run_owned_studio_upgrade.py"))
_owned = importlib.util.module_from_spec(_controller)
_controller.loader.exec_module(_owned)
MODEL_TABLES = ("elsa_runtime_model_execution", "elsa_runtime_model_input")


def model_rows(database: Path) -> dict:
    # A private restored copy: only SELECT, through SQLite, so committed WAL pages are included.
    connection = sqlite3.connect(database)
    try:
        return {table: hashlib.sha256(json.dumps(connection.execute(f'SELECT * FROM "{table}" ORDER BY 1').fetchall(), default=str).encode()).hexdigest()
                for table in MODEL_TABLES}
    finally:
        connection.close()


def call(api, method: str, path: str, value=None) -> dict:
    """Record the actual status and a bounded copy of the owned host's refusal body; no login or cookie data is kept."""
    data = None if value is None else json.dumps(value).encode()
    request = urllib.request.Request(api.base + path, data=data, method=method, headers={"Content-Type": "application/json"})
    try:
        with api.opener.open(request, timeout=30) as response:
            return {"status": response.status, "body": json.load(response)}
    except urllib.error.HTTPError as error:
        return {"status": error.code, "problem": error.read()[:2048].decode("utf-8", "replace")}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--predecessor-checkout", type=Path, required=True)
    parser.add_argument("--predecessor-workbench", type=Path, required=True)
    parser.add_argument("--reader-assembly", type=Path, required=True)
    parser.add_argument("--claims-evidence", type=Path, required=True, help="The evidence directory containing claims/claims-host.json.")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    checkout = args.predecessor_checkout.resolve(strict=True)
    predecessor = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=checkout, text=True).strip()
    current = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
    subprocess.run(["git", "merge-base", "--is-ancestor", predecessor, current], cwd=REPO, check=True)
    if "ClaimsProfile" in (checkout / "Essentials/src/Activities/Bpmn/Core/BpmnModelDefinition.cs").read_text():
        raise ValueError("The predecessor checkout already contains the claim writer.")
    products = json.loads(subprocess.check_output(["dotnet", str(args.reader_assembly.resolve(strict=True)), "--inspect-host-products",
        str(args.predecessor_workbench.resolve(strict=True))], text=True))
    if products["sourceCommit"] != predecessor:
        raise ValueError("Build the predecessor Workbench from the named pre-claim checkout.")
    record = json.loads((args.claims_evidence / "claims" / "claims-host.json").read_text())
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    root = Path(tempfile.mkdtemp(prefix="elsa-claim-fence-")); host_root = root / "workbench"; host_root.mkdir()
    restored = {}
    for name, store in (("claimed-runtime.sqlite.gz", "runtime"), ("claimed-design.sqlite.gz", "design")):
        snapshot = next(item for item in record["snapshots"] if item["file"] == name)
        source = args.claims_evidence / "claims" / name
        if digest(source) != snapshot["gzipSha256"]: raise ValueError("A claimed snapshot differs from its capture: " + name)
        with gzip.open(source, "rb") as compressed, (host_root / f"{store}.db").open("wb") as raw: shutil.copyfileobj(compressed, raw)
        if digest(host_root / f"{store}.db") != snapshot["databaseSha256"]: raise ValueError("A restored store differs from its capture: " + store)
        restored[store] = snapshot["databaseSha256"]
    # The predecessor's own configuration, with the same single-node direct profile as the capture.
    for name in ("appsettings.json", "appsettings.Development.json", "shells.json"):
        shutil.copyfile(checkout / "Essentials/apps/Elsa.Workbench" / name, host_root / name)
    (host_root / "packages").mkdir()
    url = f"http://127.0.0.1:{_owned.free_port()}"
    shells = json.loads((host_root / "shells.json").read_text())
    features = shells["CShells"]["Shells"]["default"]["Features"]
    features["ActivitiesBpmnHosting"] = {"AdmitNewExecutions": True}
    features["ActivitiesBpmnHostingApi"] = {}
    for name in ("WorkflowsRuntimeDistributed", "WorkflowsRuntimeDistributedEntityFrameworkCorePersistence", "WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence"):
        features.pop(name, None)
    features["WorkflowsRuntimeEntityFrameworkCore"]["ConnectionString"] = "Data Source=runtime.db"
    for name in ("ActivitiesDesignEntityFrameworkCore", "WorkflowsDesignEntityFrameworkCore", "WorkflowsPublishingEntityFrameworkCore"):
        if name in features: features[name]["ConnectionString"] = "Data Source=design.db"
    write_json(host_root / "shells.json", shells)
    before = model_rows(host_root / "runtime.db")
    log = (root / "predecessor.log").open("w")
    child = subprocess.Popen(["dotnet", str(args.predecessor_workbench.resolve()), "--contentRoot", str(host_root), "--urls", url],
        cwd=host_root, env={**os.environ, "ASPNETCORE_ENVIRONMENT": "Development"}, stdout=log, stderr=subprocess.STDOUT)
    observations = {}
    try:
        deadline = time.monotonic() + 180
        while True:
            if child.poll() is not None: raise RuntimeError("The predecessor host exited before readiness; see its private log.")
            try:
                with urllib.request.urlopen(url + "/health/ready", timeout=2) as response:
                    if response.status == 200: break
            except (OSError, TimeoutError): pass
            if time.monotonic() > deadline: raise TimeoutError("The predecessor host did not become ready.")
            time.sleep(.2)
        api = Api(url); api.login("admin", "Password123!")
        path = "/bpmn/executions/" + urllib.parse.quote(record["executionId"], safe="")
        invocation = path + "/work/" + urllib.parse.quote(record["invocationId"], safe="")
        observations["execution"] = call(api, "GET", path)
        observations["work"] = call(api, "GET", path + "/work")
        observations["inspection"] = call(api, "GET", path + "/inspection")
        observations["legacyCompletion"] = call(api, "POST", invocation + "/complete", {"requestId": "predecessor-complete", "attemptId": record["attemptId"]})
        observations["claim"] = call(api, "POST", invocation + "/claim", {"requestId": "predecessor-claim", "attemptId": record["attemptId"]})
    finally:
        if child.poll() is None:
            # Popen still owns this exact child; no PID search or unrelated process is stopped.
            child.terminate()
            try: child.wait(timeout=30)
            except subprocess.TimeoutExpired: child.kill(); child.wait(timeout=10)
        log.close()
    after = model_rows(host_root / "runtime.db")
    refused = {name: not 200 <= value["status"] < 300 for name, value in observations.items() if name != "execution"}
    passed = all(refused.values()) and before == after
    write_json(output / "claim-reader-fence.json", {"passed": passed, "predecessorSource": predecessor, "currentSource": current,
        "predecessorProducts": products["actualProductVersions"], "restoredDatabaseSha256": restored, "executionId": record["executionId"],
        "observations": observations, "refused": refused, "modelRowsBefore": before, "modelRowsAfter": after, "modelRowsUnchanged": before == after,
        "headerProjectionNote": "The execution header is projected without parsing the private claim frame; it discloses no work or claim.",
        "predecessorLogSha256": digest(root / "predecessor.log")})
    if not passed: raise SystemExit("The predecessor host accepted claim-bearing state or changed it.")
    print("Predecessor host refused the claim-bearing work, inspection, completion and claim; model rows unchanged.")


if __name__ == "__main__":
    main()
