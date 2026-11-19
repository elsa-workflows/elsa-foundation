#!/usr/bin/env python3
"""Spec 1190 actual-host journey: JSON Lines file input on an owned Workbench.

Publishes two JSON Lines pipelines through the design API, runs each from a native
Flowchart's RunEtl activity, and checks the native outcome port, the accepted output
files, the routed errors and the runtime inspection. The controller starts its own
Workbench on an ephemeral loopback port and stops only that process.
"""
from __future__ import annotations

import argparse
import hashlib
import http.cookiejar
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

REPO = Path(__file__).resolve().parents[3]
DEFAULT_ASSEMBLY = REPO / "Essentials/apps/Elsa.Workbench/bin/Debug/net10.0/Elsa.Workbench.dll"

# Two partitions; a BOM, a blank line, CRLF and an unterminated last line exercise the framing rules.
CLEAN = b"\xef\xbb\xbf" + b'{"id":1,"name":"Ada"}\n\n{"id":2,"name":"Bram"}\r\n{"id":3,"name":"Cleo"}\n{"id":4,"name":"Dee"}'
# One record per line-error class plus two valid records.
DIRTY = (b'{"id":1,"name":"Ada"}\n{"id":\n[1,2]\n{"id":"x","name":"B"}\n{"name":"C"}\n'
         b'{"id":6,"id":7,"name":"D"}\n{"id":7,"name":"Eve"}\n')


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def wait_for(action, predicate, message: str, seconds: float = 120):
    deadline = time.monotonic() + seconds
    while True:
        value = action()
        if predicate(value):
            return value
        if time.monotonic() > deadline:
            raise TimeoutError(message)
        time.sleep(0.25)


class Api:
    def __init__(self, base: str):
        parsed = urllib.parse.urlsplit(base)
        if parsed.scheme != "http" or parsed.hostname != "127.0.0.1":
            raise ValueError("The journey only talks to its own loopback Workbench.")
        self.base = base.rstrip("/")
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def request(self, path: str, value=None):
        data = None if value is None else json.dumps(value).encode()
        request = urllib.request.Request(self.base + path, data=data, headers={"Content-Type": "application/json"})
        try:
            with self.opener.open(request, timeout=60) as response:
                raw = response.read()
                return json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            raise RuntimeError(f"Owned-host request {path} returned HTTP {error.code}.") from None

    def activity(self, key: str) -> str:
        result = self.request("/design/activities/definitions?" + urllib.parse.urlencode({"search": key}))
        hits = [item for item in result["items"] if item["definition"]["activityTypeKey"] == key]
        if len(hits) != 1:
            raise ValueError("Expected one visible activity definition: " + key)
        return hits[0]["definition"]["headVersionId"]


def pipeline(case: str) -> dict:
    return {
        "source": {"kind": "jsonl", "binding": f"{case}-source", "fields": [
            {"name": "id", "pointer": "/id", "type": "int64", "required": True},
            {"name": "name", "pointer": "/name", "type": "string", "required": True}]},
        "lookup": None,
        "routes": {"main": {"kind": "jsonl", "binding": f"{case}-output"}, "errors": {"kind": "jsonl", "binding": f"{case}-errors"}},
        "mapping": [{"name": name, "origin": "source", "field": name} for name in ("id", "name")],
        "filters": [],
        "limits": {"filePartitionCount": 2, "maxPartitions": 8, "maxRecordBytes": 65536, "maxFields": 64, "maxBatchRecords": 2,
                   "maxBatchBytes": 4194304, "maxOutstandingRecords": 100, "maxOutstandingBytes": 4194304, "maxLookupRecords": 100,
                   "maxLookupBytes": 4194304, "maxOutputBatchBytes": 4194304, "maxRetainedOutputBytes": 16777216,
                   "maxSnapshotBytes": 16777216, "maxArtifactBytes": 262144},
        "http": {"maxPageBytes": 65536, "maxPageRecords": 100, "maxJsonDepth": 16, "maxContinuationBytes": 8192,
                 "maxPagesPerPartition": 4, "maxRetainedInputBytes": 4194304, "maxRetainedMetadataBytes": 4194304,
                 "maxAttempts": 3, "deadlineSeconds": 300, "requestTimeoutSeconds": 10, "initialDelaySeconds": 1, "maxDelaySeconds": 60}}


def literal(key: str, value) -> dict:
    return {"referenceKey": key, "value": {"value": value, "expressionType": "Literal"}}


def read_jsonl(directory: Path) -> list:
    records = []
    for path in sorted(directory.glob("*.jsonl")):
        records.extend(json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip())
    return records


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workbench-assembly", type=Path, default=DEFAULT_ASSEMBLY)
    parser.add_argument("--output", type=Path, required=True, help="A new directory for the derived summary and logs.")
    parser.add_argument("--keep-host", action="store_true", help="Leave the owned Workbench running for a browser observation.")
    parser.add_argument("--port", type=int, default=0, help="A free loopback port; 0 lets the host choose. Never pass one you do not own.")
    parser.add_argument("--studio-origin", help="An owned Studio origin allowed to call and sign in to this Workbench.")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    assembly = args.workbench_assembly.resolve(strict=True)
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=REPO, text=True).strip())

    root = Path(tempfile.mkdtemp(prefix="elsa-etl-jsonl-journey-"))
    for name in ("appsettings.json", "appsettings.Development.json", "shells.json"):
        shutil.copyfile(REPO / "Essentials/apps/Elsa.Workbench" / name, root / name)
    (root / "etl-snapshots").mkdir()
    cases = {"clean": CLEAN, "dirty": DIRTY}
    bindings = []
    for case, data in cases.items():
        (root / f"{case}.jsonl").write_bytes(data)
        for role, alias, path in (("MainOutput", f"{case}-output", root / f"{case}-output"),
                                  ("ErrorOutput", f"{case}-errors", root / f"{case}-errors")):
            path.mkdir()
            bindings.append({"TenantId": "default", "PersistencePartition": "default", "Alias": alias, "Role": role,
                             "Revision": "v1", "Path": str(path), "IsCurrent": True, "ImmutableInput": False, "AtomicPublication": True})
        bindings.append({"TenantId": "default", "PersistencePartition": "default", "Alias": f"{case}-source", "Role": "Source",
                         "Revision": "v1", "Path": str(root / f"{case}.jsonl"), "ExpectedSha256": sha256(data), "IsCurrent": True,
                         "ImmutableInput": True, "AtomicPublication": False})
    shells = json.loads((root / "shells.json").read_text())
    shells["CShells"]["Shells"]["default"]["Features"].update({
        "EtlDesign": {}, "EtlDesignEntityFrameworkCore": {"Provider": "Sqlite", "ConnectionString": "Data Source=etl-design.db;Pooling=False"},
        "EtlDesignApi": {"CursorSigningKey": "owned-etl-jsonl-design-cursor-signing-key"},
        "EtlRuntime": {"SnapshotRoot": str(root / "etl-snapshots"), "FileBindings": bindings},
        "EtlRuntimeEntityFrameworkCore": {"Provider": "Sqlite", "ConnectionString": "Data Source=etl-runtime.db;Pooling=False"},
        "EtlRuntimeApi": {"CursorSigningKey": "owned-etl-jsonl-runtime-cursor-signing-key"},
        "EtlWorkflowsDesign": {}, "EtlWorkflowsRuntime": {}})
    if args.studio_origin:
        identity = shells["CShells"]["Shells"]["default"]["Features"]["FoundationIdentityAspNetCoreIdentity"]
        identity["AllowedReturnUrlOrigins"] = list(identity.get("AllowedReturnUrlOrigins") or []) + [args.studio_origin]
    (root / "shells.json").write_text(json.dumps(shells, indent=2))

    log_path = output / "workbench.log"
    log = log_path.open("w")
    environment = {**os.environ, "ASPNETCORE_ENVIRONMENT": "Development"}
    if args.studio_origin:
        environment["Cors__AllowedOrigins__5"] = args.studio_origin
    host = subprocess.Popen(["dotnet", str(assembly), "--contentRoot", str(root), "--urls", f"http://127.0.0.1:{args.port}"],
                            cwd=root, env=environment, stdout=log, stderr=subprocess.STDOUT)
    summary = {"schemaVersion": 1, "spec": "1190-etl-jsonl-input", "sourceRevision": source, "workingTreeDirty": dirty,
               "workbenchAssemblySha256": sha256(assembly.read_bytes()), "hostProcessId": host.pid, "cases": {}}
    try:
        def address():
            if host.poll() is not None:
                raise RuntimeError(f"The owned Workbench exited; see {log_path}")
            lines = [line.split("Now listening on: ", 1)[1].strip() for line in log_path.read_text().splitlines() if "Now listening on: " in line]
            return lines[0] if lines else None
        url = wait_for(address, lambda value: value is not None, "The owned Workbench did not listen.", 240)
        api = Api(url)

        def ready():
            try:
                with urllib.request.urlopen(url + "/health/ready", timeout=3) as response:
                    return response.status == 200
            except urllib.error.HTTPError as error:
                if error.code == 503:
                    return False
                raise
        wait_for(ready, bool, "The owned Workbench is not ready.", 240)
        api.request("/_elsa/identity/login", {"username": "admin", "password": "Password123!"})
        summary["baseUrl"] = url

        catalog = api.request("/etl/v2/bindings?limit=100")["items"]
        summary["catalogSources"] = sorted((item["alias"], item["kind"]) for item in catalog if item["role"] == "Source")
        run_etl = api.activity("Elsa.Etl.Workflows.Runtime.Activities.RunEtl")
        write_line = api.activity("Elsa.Activities.Primitives.Activities.WriteLine")
        flowchart = api.activity("Elsa.Activities.Flowchart.Activities.Flowchart")

        for case in cases:
            definition = f"jsonl-{case}-{uuid.uuid4().hex[:8]}"
            draft = api.request("/etl/v2/definitions", {"id": definition, "pipeline": pipeline(case)})
            validation = api.request(f"/etl/v2/definitions/{definition}/validate", {"expectedRevision": draft["revision"]})
            if validation["diagnostics"]:
                raise ValueError(f"{case}: the JSON Lines definition did not validate: {validation['diagnostics']}")
            published = api.request(f"/etl/v2/definitions/{definition}/publish", {
                "expectedRevision": validation["revision"], "definitionVersion": validation["definitionVersion"],
                "contentHash": validation["contentHash"]})
            selection = published["version"]["selection"]

            def node(identity, version, inputs=None, structure=None):
                return {"nodeId": identity, "activityVersionId": version, "inputs": inputs or [], "outputs": [], "structure": structure}
            etl = node("etl", run_etl, [literal("Pipeline", selection)])
            done = node("after-done", write_line, [literal("text", f"JSON Lines {case} succeeded")])
            business = node("after-business-error", write_line, [literal("text", f"JSON Lines {case} rejected records")])
            root_node = node("root", flowchart, structure={"kind": "elsa.flowchart.structure", "schemaVersion": "1.0.0", "payload": {
                "startNodeId": "etl", "activities": [etl, done, business], "connections": [
                    {"source": {"nodeId": "etl", "port": "Done"}, "target": {"nodeId": "after-done", "port": "Done"}},
                    {"source": {"nodeId": "etl", "port": "BusinessError"}, "target": {"nodeId": "after-business-error", "port": "Done"}}]}})
            native = api.request("/design/workflows/definitions/submit", {"name": f"Spec 1190 JSON Lines {case} {definition}",
                                 "state": {"variables": [], "inputs": [], "outputs": [], "rootActivity": root_node}})
            publication = api.request("/publishing/workflows/" + native["version"]["id"] + "/publish", {})
            started = api.request("/runtime/workflows/executables/" + publication["artifactId"] + "/execute",
                                  {"sourceReferenceId": publication["sourceReferenceId"]})
            execution = started["workflowExecutionId"]
            view = wait_for(lambda: api.request("/runtime/workflows/instances/" + urllib.parse.quote(execution, safe="")),
                            lambda value: value["instance"]["status"] in ("Completed", "Finished", "Faulted", "Cancelled"),
                            f"{case}: the native workflow did not finish.", 180)
            ran = [activity["executableNodeId"] for activity in view["activities"]]
            runs = [item for item in api.request("/etl/runs?limit=100")["items"] if item.get("selection") == selection]
            if len(runs) != 1:
                raise ValueError(f"{case}: expected one ETL run for the selection, found {len(runs)}.")
            run_id = runs[0]["runId"]
            escaped = urllib.parse.quote(run_id, safe="")
            detail = api.request("/etl/runs/" + escaped)
            partitions = api.request("/etl/runs/" + escaped + "/connector-partitions?limit=50")["items"]
            main = read_jsonl(root / f"{case}-output")
            errors = read_jsonl(root / f"{case}-errors")
            summary["cases"][case] = {
                "definition": definition, "selection": selection, "workflowStatus": view["instance"]["status"],
                "incidents": len(view.get("incidents") or []), "executedNodes": sorted(ran),
                "run": {key: detail.get(key) for key in ("status", "sourceRecordCount", "acceptedRecords", "rejectedRecords",
                                                          "filteredRecords", "outstandingRecords", "outstandingBytes")},
                "terminalDiagnostic": (detail.get("terminalResult") or {}).get("diagnostic"),
                "connector": detail.get("connector"), "sourceInput": detail.get("sourceInput"),
                "partitions": [{"ordinal": row["partitionOrdinal"], "file": row["file"], "claimed": row["isClaimed"]} for row in partitions],
                "mainRecords": sorted(main, key=lambda record: record["id"]),
                "errorCodes": sorted(json.dumps(record, sort_keys=True) for record in errors),
                "hostPathInInspection": str(root) in json.dumps(detail)}

        clean, dirty_case = summary["cases"]["clean"], summary["cases"]["dirty"]
        checks = {
            "catalogListsSourceFilesAsCsv": all(kind == "csv" for _alias, kind in summary["catalogSources"]),
            "cleanWorkflowCompletedThroughDone": clean["workflowStatus"] in ("Completed", "Finished") and clean["incidents"] == 0
                and "after-done" in clean["executedNodes"] and "after-business-error" not in clean["executedNodes"],
            "cleanRunSucceededWithAllRecords": clean["run"]["status"] in ("succeeded", "Succeeded") and clean["run"]["acceptedRecords"] == 4
                and clean["run"]["sourceRecordCount"] == 4 and clean["mainRecords"] == [
                    {"id": 1, "name": "Ada"}, {"id": 2, "name": "Bram"}, {"id": 3, "name": "Cleo"}, {"id": 4, "name": "Dee"}],
            "cleanInspectionShowsJsonlAndTwoFinishedRanges": (clean["connector"] or {}).get("sourceKind") == "jsonl"
                and clean["sourceInput"] is not None and [p["file"] for p in clean["partitions"]] == [
                    {"startOrdinal": 1, "endOrdinalExclusive": 3, "nextOrdinal": 3},
                    {"startOrdinal": 3, "endOrdinalExclusive": 5, "nextOrdinal": 5}],
            "dirtyWorkflowTookBusinessErrorPort": dirty_case["workflowStatus"] in ("Completed", "Finished")
                and "after-business-error" in dirty_case["executedNodes"] and "after-done" not in dirty_case["executedNodes"],
            "dirtyRunRoutedEachBadLine": dirty_case["run"]["status"] in ("businessError", "BusinessError")
                and dirty_case["run"]["acceptedRecords"] == 2 and dirty_case["run"]["rejectedRecords"] == 5
                and [r["name"] for r in dirty_case["mainRecords"]] == ["Ada", "Eve"] and len(dirty_case["errorCodes"]) == 5,
            "noAccountingLeft": all(c["run"]["outstandingRecords"] == 0 and c["run"]["outstandingBytes"] == 0 for c in (clean, dirty_case)),
            "noHostPathInInspection": not clean["hostPathInInspection"] and not dirty_case["hostPathInInspection"],
        }
        summary["checks"] = checks
        summary["passed"] = all(checks.values())
        (output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
        print(json.dumps({"passed": summary["passed"], "checks": checks}, indent=2))
        if args.keep_host and summary["passed"]:
            print(f"Owned Workbench left running at {url} (pid {host.pid}); stop it with: kill {host.pid}")
            host = None
        return 0 if summary["passed"] else 1
    finally:
        if host is not None and host.poll() is None:
            host.terminate()
            try:
                host.wait(timeout=30)
            except subprocess.TimeoutExpired:
                host.kill()
                host.wait(timeout=10)
        log.close()


if __name__ == "__main__":
    raise SystemExit(main())
