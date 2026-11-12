#!/usr/bin/env python3
"""Spec 1192 actual-host journey: continuous HTTP runs on an owned Workbench.

Serves a two-shard cursor feed from this process, publishes a continuous HTTP → JSON Lines pipeline through the
design API and runs it from a native Flowchart's RunEtl activity. It checks delivery of records appended while the
run is live, that caught-up polls retain no pages, completion through the runtime API (the Flowchart resumes through
Done) and cancellation. Derived from spec 1191's driver, which stays unchanged as that spec's evidence. The controller
starts its own Workbench on an ephemeral loopback port and stops only that process.
"""
from __future__ import annotations

import argparse
import hashlib
import http.cookiejar
import http.server
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

REPO = Path(__file__).resolve().parents[3]
DEFAULT_ASSEMBLY = REPO / "Essentials/apps/Elsa.Workbench/bin/Debug/net10.0/Elsa.Workbench.dll"
CASES = ("complete", "cancel", "browser")


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


class Feed:
    """A cursor feed: GET /<case>/<shard>?cursor=n returns the shard's records after position n."""

    def __init__(self):
        self.lock = threading.Lock()
        self.shards: dict[str, list[dict]] = {}
        self.caught_up: dict[str, int] = {}
        self.requests: dict[str, int] = {}

    def append(self, path: str, record: dict):
        with self.lock:
            self.shards.setdefault(path, []).append(record)

    def respond(self, target: str) -> bytes:
        parsed = urllib.parse.urlsplit(target)
        cursor = int(urllib.parse.parse_qs(parsed.query).get("cursor", ["0"])[0])
        with self.lock:
            self.requests[parsed.path] = self.requests.get(parsed.path, 0) + 1
            page = self.shards.get(parsed.path, [])[cursor:]
            if not page:
                self.caught_up[parsed.path] = self.caught_up.get(parsed.path, 0) + 1
                return json.dumps({"records": [], "next": str(cursor) if cursor else None}).encode()
            return json.dumps({"records": page, "next": str(cursor + len(page))}).encode()


def serve(feed: Feed) -> http.server.ThreadingHTTPServer:
    class Handler(http.server.BaseHTTPRequestHandler):
        def do_GET(self):
            body = feed.respond(self.path)
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def log_message(self, *_):
            pass

    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server


class Api:
    def __init__(self, base: str):
        parsed = urllib.parse.urlsplit(base)
        if parsed.scheme != "http" or parsed.hostname != "127.0.0.1":
            raise ValueError("The journey only talks to its own loopback Workbench.")
        self.base = base.rstrip("/")
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def request(self, path: str, value=None, method: str | None = None):
        data = None if value is None else json.dumps(value).encode()
        request = urllib.request.Request(self.base + path, data=data, method=method, headers={"Content-Type": "application/json"})
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
        "source": {"kind": "httpJson", "binding": f"{case}-feed", "recordsPointer": "/records", "fields": [
            {"name": "id", "pointer": "/id", "type": "int64", "required": True},
            {"name": "name", "pointer": "/name", "type": "string", "required": True}],
            "pagination": {"kind": "token", "continuationPointer": "/next", "queryParameter": "cursor"}},
        "lookup": None,
        "routes": {"main": {"kind": "jsonl", "binding": f"{case}-output"}, "errors": {"kind": "jsonl", "binding": f"{case}-errors"}},
        "mapping": [{"name": name, "origin": "source", "field": name} for name in ("id", "name")],
        "filters": [],
        "limits": {"filePartitionCount": 2, "maxPartitions": 8, "maxRecordBytes": 65536, "maxFields": 64, "maxBatchRecords": 100,
                   "maxBatchBytes": 4194304, "maxOutstandingRecords": 100, "maxOutstandingBytes": 4194304, "maxLookupRecords": 100,
                   "maxLookupBytes": 4194304, "maxOutputBatchBytes": 4194304, "maxRetainedOutputBytes": 16777216,
                   "maxSnapshotBytes": 16777216, "maxArtifactBytes": 262144},
        "http": {"maxPageBytes": 65536, "maxPageRecords": 100, "maxJsonDepth": 16, "maxContinuationBytes": 8192,
                 "maxPagesPerPartition": 100, "maxRetainedInputBytes": 4194304, "maxRetainedMetadataBytes": 4194304,
                 "maxAttempts": 3, "deadlineSeconds": 300, "requestTimeoutSeconds": 10, "initialDelaySeconds": 1, "maxDelaySeconds": 60},
        "continuous": {"pollIntervalSeconds": 1}}


def literal(key: str, value) -> dict:
    return {"referenceKey": key, "value": {"value": value, "expressionType": "Literal"}}


def read_names(directory: Path) -> list:
    return sorted(json.loads(line)["name"] for path in directory.glob("*.jsonl") for line in path.read_text().splitlines() if line)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workbench-assembly", type=Path, default=DEFAULT_ASSEMBLY)
    parser.add_argument("--output", type=Path, required=True, help="A new directory for the derived summary and logs.")
    parser.add_argument("--keep-host", action="store_true",
                        help="Leave the owned Workbench and feed running with a live continuous run for a browser observation.")
    parser.add_argument("--port", type=int, default=0, help="A free loopback port; 0 lets the host choose. Never pass one you do not own.")
    parser.add_argument("--studio-origin", help="An owned Studio origin allowed to call and sign in to this Workbench.")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    assembly = args.workbench_assembly.resolve(strict=True)
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=REPO, text=True).strip())

    feed = Feed()
    server = serve(feed)
    feed_base = f"http://127.0.0.1:{server.server_address[1]}"
    root = Path(tempfile.mkdtemp(prefix="elsa-etl-continuous-journey-"))
    for name in ("appsettings.json", "appsettings.Development.json", "shells.json"):
        shutil.copyfile(REPO / "Essentials/apps/Elsa.Workbench" / name, root / name)
    (root / "etl-snapshots").mkdir()
    files, https = [], []
    for case in CASES:
        for role, alias in (("MainOutput", f"{case}-output"), ("ErrorOutput", f"{case}-errors")):
            (root / alias).mkdir()
            files.append({"TenantId": "default", "PersistencePartition": "default", "Alias": alias, "Role": role,
                          "Revision": "v1", "Path": str(root / alias), "IsCurrent": True, "ImmutableInput": False, "AtomicPublication": True})
        https.append({"TenantId": "default", "PersistencePartition": "default", "Alias": f"{case}-feed", "Role": "Source",
                      "Revision": "v1", "Endpoint": f"{feed_base}/{case}/a", "AllowHttp": True, "IsCurrent": True,
                      "AllowedPaths": [f"/{case}/a", f"/{case}/b"],
                      "Shards": [{"Id": "a", "SeedUri": f"{feed_base}/{case}/a"}, {"Id": "b", "SeedUri": f"{feed_base}/{case}/b"}]})
    shells = json.loads((root / "shells.json").read_text())
    shells["CShells"]["Shells"]["default"]["Features"].update({
        "EtlDesign": {}, "EtlDesignEntityFrameworkCore": {"Provider": "Sqlite", "ConnectionString": "Data Source=etl-design.db;Pooling=False"},
        "EtlDesignApi": {"CursorSigningKey": "owned-etl-continuous-design-cursor-signing-key"},
        "EtlRuntime": {"SnapshotRoot": str(root / "etl-snapshots"), "FileBindings": files, "HttpBindings": https},
        "EtlRuntimeEntityFrameworkCore": {"Provider": "Sqlite", "ConnectionString": "Data Source=etl-runtime.db;Pooling=False"},
        "EtlRuntimeApi": {"CursorSigningKey": "owned-etl-continuous-runtime-cursor-signing-key"},
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
    summary = {"schemaVersion": 1, "spec": "1192-etl-continuous-http", "sourceRevision": source, "workingTreeDirty": dirty,
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
        run_etl = api.activity("Elsa.Etl.Workflows.Runtime.Activities.RunEtl")
        write_line = api.activity("Elsa.Activities.Primitives.Activities.WriteLine")
        flowchart = api.activity("Elsa.Activities.Flowchart.Activities.Flowchart")

        def start(case: str):
            definition = f"continuous-{case}-{uuid.uuid4().hex[:8]}"
            draft = api.request("/etl/v2/definitions", {"id": definition, "pipeline": pipeline(case)})
            validation = api.request(f"/etl/v2/definitions/{definition}/validate", {"expectedRevision": draft["revision"]})
            if validation["diagnostics"]:
                raise ValueError(f"{case}: the continuous definition did not validate: {validation['diagnostics']}")
            published = api.request(f"/etl/v2/definitions/{definition}/publish", {
                "expectedRevision": validation["revision"], "definitionVersion": validation["definitionVersion"],
                "contentHash": validation["contentHash"]})
            selection = published["version"]["selection"]

            def node(identity, version, inputs=None, structure=None):
                return {"nodeId": identity, "activityVersionId": version, "inputs": inputs or [], "outputs": [], "structure": structure}
            etl = node("etl", run_etl, [literal("Pipeline", selection)])
            done = node("after-done", write_line, [literal("text", f"Continuous run {case} completed")])
            business = node("after-business-error", write_line, [literal("text", f"Continuous run {case} rejected records")])
            root_node = node("root", flowchart, structure={"kind": "elsa.flowchart.structure", "schemaVersion": "1.0.0", "payload": {
                "startNodeId": "etl", "activities": [etl, done, business], "connections": [
                    {"source": {"nodeId": "etl", "port": "Done"}, "target": {"nodeId": "after-done", "port": "Done"}},
                    {"source": {"nodeId": "etl", "port": "BusinessError"}, "target": {"nodeId": "after-business-error", "port": "Done"}}]}})
            native = api.request("/design/workflows/definitions/submit", {"name": f"Spec 1192 continuous {case} {definition}",
                                 "state": {"variables": [], "inputs": [], "outputs": [], "rootActivity": root_node}})
            publication = api.request("/publishing/workflows/" + native["version"]["id"] + "/publish", {})
            started = api.request("/runtime/workflows/executables/" + publication["artifactId"] + "/execute",
                                  {"sourceReferenceId": publication["sourceReferenceId"]})
            run = wait_for(lambda: [item for item in api.request("/etl/runs?limit=100")["items"] if item.get("selection") == selection],
                           lambda runs: len(runs) == 1, f"{case}: the ETL run was not admitted.", 60)[0]
            return definition, selection, started["workflowExecutionId"], urllib.parse.quote(run["runId"], safe="")

        def partitions(run: str) -> list:
            return api.request("/etl/runs/" + run + "/connector-partitions?limit=50")["items"]

        def idle(run: str, accepted: int):
            def probe():
                detail, rows = api.request("/etl/runs/" + run), partitions(run)
                return detail["acceptedRecords"] == accepted and len(rows) == 2 and all(row["nextPollAt"] for row in rows), rows
            return wait_for(probe, lambda value: value[0], f"{run}: the shards did not idle with {accepted} accepted.", 90)[1]

        def workflow(execution: str):
            return wait_for(lambda: api.request("/runtime/workflows/instances/" + urllib.parse.quote(execution, safe="")),
                            lambda value: value["instance"]["status"] in ("Completed", "Finished", "Faulted", "Cancelled"),
                            f"{execution}: the native workflow did not finish.", 180)

        # Completion: deliver while live, idle without growth, then drain.
        feed.append("/complete/a", {"id": 1, "name": "Ada"})
        definition, selection, execution, run = start("complete")
        first = idle(run, 1)
        polled = sum(feed.caught_up.get(path, 0) for path in ("/complete/a", "/complete/b"))
        wait_for(lambda: sum(feed.caught_up.get(path, 0) for path in ("/complete/a", "/complete/b")),
                 lambda count: count >= polled + 20, "complete: the shards did not keep polling.", 60)
        after_polls = idle(run, 1)
        feed.append("/complete/b", {"id": 2, "name": "Bram"})
        feed.append("/complete/a", {"id": 3, "name": "Cleo"})
        idle(run, 3)
        running = api.request("/etl/runs/" + run)
        completion = api.request("/etl/runs/" + run + "/complete", {}, "POST")
        repeat = api.request("/etl/runs/" + run + "/complete", {}, "POST")
        view = workflow(execution)
        detail = api.request("/etl/runs/" + run)
        summary["cases"]["complete"] = {
            "definition": definition, "selection": selection, "workflowStatus": view["instance"]["status"],
            "executedNodes": sorted(activity["executableNodeId"] for activity in view["activities"]),
            "statusWhileLive": running["status"], "connectorWhileLive": running["connector"],
            "pageSequencesAfterFirstIdle": [row["http"]["pageSequence"] for row in first],
            "pageSequencesAfterTwentyPolls": [row["http"]["pageSequence"] for row in after_polls],
            "caughtUpResponses": sum(feed.caught_up.get(path, 0) for path in ("/complete/a", "/complete/b")),
            "completion": completion, "repeat": repeat,
            "run": {key: detail.get(key) for key in ("status", "acceptedRecords", "rejectedRecords", "outstandingRecords", "outstandingBytes")},
            "connector": detail.get("connector"), "mainNames": read_names(root / "complete-output"),
            "hostPathInInspection": str(root) in json.dumps(detail) or feed_base in json.dumps(detail)}

        # Cancellation of a live continuous run.
        definition, selection, execution, run = start("cancel")
        idle(run, 0)
        cancelled = api.request("/etl/runs/" + run + "/cancel", {}, "POST")
        view = workflow(execution)
        after = api.request("/etl/runs/" + run + "/complete", {}, "POST")
        summary["cases"]["cancel"] = {"definition": definition, "workflowStatus": view["instance"]["status"],
                                      "cancellation": cancelled["terminalResult"]["outcome"], "completionAfterCancel": after,
                                      "runStatus": api.request("/etl/runs/" + run)["status"]}

        complete, cancel = summary["cases"]["complete"], summary["cases"]["cancel"]
        checks = {
            "liveRunIsContinuousAndRunning": complete["statusWhileLive"] in ("running", "Running")
                and (complete["connectorWhileLive"] or {}).get("mode") == "continuous",
            "twentyCaughtUpPollsRetainedNoPage": complete["pageSequencesAfterFirstIdle"] == complete["pageSequencesAfterTwentyPolls"],
            "appendedRecordsDeliveredOnce": complete["mainNames"] == ["Ada", "Bram", "Cleo"],
            "completionAcceptedThenIdempotent": complete["completion"] == {"result": "accepted"}
                and complete["repeat"]["result"] in ("alreadyRequested", "terminal"),
            "completedRunSucceededThroughDone": complete["run"]["status"] in ("succeeded", "Succeeded")
                and complete["workflowStatus"] in ("Completed", "Finished") and "after-done" in complete["executedNodes"]
                and "after-business-error" not in complete["executedNodes"],
            "noAccountingLeft": complete["run"]["outstandingRecords"] == 0 and complete["run"]["outstandingBytes"] == 0,
            "cancelledRunSettledCancelled": cancel["cancellation"] == "cancelled" and cancel["runStatus"] in ("cancelled", "Cancelled")
                and cancel["completionAfterCancel"] == {"result": "terminal"},
            "noHostPathOrFeedUrlInInspection": not complete["hostPathInInspection"],
        }
        summary["checks"] = checks
        summary["passed"] = all(checks.values())
        if args.keep_host and summary["passed"]:
            feed.append("/browser/a", {"id": 1, "name": "Ada"})
            definition, selection, execution, run = start("browser")
            idle(run, 1)
            summary["browserRun"] = {"definition": definition, "runId": urllib.parse.unquote(run), "workflowExecutionId": execution}
        (output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
        print(json.dumps({"passed": summary["passed"], "checks": checks}, indent=2))
        if args.keep_host and summary["passed"]:
            stop = output / "stop"
            print(f"Owned Workbench left running at {url} (pid {host.pid}) with live run {summary['browserRun']['runId']}; "
                  f"create {stop} to stop it and the feed.", flush=True)
            while not stop.exists():
                time.sleep(1)
        return 0 if summary["passed"] else 1
    finally:
        if host.poll() is None:
            host.terminate()
            try:
                host.wait(timeout=30)
            except subprocess.TimeoutExpired:
                host.kill()
                host.wait(timeout=10)
        server.shutdown()
        log.close()


if __name__ == "__main__":
    raise SystemExit(main())
