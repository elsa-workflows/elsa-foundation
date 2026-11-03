#!/usr/bin/env python3
"""Spec 1197 actual-host journey: keyed SQL delivery to PostgreSQL on an owned Workbench.

Starts its own PostgreSQL 16 container and its own Workbench on an ephemeral loopback port, publishes two JSON Lines → SQL
pipelines through the design API and runs each from a native Flowchart's RunEtl activity. The first inserts four keyed
rows; the second updates two of them and inserts a fifth. It checks the native outcome port, the table, one ledger row per
batch, and the runtime inspection. Derived from spec 1191's driver, which stays unchanged as that spec's evidence. The
controller stops only the container and the Workbench it started.
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

# The first run inserts four rows; the second updates ids 2 and 3 and inserts id 5 (two partitions, one record per batch).
FIRST = b'{"id":1,"name":"Ada"}\n{"id":2,"name":"Bram"}\n{"id":3,"name":"Cleo"}\n{"id":4,"name":"Dee"}\n'
SECOND = b'{"id":2,"name":"Bram II"}\n{"id":3,"name":"Cleo II"}\n{"id":5,"name":"Eve"}\n'


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


def literal(key: str, value) -> dict:
    return {"referenceKey": key, "value": {"value": value, "expressionType": "Literal"}}


def pipeline(case: str) -> dict:
    return {
        "source": {"kind": "jsonl", "binding": f"{case}-source", "fields": [
            {"name": "id", "pointer": "/id", "type": "int64", "required": True},
            {"name": "name", "pointer": "/name", "type": "string", "required": True}]},
        "lookup": None,
        "routes": {"main": {"kind": "sql", "binding": "warehouse", "table": "people", "key": ["id"]},
                   "errors": {"kind": "jsonl", "binding": f"{case}-errors"}},
        "mapping": [{"name": name, "origin": "source", "field": name} for name in ("id", "name")],
        "filters": [],
        "limits": {"filePartitionCount": 2, "maxPartitions": 8, "maxRecordBytes": 65536, "maxFields": 64, "maxBatchRecords": 1,
                   "maxBatchBytes": 4194304, "maxOutstandingRecords": 100, "maxOutstandingBytes": 4194304, "maxLookupRecords": 100,
                   "maxLookupBytes": 4194304, "maxOutputBatchBytes": 4194304, "maxRetainedOutputBytes": 16777216,
                   "maxSnapshotBytes": 16777216, "maxArtifactBytes": 262144},
        "http": {"maxPageBytes": 65536, "maxPageRecords": 100, "maxJsonDepth": 16, "maxContinuationBytes": 8192,
                 "maxPagesPerPartition": 4, "maxRetainedInputBytes": 4194304, "maxRetainedMetadataBytes": 4194304,
                 "maxAttempts": 3, "deadlineSeconds": 300, "requestTimeoutSeconds": 10, "initialDelaySeconds": 1, "maxDelaySeconds": 60}}


def psql(container: str, sql: str) -> list[str]:
    result = subprocess.run(["docker", "exec", container, "psql", "-U", "postgres", "-d", "warehouse", "-At", "-F", "|", "-c", sql],
                            capture_output=True, text=True, check=True)
    return [line for line in result.stdout.splitlines() if line]


def start_postgres() -> tuple[str, int]:
    """Starts an owned PostgreSQL 16 container on an ephemeral loopback port; never touches another instance."""
    name = "elsa-etl-sql-journey-" + uuid.uuid4().hex[:8]
    subprocess.run(["docker", "run", "-d", "--rm", "--name", name, "-e", "POSTGRES_PASSWORD=postgres", "-e", "POSTGRES_DB=warehouse",
                    "-p", "127.0.0.1::5432", "postgres:16-alpine"], capture_output=True, text=True, check=True)
    port = int(subprocess.check_output(["docker", "port", name, "5432/tcp"], text=True).strip().rsplit(":", 1)[1])
    wait_for(lambda: subprocess.run(["docker", "exec", name, "pg_isready", "-U", "postgres", "-d", "warehouse"],
                                    capture_output=True).returncode == 0, bool, "PostgreSQL did not become ready.", 120)
    # pg_isready can answer before the init script's restart; require a real query.
    wait_for(lambda: subprocess.run(["docker", "exec", name, "psql", "-U", "postgres", "-d", "warehouse", "-c", "SELECT 1"],
                                    capture_output=True).returncode == 0, bool, "PostgreSQL did not accept queries.", 120)
    return name, port


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workbench-assembly", type=Path, default=DEFAULT_ASSEMBLY)
    parser.add_argument("--output", type=Path, required=True, help="A new directory for the derived summary and logs.")
    parser.add_argument("--port", type=int, default=0, help="A free loopback port; 0 lets the host choose. Never pass one you do not own.")
    parser.add_argument("--studio-origin", help="An owned Studio origin allowed to call and sign in to this Workbench.")
    parser.add_argument("--keep-host", action="store_true", help="Leave the owned Workbench running for a browser observation.")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    assembly = args.workbench_assembly.resolve(strict=True)
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=REPO, text=True).strip())

    database, database_port = start_postgres()
    psql(database, "CREATE TABLE people (id bigint NOT NULL, name text NOT NULL, CONSTRAINT people_key UNIQUE (id))")
    connection_string = f"Host=127.0.0.1;Port={database_port};Database=warehouse;Username=postgres;Password=postgres;Pooling=false"

    root = Path(tempfile.mkdtemp(prefix="elsa-etl-sql-journey-"))
    for name in ("appsettings.json", "appsettings.Development.json", "shells.json"):
        shutil.copyfile(REPO / "Essentials/apps/Elsa.Workbench" / name, root / name)
    (root / "etl-snapshots").mkdir()
    cases = {"first": FIRST, "second": SECOND}
    bindings = []
    for case, data in cases.items():
        (root / f"{case}.jsonl").write_bytes(data)
        (root / f"{case}-errors").mkdir()
        bindings.append({"TenantId": "default", "PersistencePartition": "default", "Alias": f"{case}-errors", "Role": "ErrorOutput",
                         "Revision": "v1", "Path": str(root / f"{case}-errors"), "IsCurrent": True, "ImmutableInput": False, "AtomicPublication": True})
        bindings.append({"TenantId": "default", "PersistencePartition": "default", "Alias": f"{case}-source", "Role": "Source",
                         "Revision": "v1", "Path": str(root / f"{case}.jsonl"), "ExpectedSha256": sha256(data), "IsCurrent": True,
                         "ImmutableInput": True, "AtomicPublication": False})
    sql_bindings = [{"TenantId": "default", "PersistencePartition": "default", "Alias": "warehouse", "Role": "MainOutput", "Revision": "v1",
                     "Provider": "postgresql", "ConnectionString": connection_string, "AllowedTables": ["people"]}]
    shells = json.loads((root / "shells.json").read_text())
    shells["CShells"]["Shells"]["default"]["Features"].update({
        "EtlDesign": {}, "EtlDesignEntityFrameworkCore": {"Provider": "Sqlite", "ConnectionString": "Data Source=etl-design.db;Pooling=False"},
        "EtlDesignApi": {"CursorSigningKey": "owned-etl-sql-design-cursor-signing-key"},
        "EtlRuntime": {"SnapshotRoot": str(root / "etl-snapshots"), "FileBindings": bindings, "SqlBindings": sql_bindings},
        "EtlSqlPostgreSql": {},
        "EtlRuntimeEntityFrameworkCore": {"Provider": "Sqlite", "ConnectionString": "Data Source=etl-runtime.db;Pooling=False"},
        "EtlRuntimeApi": {"CursorSigningKey": "owned-etl-sql-runtime-cursor-signing-key"},
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
    summary = {"schemaVersion": 1, "spec": "1197-etl-sql-destination", "sourceRevision": source, "workingTreeDirty": dirty,
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
        summary["catalogSqlBindings"] = sorted((item["alias"], item["role"], item["kind"]) for item in catalog if item["kind"] == "sql")
        run_etl = api.activity("Elsa.Etl.Workflows.Runtime.Activities.RunEtl")
        write_line = api.activity("Elsa.Activities.Primitives.Activities.WriteLine")
        flowchart = api.activity("Elsa.Activities.Flowchart.Activities.Flowchart")

        for case in cases:
            definition = f"sql-{case}-{uuid.uuid4().hex[:8]}"
            draft = api.request("/etl/v2/definitions", {"id": definition, "pipeline": pipeline(case)})
            validation = api.request(f"/etl/v2/definitions/{definition}/validate", {"expectedRevision": draft["revision"]})
            if validation["diagnostics"]:
                raise ValueError(f"{case}: the SQL definition did not validate: {validation['diagnostics']}")
            published = api.request(f"/etl/v2/definitions/{definition}/publish", {
                "expectedRevision": validation["revision"], "definitionVersion": validation["definitionVersion"],
                "contentHash": validation["contentHash"]})
            selection = published["version"]["selection"]

            def node(identity, version, inputs=None, structure=None):
                return {"nodeId": identity, "activityVersionId": version, "inputs": inputs or [], "outputs": [], "structure": structure}
            etl = node("etl", run_etl, [literal("Pipeline", selection)])
            done = node("after-done", write_line, [literal("text", f"SQL delivery {case} succeeded")])
            business = node("after-business-error", write_line, [literal("text", f"SQL delivery {case} rejected records")])
            root_node = node("root", flowchart, structure={"kind": "elsa.flowchart.structure", "schemaVersion": "1.0.0", "payload": {
                "startNodeId": "etl", "activities": [etl, done, business], "connections": [
                    {"source": {"nodeId": "etl", "port": "Done"}, "target": {"nodeId": "after-done", "port": "Done"}},
                    {"source": {"nodeId": "etl", "port": "BusinessError"}, "target": {"nodeId": "after-business-error", "port": "Done"}}]}})
            native = api.request("/design/workflows/definitions/submit", {"name": f"Spec 1197 SQL delivery {case} {definition}",
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
            detail = api.request("/etl/runs/" + urllib.parse.quote(run_id, safe=""))
            summary["cases"][case] = {
                "definition": definition, "selection": selection, "workflowStatus": view["instance"]["status"],
                "incidents": len(view.get("incidents") or []), "executedNodes": sorted(ran),
                "run": {key: detail.get(key) for key in ("status", "sourceRecordCount", "acceptedRecords", "rejectedRecords",
                                                          "outstandingRecords", "outstandingBytes")},
                "connector": detail.get("connector"),
                "table": psql(database, "SELECT id, name FROM people ORDER BY id"),
                "ledgerRowsForRun": int(psql(database, f"SELECT COUNT(*) FROM elsa_etl_effect_ledger WHERE run_id = '{run_id}'")[0]),
                "hostPathOrSecretInInspection": str(root) in json.dumps(detail) or "Password" in json.dumps(detail)}

        first, second = summary["cases"]["first"], summary["cases"]["second"]
        done = lambda case: case["workflowStatus"] in ("Completed", "Finished") and case["incidents"] == 0 \
            and "after-done" in case["executedNodes"] and "after-business-error" not in case["executedNodes"]
        checks = {
            "catalogListsTheSqlBinding": summary["catalogSqlBindings"] == [("warehouse", "MainOutput", "sql")],
            "firstRunInsertedEveryRowThroughDone": done(first) and first["run"]["status"] in ("succeeded", "Succeeded")
                and first["run"]["acceptedRecords"] == 4 and first["table"] == ["1|Ada", "2|Bram", "3|Cleo", "4|Dee"],
            "secondRunUpdatedAndInsertedThroughDone": done(second) and second["run"]["status"] in ("succeeded", "Succeeded")
                and second["table"] == ["1|Ada", "2|Bram II", "3|Cleo II", "4|Dee", "5|Eve"],
            "oneLedgerRowPerBatch": first["ledgerRowsForRun"] == 4 and second["ledgerRowsForRun"] == 3,
            "inspectionShowsTheSqlRoute": (first["connector"] or {}).get("mainKind") == "sql",
            "noAccountingLeft": all(c["run"]["outstandingRecords"] == 0 and c["run"]["outstandingBytes"] == 0 for c in (first, second)),
            "noHostPathOrSecretInInspection": not first["hostPathOrSecretInInspection"] and not second["hostPathOrSecretInInspection"],
        }
        summary["checks"] = {name: bool(value) for name, value in checks.items()}
        summary["passed"] = all(summary["checks"].values())
        (output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
        print(json.dumps({"passed": summary["passed"], "checks": summary["checks"]}, indent=2))
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
        subprocess.run(["docker", "rm", "-f", database], capture_output=True)


if __name__ == "__main__":
    raise SystemExit(main())
