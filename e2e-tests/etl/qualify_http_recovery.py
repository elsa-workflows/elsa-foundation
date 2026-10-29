#!/usr/bin/env python3
"""Actual published RunEtl: durable HTTP 429 wait, Workbench process loss, recovery.

Reuses the delivered HTTP fixture and public ETL/native APIs. Only this controller's
Workbench and Node children are stopped; original/user stores are never opened.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timezone
import gzip
import importlib.util
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

REPO = Path(__file__).resolve().parents[2]
_spec = importlib.util.spec_from_file_location("bpmn_qualification", REPO / "tools/bpmn/qualify-hosting.py")
q = importlib.util.module_from_spec(_spec); _spec.loader.exec_module(q)


def wait_for(action, predicate, message, seconds=90):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        value = action()
        if predicate(value): return value
        time.sleep(.1)
    raise TimeoutError(message)


def pipeline():
    return {"source": {"kind": "httpJson", "binding": "http-source", "recordsPointer": "/items",
        "fields": [{"name": "id", "pointer": "/id", "type": "int64", "required": True}, {"name": "name", "pointer": "/name", "type": "string", "required": True}],
        "pagination": {"kind": "token", "continuationPointer": "/next", "queryParameter": "pageToken"}},
        "lookup": None, "routes": {"main": {"kind": "http", "binding": "http-output"}, "errors": {"kind": "jsonl", "binding": "errors"}},
        "mapping": [{"name": name, "origin": "source", "field": name} for name in ("id", "name")], "filters": [],
        "limits": {"filePartitionCount": 2, "maxPartitions": 8, "maxRecordBytes": 65536, "maxFields": 64, "maxBatchRecords": 100,
            "maxBatchBytes": 4194304, "maxOutstandingRecords": 100, "maxOutstandingBytes": 4194304, "maxLookupRecords": 100,
            "maxLookupBytes": 4194304, "maxOutputBatchBytes": 4194304, "maxRetainedOutputBytes": 16777216, "maxSnapshotBytes": 16777216, "maxArtifactBytes": 262144},
        "http": {"maxPageBytes": 65536, "maxPageRecords": 100, "maxJsonDepth": 16, "maxContinuationBytes": 8192,
            "maxPagesPerPartition": 4, "maxRetainedInputBytes": 4194304, "maxRetainedMetadataBytes": 4194304,
            "maxAttempts": 3, "deadlineSeconds": 300, "requestTimeoutSeconds": 10, "initialDelaySeconds": 1, "maxDelaySeconds": 60}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workbench-assembly", type=Path, required=True)
    parser.add_argument("--reader-assembly", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--parent", choices=("Sequence", "Flowchart"), default="Sequence")
    args = parser.parse_args()
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
    if subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=all"], cwd=REPO, text=True).strip():
        raise ValueError("Commit the journey source before recording exact-source evidence.")
    assembly = args.workbench_assembly.resolve(strict=True); reader = args.reader_assembly.resolve(strict=True)
    products = json.loads(subprocess.check_output(["dotnet", str(reader), "--inspect-etl-products", str(assembly)], text=True))
    if products["sourceCommit"] != source: raise ValueError("Build the actual Workbench/ETL products from this exact committed source.")
    output = args.output.resolve(); output.mkdir(parents=True, exist_ok=False)
    root = Path(tempfile.mkdtemp(prefix="elsa-etl-http-recovery-"))
    for name in ("appsettings.json", "appsettings.Development.json", "shells.json"):
        shutil.copyfile(REPO / "Essentials/apps/Elsa.Workbench" / name, root / name)
    for name in ("packages", "etl-snapshots", "errors"): (root / name).mkdir()
    processes = []; children = []; logs = []; host = None

    def start(command, role):
        log = (root / f"{role}-{len(processes)}.log").open("w"); logs.append(log)
        child = subprocess.Popen(command, cwd=root, env={**os.environ, "ASPNETCORE_ENVIRONMENT": "Development"}, stdout=log, stderr=subprocess.STDOUT)
        children.append(child)
        processes.append({"role": role, "processId": child.pid, "controllerProcessId": os.getpid(), "started": q.process_field(child.pid, "lstart")})
        return child

    def stop(child, crash=False):
        if child is None or child.poll() is not None: return
        if int(q.process_field(child.pid, "ppid")) != os.getpid(): raise ValueError("The process is no longer owned by this controller.")
        if crash: child.kill()
        else: child.terminate()
        try: child.wait(timeout=30)
        except subprocess.TimeoutExpired: child.kill(); child.wait(timeout=10)

    def launch():
        nonlocal host
        host = start(["dotnet", str(assembly), "--contentRoot", str(root), "--urls", "http://127.0.0.1:0"], "workbench")
        log = root / f"workbench-{len(processes)-1}.log"
        def address():
            if host.poll() is not None: raise RuntimeError(f"The owned Workbench exited; private logs: {root}")
            lines=[line.split("Now listening on: ",1)[1].strip() for line in log.read_text().splitlines() if "Now listening on: " in line]
            return lines[0] if lines else None
        url=wait_for(address, lambda value: value is not None, "Owned Workbench did not listen.",180)
        api=q.Api(url)
        def healthy():
            if host.poll() is not None:raise RuntimeError(f"The owned Workbench exited before readiness; private logs: {root}")
            try:
                with urllib.request.urlopen(url+"/health/ready",timeout=3) as response:return response.status==200
            except urllib.error.HTTPError as error:
                if error.code==503:return False
                raise
        wait_for(healthy,bool,"Owned Workbench is not ready.",180)
        api.login("admin","Password123!")
        return api

    def snapshots(label):
        result={}
        for name in ("elsa", "etl-runtime", "etl-design"):
            backup=root/f"{label}-{name}.sqlite"; q.backup_sqlite(root/(name+".db"),backup)
            target=output/(label+"-"+name+".sqlite.gz")
            with backup.open("rb") as raw,gzip.open(target,"wb") as zipped:shutil.copyfileobj(raw,zipped)
            result[name]={"file":target.name,"databaseSha256":q.digest(backup),"gzipSha256":q.digest(target)}
        return result

    try:
        fixture_script=REPO/"e2e-tests/etl/EtlHttpFixture.mjs";ready=root/"http-ready.json"
        fixture=start(["node",str(fixture_script),"--ready-file",str(ready),"--source-retry-after-seconds","20"],"http-fixture")
        wait_for(lambda:ready.exists(),bool,"The owned HTTP fixture did not start.",15)
        fixture_url="http://127.0.0.1:"+str(json.loads(ready.read_text())["port"])
        def wire():
            with urllib.request.urlopen(fixture_url+"/__fixture/state",timeout=5) as response:value=json.load(response)
            if value["overflow"]:raise ValueError("The actual HTTP fixture observation bound was exceeded.")
            return value
        shells=json.loads((root/"shells.json").read_text());features=shells["CShells"]["Shells"]["default"]["Features"]
        source_path="/source/shard-a/page";second_path="/source/shard-b/page";output_path="/output/records"
        common={"TenantId":"default","PersistencePartition":"default","AllowHttp":True}
        features.update({"EtlDesign":{},"EtlDesignEntityFrameworkCore":{"Provider":"Sqlite","ConnectionString":"Data Source=etl-design.db;Pooling=False"},
            "EtlDesignApi":{"CursorSigningKey":"owned-etl-recovery-design-cursor-signing-key"},
            "EtlRuntime":{"SnapshotRoot":str(root/"etl-snapshots"),"FileBindings":[{"TenantId":"default","PersistencePartition":"default","Alias":"errors","Role":"ErrorOutput","Revision":"errors-v1","Path":str(root/"errors"),"IsCurrent":True,"ImmutableInput":False,"AtomicPublication":True}],
                "HttpBindings":[{**common,"Alias":"http-source","Role":"Source","Revision":"http-source-v1","Endpoint":fixture_url+source_path,"AllowedPaths":[source_path,second_path],"Shards":[{"Id":"shard-a","SeedUri":fixture_url+source_path},{"Id":"shard-b","SeedUri":fixture_url+second_path}]},
                    {**common,"Alias":"http-output","Role":"MainOutput","Revision":"http-output-v1","Endpoint":fixture_url+output_path,"AllowedPaths":[output_path],"Shards":[]}]},
            "EtlRuntimeEntityFrameworkCore":{"Provider":"Sqlite","ConnectionString":"Data Source=etl-runtime.db;Pooling=False"},
            "EtlRuntimeApi":{"CursorSigningKey":"owned-etl-recovery-runtime-cursor-signing-key"},"EtlWorkflowsDesign":{},"EtlWorkflowsRuntime":{}})
        q.write_json(root/"shells.json",shells)
        api=launch();definition="recovery-"+uuid.uuid4().hex[:12]
        bindings=api.request("/etl/v2/bindings?limit=100")
        draft=api.request("/etl/v2/definitions",{"id":definition,"pipeline":pipeline()})
        validation=api.request(f"/etl/v2/definitions/{definition}/validate",{"expectedRevision":draft["revision"]})
        if validation["diagnostics"]:raise ValueError("The actual ETL definition did not validate.")
        published=api.request(f"/etl/v2/definitions/{definition}/publish",{key:validation[key] for key in ("definitionVersion","contentHash")} | {"expectedRevision":validation["revision"]})
        selection=published["version"]["selection"]
        def node(identity,activity,inputs=None,structure=None):
            return {"nodeId":identity,"activityVersionId":api.activity(activity),"inputs":inputs or [],"outputs":[],"structure":structure}
        etl=node("etl","Elsa.Etl.Workflows.Runtime.Activities.RunEtl",[q.literal("Pipeline",selection)])
        after=node("after","Elsa.Activities.Primitives.Activities.WriteLine",[q.literal("text","Recovered actual ETL "+args.parent)])
        if args.parent=="Sequence":
            structure={"kind":"elsa.sequence.structure","schemaVersion":"1.0.0","payload":{"activities":[etl,after]}}
            key="Elsa.Activities.Sequence.Activities.Sequence"
        else:
            structure={"kind":"elsa.flowchart.structure","schemaVersion":"1.0.0","payload":{"startNodeId":"etl","activities":[etl,after],"connections":[{"source":{"nodeId":"etl","port":port},"target":{"nodeId":"after","port":"Done"}} for port in ("Done","BusinessError")]}}
            key="Elsa.Activities.Flowchart.Activities.Flowchart"
        native=api.request("/design/workflows/definitions/submit",{"name":"Actual ETL recovery "+args.parent+" "+definition,"state":{"variables":[],"inputs":[],"outputs":[],"rootActivity":node("root",key,structure=structure)}})
        publication=api.request("/publishing/workflows/"+native["version"]["id"]+"/publish",{})
        executed=api.request("/runtime/workflows/executables/"+publication["artifactId"]+"/execute",{"sourceReferenceId":publication["sourceReferenceId"]})
        execution=executed["workflowExecutionId"]
        summaries=wait_for(lambda:api.request("/etl/runs?limit=100"),lambda value:len(value["items"])==1,"The real RunEtl activity did not admit its run.")
        run=summaries["items"][0]["runId"];escaped=urllib.parse.quote(run,safe="")
        def view():return {"native":api.view(execution),"run":api.request("/etl/runs/"+escaped),"partitions":api.request("/etl/runs/"+escaped+"/connector-partitions?limit=50")}
        def cut_ready(value):
            rows=value["partitions"]["items"]
            return len(rows)==2 and any(item["http"]["fetch"] and item["http"]["fetch"]["lastStatusCode"]==429 and item["http"]["fetch"]["phase"].casefold()=="retryscheduled" for item in rows) and any(item["http"]["exhausted"] and item["owedBatch"] is None for item in rows)
        before=wait_for(view,cut_ready,"No real 429 wait with a completed sibling was observed.",30)
        retry=next(item["http"]["fetch"] for item in before["partitions"]["items"] if item["http"]["fetch"] and item["http"]["fetch"]["lastStatusCode"]==429)
        if retry["attemptCount"]!=1 or datetime.fromisoformat(retry["notBefore"].replace("Z","+00:00"))<=datetime.now(timezone.utc):
            raise ValueError("The crash cut did not retain its original future retry deadline.")
        if host.poll() is not None or int(q.process_field(host.pid,"ppid"))!=os.getpid():raise ValueError("The cut no longer belongs to this live controller child.")
        os.kill(host.pid,signal.SIGSTOP)
        wait_for(lambda:q.process_field(host.pid,"stat"),lambda value:"T" in value,"The owned Workbench did not stop at its actual cut.",5)
        before_wire=wire();q.write_json(output/"before.json",before);q.write_json(output/"before-wire.json",before_wire)
        if len([item for item in before_wire["requests"] if item.get("shard")=="shard-b"])!=1:
            raise ValueError("The actual source already retried before the recorded crash cut.")
        before_stores=snapshots("before")
        stop(host,crash=True);exit_code=host.returncode
        if exit_code!=-signal.SIGKILL:raise ValueError("The predecessor did not exit through actual process loss.")
        print("Observed durable 429 and completed sibling; owned predecessor terminated.",flush=True)
        api=launch();recovered=view();q.write_json(output/"recovered.json",recovered)
        finished=wait_for(view,lambda value:value["run"]["status"]=="succeeded" and value["native"]["instance"]["status"]=="Completed","The actual ETL/native pair did not finish after process loss.",120)
        result=finished["run"];rows=finished["partitions"]["items"];native_view=finished["native"]
        if (result["formatVersion"]!=2 or result["selection"]!=selection or result["terminalResult"]["outcome"]!="succeeded" or
                result["acceptedRecords"]!=4 or result["rejectedRecords"]!=0 or result["filteredRecords"]!=0 or result["outstandingRecords"]!=0 or result["outstandingBytes"]!=0 or
                len(rows)!=2 or finished["partitions"]["nextCursor"] is not None or any(not item["http"]["exhausted"] or item["http"]["pageSequence"]!=1 or item["owedBatch"] is not None for item in rows)):
            raise ValueError("The recovered ETL run did not settle four accepted records and both exact partitions.")
        if native_view["incidents"] or len(native_view["activities"])!=3 or any(item["status"]!="Completed" for item in native_view["activities"]):
            raise ValueError("The recovered native parent did not execute RunEtl and its successor once.")
        old_activities={(item.get("authoredActivityId") or item["executableNodeId"]):item for item in before["native"]["activities"]}
        if "after" in old_activities or old_activities["etl"]["activityExecutionId"]!=next(item["activityExecutionId"] for item in native_view["activities"] if (item.get("authoredActivityId") or item["executableNodeId"])=="etl"):
            raise ValueError("The native RunEtl invocation was recreated or its successor ran before the cut.")
        for field in ("artifactId","artifactHash","artifactVersion","definitionId","definitionVersionId"):
            if before["native"]["instance"][field]!=native_view["instance"][field]:raise ValueError("The actual native executable full pin changed during ETL recovery.")
        q.write_json(output/"finished.json",finished);finished_wire=wire();q.write_json(output/"finished-wire.json",finished_wire)
        b_requests=[item for item in finished_wire["requests"] if item.get("shard")=="shard-b"]
        if len(b_requests)!=3 or [item["status"] for item in b_requests]!=[429,200,200] or b_requests[1]["receivedAtMs"]-b_requests[0]["receivedAtMs"]<20000:
            raise ValueError("The actual source retried early or failed to resume its original two-page shard.")
        records=[item for post in finished_wire["outputPosts"] for item in post["records"]]
        if {item["id"]:item["name"] for item in records}!={1:"Ada",2:"Grace",3:"Linus",4:"Margaret"} or any(not post["hasIdempotencyKey"] for post in finished_wire["outputPosts"]):
            raise ValueError("The actual HTTP receiver lacks mapped output or idempotency headers.")
        stop(host);finished_stores=snapshots("finished")
        api=launch();retained=view();retained_wire=wire();q.write_json(output/"retained.json",retained);q.write_json(output/"retained-wire.json",retained_wire);stop(host)
        if retained!=finished or retained_wire!=finished_wire:raise ValueError("The terminal pair or physical HTTP observations changed after another process.")
        q.write_json(output/"process.json",{"schemaVersion":1,"kind":"actual-native-etl-http-recovery","sourceRevision":source,"products":products,"parent":args.parent,"processes":processes,"predecessorExitCode":exit_code,
            "definition":definition,"selection":selection,"workflowExecutionId":execution,"runId":run,"publication":publication,"etlPublication":published,"bindings":bindings,
            "fixtureSha256":q.digest(fixture_script),"controllerSha256":q.digest(Path(__file__)),"beforeStores":before_stores,"finishedStores":finished_stores,
            "scopeLimits":["Real transient HTTP retry and native RunEtl recovery only; no BPMN/model worker bridge or held terminal-fault retry is proved.","The crash cut uses SIGSTOP only to copy committed stores, then SIGKILL; no persisted state or clock is edited.","External HTTP effects remain at least once."]})
        print("Actual ETL and native successor completed and survived another Workbench process.",flush=True)
    finally:
        for child in reversed(children):
            if child.poll() is None:
                if child is host and "T" in q.process_field(child.pid,"stat"):os.kill(child.pid,signal.SIGCONT)
                stop(child)
        for log in logs:log.close()
        print("Owned private diagnostic root: "+str(root),flush=True)


if __name__=="__main__":main()
