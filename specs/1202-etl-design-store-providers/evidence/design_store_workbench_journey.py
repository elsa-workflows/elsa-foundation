#!/usr/bin/env python3
"""Spec 1202 actual-host journey: the ETL design store on PostgreSQL, SQL Server and MySQL, behind an owned Workbench.

For each engine it starts its own database container and its own Workbench on ephemeral loopback ports, with the ETL design
store on that engine and the ETL runtime on SQLite. Through the HTTP API it creates two definitions whose ids differ only by
case, validates and publishes one, and runs the published version from a native Flowchart over a JSON Lines file. It then
stops the host, starts a new one on the same database, and reads the draft, the publication and the catalog back. The
controller stops only what it started; container secrets are generated per run.
"""
from __future__ import annotations

import argparse
import hashlib
import http.cookiejar
import json
import os
import secrets
import shutil
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
DEFAULT_ASSEMBLY = REPO / "Essentials/apps/Elsa.Workbench/bin/Debug/net10.0/Elsa.Workbench.dll"
RECORDS = [{"id": 1, "name": "Ada"}, {"id": 2, "name": "Bram"}, {"id": 3, "name": "Cleo"}]
MYSQL_IMAGE = "mysql:8.4.11@sha256:85b9bf2e29cf836ecb8c2a15a935d4ba0c606631dff1dd79531a11983c638f2a"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def wait_for(action, predicate, message: str, seconds: float = 180):
    deadline = time.monotonic() + seconds
    while True:
        value = action()
        if predicate(value):
            return value
        if time.monotonic() > deadline:
            raise TimeoutError(message)
        time.sleep(1)


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


def pipeline() -> dict:
    return {
        "source": {"kind": "jsonl", "binding": "people-source", "fields": [
            {"name": "id", "pointer": "/id", "type": "int64", "required": True},
            {"name": "name", "pointer": "/name", "type": "string", "required": True}]},
        "lookup": None,
        "routes": {"main": {"kind": "jsonl", "binding": "people-output"}, "errors": {"kind": "jsonl", "binding": "people-errors"}},
        "mapping": [{"name": name, "origin": "source", "field": name} for name in ("id", "name")],
        "filters": [],
        "limits": {"filePartitionCount": 2, "maxPartitions": 8, "maxRecordBytes": 65536, "maxFields": 64, "maxBatchRecords": 2,
                   "maxBatchBytes": 4194304, "maxOutstandingRecords": 100, "maxOutstandingBytes": 4194304, "maxLookupRecords": 100,
                   "maxLookupBytes": 4194304, "maxOutputBatchBytes": 4194304, "maxRetainedOutputBytes": 16777216,
                   "maxSnapshotBytes": 16777216, "maxArtifactBytes": 262144},
        "http": {"maxPageBytes": 65536, "maxPageRecords": 100, "maxJsonDepth": 16, "maxContinuationBytes": 8192,
                 "maxPagesPerPartition": 4, "maxRetainedInputBytes": 4194304, "maxRetainedMetadataBytes": 4194304,
                 "maxAttempts": 3, "deadlineSeconds": 300, "requestTimeoutSeconds": 10, "initialDelaySeconds": 1, "maxDelaySeconds": 60}}


class Database:
    """An owned database container for one engine; the secret is generated per run so none is committed."""

    def __init__(self, provider: str):
        self.provider = provider
        self.name = f"elsa-etl-design-journey-{provider.lower()}-{uuid.uuid4().hex[:8]}"
        self.secret = "Aa1!" + secrets.token_hex(16)
        image, port, env = {
            "PostgreSql": ("postgres:16-alpine", "5432", {"POSTGRES_PASSWORD": self.secret, "POSTGRES_DB": "elsa_etl"}),
            "SqlServer": ("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04", "1433",
                          {"ACCEPT_EULA": "Y", "MSSQL_SA_PASSWORD": self.secret}),
            "MySql": (MYSQL_IMAGE, "3306", {"MYSQL_ROOT_PASSWORD": self.secret, "MYSQL_DATABASE": "elsa_etl"}),
        }[provider]
        command = ["docker", "run", "-d", "--rm", "--name", self.name, "-p", f"127.0.0.1::{port}"]
        for key, value in env.items():
            command += ["-e", f"{key}={value}"]
        if subprocess.run(command + [image], capture_output=True, text=True).returncode != 0:
            raise RuntimeError(f"The owned {provider} container could not start.")
        try:
            self.port = int(subprocess.check_output(["docker", "port", self.name, f"{port}/tcp"], text=True).strip().splitlines()[0].rsplit(":", 1)[1])
            wait_for(self._ready, bool, f"{provider} did not become ready.", 240)
            if provider == "SqlServer":
                if self._sqlcmd("CREATE DATABASE elsa_etl").returncode != 0:
                    # A CalledProcessError would include the generated password in its command arguments.
                    raise RuntimeError("The owned SQL Server database could not be created.")
        except BaseException as failure:
            try:
                self.stop()
            except Exception as cleanup:
                if hasattr(failure, "add_note"):
                    failure.add_note(f"Owned database cleanup failed ({type(cleanup).__name__}).")
            raise

    def _sqlcmd(self, sql: str) -> subprocess.CompletedProcess:
        return subprocess.run(["docker", "exec", self.name, "/opt/mssql-tools18/bin/sqlcmd", "-C", "-S", "localhost", "-U", "sa",
                               "-P", self.secret, "-Q", sql], capture_output=True, text=True)

    def _ready(self) -> bool:
        if self.provider == "PostgreSql":
            probe = ["docker", "exec", self.name, "pg_isready", "-U", "postgres", "-d", "elsa_etl", "-h", "127.0.0.1"]
            return subprocess.run(probe, capture_output=True).returncode == 0
        if self.provider == "SqlServer":
            return self._sqlcmd("SELECT 1").returncode == 0
        probe = ["docker", "exec", self.name, "mysql", "-h", "127.0.0.1", "-uroot", f"-p{self.secret}", "-e", "SELECT 1", "elsa_etl"]
        return subprocess.run(probe, capture_output=True).returncode == 0

    @property
    def connection_string(self) -> str:
        return {
            "PostgreSql": f"Host=127.0.0.1;Port={self.port};Database=elsa_etl;Username=postgres;Password={self.secret};Pooling=false",
            "SqlServer": f"Server=127.0.0.1,{self.port};Database=elsa_etl;User Id=sa;Password={self.secret};TrustServerCertificate=True;Encrypt=False;Pooling=false",
            "MySql": f"Server=127.0.0.1;Port={self.port};Database=elsa_etl;Uid=root;Pwd={self.secret};SslMode=Disabled;Pooling=false",
        }[self.provider]

    def tables(self) -> list[str]:
        if self.provider == "PostgreSql":
            out = subprocess.check_output(["docker", "exec", self.name, "psql", "-U", "postgres", "-d", "elsa_etl", "-At", "-c",
                                           "SELECT table_name FROM information_schema.tables WHERE table_name LIKE 'elsa_etl_%'"], text=True)
        elif self.provider == "SqlServer":
            out = self._sqlcmd("SET NOCOUNT ON; SELECT name FROM elsa_etl.sys.tables WHERE name LIKE 'elsa[_]etl[_]%'").stdout
        else:
            result = subprocess.run(["docker", "exec", self.name, "mysql", "-h", "127.0.0.1", "-uroot", f"-p{self.secret}", "-N",
                                           "-e", "SELECT table_name FROM information_schema.tables WHERE table_schema = 'elsa_etl' AND table_name LIKE 'elsa_etl_%'"],
                                    capture_output=True, text=True)
            if result.returncode != 0:
                raise RuntimeError("The owned MySQL catalog could not be read.")
            out = result.stdout
        return sorted(line.strip() for line in out.splitlines() if line.strip().startswith("elsa_etl_"))

    def stop(self) -> None:
        if subprocess.run(["docker", "rm", "-f", self.name], capture_output=True).returncode != 0:
            raise RuntimeError("Owned database container cleanup failed.")


class Host:
    """One owned Workbench process over a prepared content root."""

    def __init__(self, assembly: Path, root: Path, log_path: Path, port: int):
        self.log_path = log_path
        self.log = log_path.open("a")
        environment = {**os.environ, "ASPNETCORE_ENVIRONMENT": "Development"}
        self.process = None
        try:
            start = log_path.stat().st_size
            self.process = subprocess.Popen(["dotnet", str(assembly), "--contentRoot", str(root), "--urls", f"http://127.0.0.1:{port}"],
                                            cwd=root, env=environment, stdout=self.log, stderr=subprocess.STDOUT)

            def address():
                if self.process.poll() is not None:
                    raise RuntimeError(f"The owned Workbench exited; see {log_path}")
                text = log_path.read_text()[start:]
                lines = [line.split("Now listening on: ", 1)[1].strip() for line in text.splitlines() if "Now listening on: " in line]
                return lines[0] if lines else None
            self.url = wait_for(address, lambda value: value is not None, "The owned Workbench did not listen.", 240)

            def ready():
                try:
                    with urllib.request.urlopen(self.url + "/health/ready", timeout=3) as response:
                        return response.status == 200
                except urllib.error.HTTPError as error:
                    if error.code == 503:
                        return False
                    raise
                except urllib.error.URLError:
                    return False
            wait_for(ready, bool, "The owned Workbench is not ready.", 240)
            self.api = Api(self.url)
            self.api.request("/_elsa/identity/login", {"username": "admin", "password": "Password123!"})
        except BaseException as failure:
            try:
                self.stop()
            except Exception as cleanup:
                if hasattr(failure, "add_note"):
                    failure.add_note(f"Owned Workbench cleanup failed ({type(cleanup).__name__}).")
            raise

    def stop(self) -> None:
        try:
            if self.process is not None and self.process.poll() is None:
                self.process.terminate()
                try:
                    self.process.wait(timeout=30)
                except subprocess.TimeoutExpired:
                    self.process.kill()
                    self.process.wait(timeout=10)
        finally:
            self.log.close()


def run_engine(provider: str, assembly: Path, output: Path, port: int) -> dict:
    database = None
    host = None
    try:
        database = Database(provider)
        root = Path(tempfile.mkdtemp(prefix=f"elsa-etl-design-{provider.lower()}-"))
        for name in ("appsettings.json", "appsettings.Development.json", "shells.json"):
            shutil.copyfile(REPO / "Essentials/apps/Elsa.Workbench" / name, root / name)
        (root / "etl-snapshots").mkdir()
        data = "".join(json.dumps(record) + "\n" for record in RECORDS).encode()
        (root / "people.jsonl").write_bytes(data)
        bindings = [{"TenantId": "default", "PersistencePartition": "default", "Alias": "people-source", "Role": "Source", "Revision": "v1",
                     "Path": str(root / "people.jsonl"), "ExpectedSha256": sha256(data), "IsCurrent": True, "ImmutableInput": True,
                     "AtomicPublication": False}]
        for route, role in (("output", "MainOutput"), ("errors", "ErrorOutput")):
            (root / f"people-{route}").mkdir()
            bindings.append({"TenantId": "default", "PersistencePartition": "default", "Alias": f"people-{route}", "Role": role,
                             "Revision": "v1", "Path": str(root / f"people-{route}"), "IsCurrent": True, "ImmutableInput": False,
                             "AtomicPublication": True})
        shells = json.loads((root / "shells.json").read_text())
        shells["CShells"]["Shells"]["default"]["Features"].update({
            "EtlDesign": {}, "EtlDesignEntityFrameworkCore": {"Provider": provider, "ConnectionString": database.connection_string},
            "EtlDesignApi": {"CursorSigningKey": "owned-etl-design-journey-cursor-signing-key"},
            "EtlRuntime": {"SnapshotRoot": str(root / "etl-snapshots"), "FileBindings": bindings},
            "EtlRuntimeEntityFrameworkCore": {"Provider": "Sqlite", "ConnectionString": "Data Source=etl-runtime.db;Pooling=False"},
            "EtlRuntimeApi": {"CursorSigningKey": "owned-etl-design-journey-runtime-cursor-signing-key"},
            "EtlWorkflowsDesign": {}, "EtlWorkflowsRuntime": {}})
        (root / "shells.json").write_text(json.dumps(shells, indent=2))
        log_path = output / f"workbench-{provider.lower()}.log"

        host = Host(assembly, root, log_path, port)
        api = host.api
        definition = f"people-{uuid.uuid4().hex[:8]}"
        twin = definition[0].upper() + definition[1:]
        draft = api.request("/etl/v2/definitions", {"id": definition, "pipeline": pipeline()})
        api.request("/etl/v2/definitions", {"id": twin, "pipeline": pipeline()})
        validation = api.request(f"/etl/v2/definitions/{definition}/validate", {"expectedRevision": draft["revision"]})
        published = api.request(f"/etl/v2/definitions/{definition}/publish", {
            "expectedRevision": validation["revision"], "definitionVersion": validation["definitionVersion"],
            "contentHash": validation["contentHash"]})
        selection = published["version"]["selection"]

        run_etl = api.activity("Elsa.Etl.Workflows.Runtime.Activities.RunEtl")
        write_line = api.activity("Elsa.Activities.Primitives.Activities.WriteLine")
        flowchart = api.activity("Elsa.Activities.Flowchart.Activities.Flowchart")

        def node(identity, version, inputs=None, structure=None):
            return {"nodeId": identity, "activityVersionId": version, "inputs": inputs or [], "outputs": [], "structure": structure}
        root_node = node("root", flowchart, structure={"kind": "elsa.flowchart.structure", "schemaVersion": "1.0.0", "payload": {
            "startNodeId": "etl", "activities": [node("etl", run_etl, [literal("Pipeline", selection)]),
                                                 node("after-done", write_line, [literal("text", "ETL design journey succeeded")])],
            "connections": [{"source": {"nodeId": "etl", "port": "Done"}, "target": {"nodeId": "after-done", "port": "Done"}}]}})
        native = api.request("/design/workflows/definitions/submit", {"name": f"Spec 1202 {provider} {definition}",
                             "state": {"variables": [], "inputs": [], "outputs": [], "rootActivity": root_node}})
        publication = api.request("/publishing/workflows/" + native["version"]["id"] + "/publish", {})
        started = api.request("/runtime/workflows/executables/" + publication["artifactId"] + "/execute",
                              {"sourceReferenceId": publication["sourceReferenceId"]})
        view = wait_for(lambda: api.request("/runtime/workflows/instances/" + urllib.parse.quote(started["workflowExecutionId"], safe="")),
                        lambda value: value["instance"]["status"] in ("Completed", "Finished", "Faulted", "Cancelled"),
                        f"{provider}: the native workflow did not finish.", 180)
        delivered = sorted(json.loads(line)["name"] for path in (root / "people-output").glob("*.jsonl")
                           for line in path.read_text().splitlines() if line)
        catalog_before = sorted(item["definitionId"] for item in api.request("/etl/v2/definitions?limit=100")["items"])
        host.stop()

        # A new host on the same database: the draft, the publication and the catalog are read back.
        host = Host(assembly, root, log_path, port)
        reopened = host.api.request(f"/etl/v2/definitions/{definition}")
        versions = host.api.request(f"/etl/v2/definitions/{definition}/versions")
        catalog_after = sorted(item["definitionId"] for item in host.api.request("/etl/v2/definitions?limit=100")["items"])
        tables = database.tables()
        return {
            "definition": definition, "twin": twin, "selection": selection,
            "nativeVersionId": native["version"]["id"],
            "nativePublicationArtifactId": publication["artifactId"],
            "nativeSourceReferenceId": publication["sourceReferenceId"],
            "workflowExecutionId": started["workflowExecutionId"],
            "workflowStatus": view["instance"]["status"], "incidents": len(view.get("incidents") or []),
            "executedNodes": sorted(activity["executableNodeId"] for activity in view["activities"]),
            "delivered": delivered, "catalogBefore": catalog_before, "catalogAfter": catalog_after,
            "reopenedRevision": reopened.get("revision"), "reopenedLastPublishedVersion": reopened.get("lastPublishedVersion"),
            "versionsAfter": [item.get("definitionVersion") for item in (versions.get("items") or [])],
            "designTables": tables,
            "secretInOutput": database.secret in json.dumps([reopened, versions]),
        }
    finally:
        try:
            if host is not None:
                host.stop()
        finally:
            if database is not None:
                database.stop()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workbench-assembly", type=Path, default=DEFAULT_ASSEMBLY)
    parser.add_argument("--output", type=Path, required=True, help="A new directory for the derived summary and logs.")
    parser.add_argument("--port", type=int, default=0, help="A free loopback port; 0 lets the host choose. Never pass one you do not own.")
    parser.add_argument("--provider", action="append", choices=["PostgreSql", "SqlServer", "MySql"],
                        help="Engines to run; all three by default.")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    assembly = args.workbench_assembly.resolve(strict=True)
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=REPO, text=True).strip())
    summary = {"schemaVersion": 1, "spec": "1202-etl-design-store-providers", "sourceRevision": source, "workingTreeDirty": dirty,
               "workbenchAssemblySha256": sha256(assembly.read_bytes()), "engines": {}, "checks": {}}
    for provider in args.provider or ["PostgreSql", "SqlServer", "MySql"]:
        case = summary["engines"][provider] = run_engine(provider, assembly, output, args.port)
        both = sorted([case["definition"], case["twin"]])
        summary["checks"].update({
            f"{provider}:publishedRunCompletedThroughDone": case["workflowStatus"] in ("Completed", "Finished")
                and case["incidents"] == 0 and case["executedNodes"].count("after-done") == 1,
            f"{provider}:recordsDelivered": case["delivered"] == ["Ada", "Bram", "Cleo"],
            f"{provider}:catalogListsBothCaseTwins": case["catalogBefore"] == both and case["catalogAfter"] == both,
            f"{provider}:reopenedHostReadsTheDraftAndPublication": case["reopenedLastPublishedVersion"] == 1 and case["versionsAfter"] == [1],
            f"{provider}:designTablesOnTheEngine": case["designTables"] == ["elsa_etl_definition_drafts", "elsa_etl_published_artifacts"],
            f"{provider}:noSecretInApiOutput": not case["secretInOutput"],
        })
    summary["checks"] = {name: bool(value) for name, value in summary["checks"].items()}
    summary["passed"] = all(summary["checks"].values())
    (output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(json.dumps({"passed": summary["passed"], "checks": summary["checks"]}, indent=2))
    return 0 if summary["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
