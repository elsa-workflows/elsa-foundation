#!/usr/bin/env python3
"""Spec 1201 actual-host journey: RabbitMQ ingestion with manual acknowledgements on an owned Workbench.

Starts its own RabbitMQ 4.1 container (with the management plugin, used only to declare the queue, publish the messages and
inspect the queue) and its own Workbench on ephemeral loopback ports. Five messages are published, one of them not JSON. A
RabbitMQ → JSON Lines definition with a maximum of three messages runs twice from native Flowcharts: the first run takes
three messages and routes the invalid one to errors, the second takes the remaining two. It checks the native outcome
ports, the delivered records, the queue being empty with nothing unacknowledged, and the runtime inspection. Derived from
spec 1200's driver, which stays unchanged as that spec's evidence. The controller stops only what it started.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import http.cookiejar
import json
import os
import secrets
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

MESSAGES = ['{"id":1,"name":"Ada"}', '{"id":2,"name":"Bram"}', 'not json', '{"id":3,"name":"Cleo"}', '{"id":4,"name":"Dee"}']
QUEUE = "etl-people-in"


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
        "source": {"kind": "rabbitmq", "binding": "events-in", "queue": QUEUE, "maxMessages": 3, "fields": [
            {"name": "id", "pointer": "/id", "type": "int64", "required": True},
            {"name": "name", "pointer": "/name", "type": "string", "required": True}]},
        "lookup": None,
        "routes": {"main": {"kind": "jsonl", "binding": f"{case}-output"}, "errors": {"kind": "jsonl", "binding": f"{case}-errors"}},
        "mapping": [{"name": name, "origin": "source", "field": name} for name in ("id", "name")],
        "filters": [],
        "limits": {"filePartitionCount": 2, "maxPartitions": 8, "maxRecordBytes": 65536, "maxFields": 64, "maxBatchRecords": 1,
                   "maxBatchBytes": 4194304, "maxOutstandingRecords": 100, "maxOutstandingBytes": 4194304, "maxLookupRecords": 100,
                   "maxLookupBytes": 4194304, "maxOutputBatchBytes": 4194304, "maxRetainedOutputBytes": 16777216,
                   "maxSnapshotBytes": 16777216, "maxArtifactBytes": 262144},
        "http": {"maxPageBytes": 65536, "maxPageRecords": 100, "maxJsonDepth": 16, "maxContinuationBytes": 8192,
                 "maxPagesPerPartition": 4, "maxRetainedInputBytes": 4194304, "maxRetainedMetadataBytes": 4194304,
                 "maxAttempts": 3, "deadlineSeconds": 300, "requestTimeoutSeconds": 10, "initialDelaySeconds": 1, "maxDelaySeconds": 60}}


def throwaway_secret() -> str:
    """A container's administrator secret generated per run, so none is committed."""
    return secrets.token_hex(16)


class Broker:
    """An owned RabbitMQ container: AMQP for the Workbench, the management API for this controller only."""

    def __init__(self):
        self.name = "elsa-etl-rabbitmq-journey-" + uuid.uuid4().hex[:8]
        self.user = "etl"
        self.secret = throwaway_secret()
        subprocess.run(["docker", "run", "-d", "--rm", "--name", self.name, "-e", f"RABBITMQ_DEFAULT_USER={self.user}",
                        "-e", f"RABBITMQ_DEFAULT_PASS={self.secret}", "-p", "127.0.0.1::5672", "-p", "127.0.0.1::15672",
                        "rabbitmq:4.1-management-alpine"], capture_output=True, text=True, check=True)
        self.amqp_port = self._port("5672/tcp")
        self.http_port = self._port("15672/tcp")
        wait_for(self._alive, bool, "RabbitMQ did not become ready.", 180)

    def _port(self, port: str) -> int:
        return int(subprocess.check_output(["docker", "port", self.name, port], text=True).strip().splitlines()[0].rsplit(":", 1)[1])

    @property
    def uri(self) -> str:
        return f"amqp://{self.user}:{self.secret}@127.0.0.1:{self.amqp_port}/"

    def request(self, method: str, path: str, value=None):
        token = base64.b64encode(f"{self.user}:{self.secret}".encode()).decode()
        request = urllib.request.Request(f"http://127.0.0.1:{self.http_port}/api/{path}", method=method,
                                         data=None if value is None else json.dumps(value).encode(),
                                         headers={"Content-Type": "application/json", "Authorization": "Basic " + token})
        with urllib.request.urlopen(request, timeout=30) as response:
            raw = response.read()
            return json.loads(raw) if raw else None

    def _alive(self) -> bool:
        try:
            # Ready once the AMQP listener is up as well as the management API.
            return any(listener["protocol"] == "amqp" for listener in self.request("GET", "overview").get("listeners", []))
        except (urllib.error.URLError, ConnectionError, OSError, ValueError):
            return False

    def declare(self) -> None:
        self.request("PUT", f"queues/%2F/{QUEUE}", {"durable": True})
        for message in MESSAGES:
            routed = self.request("POST", "exchanges/%2F/amq.default/publish",
                                  {"properties": {"delivery_mode": 2}, "routing_key": QUEUE, "payload": message, "payload_encoding": "string"})
            if not routed.get("routed"):
                raise RuntimeError("A journey message was not routed to its queue.")

    def queue(self) -> dict:
        # Live counts from the broker; the management API's statistics refresh only every few seconds.
        out = subprocess.check_output(["docker", "exec", self.name, "rabbitmqctl", "-q", "list_queues", "name", "messages_ready",
                                       "messages_unacknowledged"], text=True)
        for line in out.splitlines():
            parts = line.split()
            if len(parts) == 3 and parts[0] == QUEUE:
                return {"ready": int(parts[1]), "unacknowledged": int(parts[2])}
        raise RuntimeError("The journey queue was not listed.")

    def stop(self) -> None:
        subprocess.run(["docker", "rm", "-f", self.name], capture_output=True)


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

    broker = Broker()
    broker.declare()

    root = Path(tempfile.mkdtemp(prefix="elsa-etl-rabbitmq-journey-"))
    for name in ("appsettings.json", "appsettings.Development.json", "shells.json"):
        shutil.copyfile(REPO / "Essentials/apps/Elsa.Workbench" / name, root / name)
    (root / "etl-snapshots").mkdir()
    cases = {"first": None, "second": None}
    bindings = []
    for case in cases:
        for route, role in (("output", "MainOutput"), ("errors", "ErrorOutput")):
            (root / f"{case}-{route}").mkdir()
            bindings.append({"TenantId": "default", "PersistencePartition": "default", "Alias": f"{case}-{route}", "Role": role,
                             "Revision": "v1", "Path": str(root / f"{case}-{route}"), "IsCurrent": True, "ImmutableInput": False,
                             "AtomicPublication": True})
    rabbitmq_bindings = [{"TenantId": "default", "PersistencePartition": "default", "Alias": "events-in", "Role": "Source",
                          "Revision": "v1", "ConnectionString": broker.uri, "AllowedQueues": [QUEUE]}]
    shells = json.loads((root / "shells.json").read_text())
    shells["CShells"]["Shells"]["default"]["Features"].update({
        "EtlDesign": {}, "EtlDesignEntityFrameworkCore": {"Provider": "Sqlite", "ConnectionString": "Data Source=etl-design.db;Pooling=False"},
        "EtlDesignApi": {"CursorSigningKey": "owned-etl-rabbitmq-design-cursor-signing-key"},
        "EtlRuntime": {"SnapshotRoot": str(root / "etl-snapshots"), "FileBindings": bindings, "RabbitMqBindings": rabbitmq_bindings},
        "EtlRabbitMq": {},
        "EtlRuntimeEntityFrameworkCore": {"Provider": "Sqlite", "ConnectionString": "Data Source=etl-runtime.db;Pooling=False"},
        "EtlRuntimeApi": {"CursorSigningKey": "owned-etl-rabbitmq-runtime-cursor-signing-key"},
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
    summary = {"schemaVersion": 1, "spec": "1201-etl-rabbitmq-source", "sourceRevision": source, "workingTreeDirty": dirty,
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
        summary["catalogRabbitMqBindings"] = sorted((item["alias"], item["role"], item["kind"]) for item in catalog if item["kind"] == "rabbitMq")
        run_etl = api.activity("Elsa.Etl.Workflows.Runtime.Activities.RunEtl")
        write_line = api.activity("Elsa.Activities.Primitives.Activities.WriteLine")
        flowchart = api.activity("Elsa.Activities.Flowchart.Activities.Flowchart")

        for case in cases:
            definition = f"rabbitmq-{case}-{uuid.uuid4().hex[:8]}"
            draft = api.request("/etl/v2/definitions", {"id": definition, "pipeline": pipeline(case)})
            validation = api.request(f"/etl/v2/definitions/{definition}/validate", {"expectedRevision": draft["revision"]})
            if validation["diagnostics"]:
                raise ValueError(f"{case}: the RabbitMQ definition did not validate: {validation['diagnostics']}")
            published = api.request(f"/etl/v2/definitions/{definition}/publish", {
                "expectedRevision": validation["revision"], "definitionVersion": validation["definitionVersion"],
                "contentHash": validation["contentHash"]})
            selection = published["version"]["selection"]

            def node(identity, version, inputs=None, structure=None):
                return {"nodeId": identity, "activityVersionId": version, "inputs": inputs or [], "outputs": [], "structure": structure}
            etl = node("etl", run_etl, [literal("Pipeline", selection)])
            done = node("after-done", write_line, [literal("text", f"RabbitMQ ingestion {case} succeeded")])
            business = node("after-business-error", write_line, [literal("text", f"RabbitMQ ingestion {case} rejected records")])
            root_node = node("root", flowchart, structure={"kind": "elsa.flowchart.structure", "schemaVersion": "1.0.0", "payload": {
                "startNodeId": "etl", "activities": [etl, done, business], "connections": [
                    {"source": {"nodeId": "etl", "port": "Done"}, "target": {"nodeId": "after-done", "port": "Done"}},
                    {"source": {"nodeId": "etl", "port": "BusinessError"}, "target": {"nodeId": "after-business-error", "port": "Done"}}]}})
            native = api.request("/design/workflows/definitions/submit", {"name": f"Spec 1201 RabbitMQ ingestion {case} {definition}",
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
                "runId": run_id,
                "connector": detail.get("connector"),
                "diagnosticCode": ((detail.get("terminalResult") or {}).get("diagnostic") or {}).get("code"),
                "delivered": sorted(line for path in (root / f"{case}-output").glob("*.jsonl") for line in path.read_text().splitlines() if line),
                "rejected": sum(1 for path in (root / f"{case}-errors").glob("*.jsonl") for line in path.read_text().splitlines() if line),
                "queueAfter": broker.queue(),
                "hostPathOrSecretInInspection": str(root) in json.dumps(detail) or broker.secret in json.dumps(detail)}

        first, second = summary["cases"]["first"], summary["cases"]["second"]
        names = lambda case: sorted(json.loads(line)["name"] for line in case["delivered"])
        through = lambda case, port: case["workflowStatus"] in ("Completed", "Finished") and case["incidents"] == 0 and port in case["executedNodes"]
        checks = {
            "catalogListsTheRabbitMqSourceBinding": summary["catalogRabbitMqBindings"] == [("events-in", "Source", "rabbitMq")],
            "firstRunTookThreeMessagesAndRejectedTheInvalidOne": through(first, "after-business-error")
                and first["run"]["sourceRecordCount"] == 3 and first["run"]["acceptedRecords"] == 2 and first["rejected"] == 1
                and names(first) == ["Ada", "Bram"],
            "firstRunLeftTheRestQueuedAndAcknowledgedItsOwn": first["queueAfter"] == {"ready": 2, "unacknowledged": 0},
            "secondRunTookTheRemainingTwoThroughDone": through(second, "after-done") and second["run"]["acceptedRecords"] == 2
                and names(second) == ["Cleo", "Dee"],
            "queueIsEmptyWithNothingUnacknowledged": second["queueAfter"] == {"ready": 0, "unacknowledged": 0},
            "inspectionShowsTheRabbitMqSource": (first["connector"] or {}).get("sourceKind") == "rabbitMq",
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
        broker.stop()


if __name__ == "__main__":
    raise SystemExit(main())
