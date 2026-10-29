#!/usr/bin/env python3
"""Actual published RunEtl: durable HTTP 429 wait, Workbench process loss, recovery.

Reuses the delivered HTTP fixture and public ETL/native APIs. Only this controller's
Workbench and Node children are stopped; original/user stores are never opened.
"""
from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
import gzip
import importlib.util
import json
import os
from pathlib import Path
import re
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


def utc_instant(value):
    # .NET emits variable fractional digits; Python3.9 accepts only three or six.
    normalized=re.sub(r"\.(\d{1,7})(?=[+-])",lambda match:"."+match[1].ljust(6,"0")[:6],value.replace("Z","+00:00"))
    return datetime.fromisoformat(normalized)


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
    parser.add_argument("--parent", choices=("Sequence", "Flowchart", "RunEtl", "BpmnModel"), default="Sequence")
    outcome_group = parser.add_mutually_exclusive_group()
    outcome_group.add_argument("--business-error", action="store_true", help="Reject one actual HTTP record through its declared Int64 schema.")
    outcome_group.add_argument("--pipeline-cancel", action="store_true", help="Cancel the actual ETL run after host recovery and observe native/BPMN cancellation.")
    outcome_group.add_argument("--native-cancel", action="store_true", help="Cancel the actual native worker after recovery and verify cancellation reaches ETL and BPMN.")
    outcome_group.add_argument("--model-cancel", action="store_true", help="Cancel the BPMN model after recovery and verify its durable cancellation reaches the admitted worker and ETL.")
    outcome_group.add_argument("--model-cancel-before-admission", action="store_true", help="Cancel the model while its original native start is owed, kill the host and recover positive retirement without admitting a worker.")
    args = parser.parse_args()
    cancelling=args.pipeline_cancel or args.native_cancel or args.model_cancel or args.model_cancel_before_admission
    if cancelling and args.parent != "BpmnModel":parser.error("Cancellation currently qualifies the direct BPMN/native RunEtl root only.")
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
    if subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=all"], cwd=REPO, text=True).strip():
        raise ValueError("Commit the journey source before recording exact-source evidence.")
    assembly = args.workbench_assembly.resolve(strict=True); reader = args.reader_assembly.resolve(strict=True)
    direct_bpmn = args.parent == "BpmnModel"
    products = json.loads(subprocess.check_output(["dotnet", str(reader), "--inspect-native-bpmn-products" if direct_bpmn else "--inspect-etl-products", str(assembly)], text=True))
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
        if host.poll() is None and "T" not in q.process_field(host.pid,"stat"):
            raise ValueError("Quiesce or stop the owned Workbench before copying coupled stores.")
        result={}
        for name in ("elsa", "etl-runtime", "etl-design"):
            backup=root/f"{label}-{name}.sqlite"
            observed=json.loads(subprocess.check_output(["dotnet",str(reader),"--backup-sqlite",str(root/(name+".db")),str(backup)],text=True,timeout=30))
            if observed["readOnly"] is not True or observed["snapshotSha256"]!=q.digest(backup):raise ValueError("The provider backup receipt differs from its actual closed database.")
            target=output/(label+"-"+name+".sqlite.gz")
            with backup.open("rb") as raw,gzip.open(target,"wb") as zipped:shutil.copyfileobj(raw,zipped)
            result[name]={"file":target.name,"databaseSha256":q.digest(backup),"gzipSha256":q.digest(target),"observer":observed}
        return result

    try:
        fixture_script=REPO/"e2e-tests/etl/EtlHttpFixture.mjs";ready=root/"http-ready.json"
        retry_after=60 if cancelling else 20
        fixture=start(["node",str(fixture_script),"--ready-file",str(ready),"--source-retry-after-seconds",str(retry_after)]+(["--invalid-last-id"] if args.business_error else []),"http-fixture")
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
        if direct_bpmn:
            features.update({"ActivitiesBpmnHosting":{"AdmitNewExecutions":True},"ActivitiesBpmnHostingApi":{}})
            if args.model_cancel_before_admission:features["WorkflowsRuntimeResumption"]={"SweepIntervalSeconds":300}
            # This first model profile qualifies the built-in single-node actor, as the hosting tests do.
            for name in ("WorkflowsRuntimeDistributed", "WorkflowsRuntimeDistributedEntityFrameworkCorePersistence", "WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence"):
                features.pop(name, None)
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
        elif args.parent=="Flowchart":
            structure={"kind":"elsa.flowchart.structure","schemaVersion":"1.0.0","payload":{"startNodeId":"etl","activities":[etl,after],"connections":[{"source":{"nodeId":"etl","port":port},"target":{"nodeId":"after","port":"Done"}} for port in ("Done","BusinessError")]}}
            key="Elsa.Activities.Flowchart.Activities.Flowchart"
        root_node=etl if args.parent in ("RunEtl","BpmnModel") else node("root",key,structure=structure)
        native=api.request("/design/workflows/definitions/submit",{"name":"Actual ETL recovery "+args.parent+" "+definition,"state":{"variables":[],"inputs":[],"outputs":[],"rootActivity":root_node}})
        publication=api.request("/publishing/workflows/"+native["version"]["id"]+"/publish",{})
        model_publication=None;model_execution=None
        if direct_bpmn:
            worker_pin={field:publication[field] for field in ("artifactId","artifactHash","artifactVersion","definitionId","definitionVersionId")}
            process={"processId":"direct-etl","elements":[{"elementId":"start","elementType":"startEvent"},{"elementId":"service","elementType":"serviceTask","bindingRef":"pipeline"},{"elementId":"end","elementType":"endEvent"}],
                "sequenceFlows":[{"flowId":"to-service","sourceRef":"start","targetRef":"service"},{"flowId":"to-end","sourceRef":"service","targetRef":"end"}]}
            model_publication=api.request("/bpmn/artifacts",{"requestId":"publish-model","definition":{"formatVersion":2,"process":process,"humanBindings":[],
                "serviceBindings":[{"bindingId":"pipeline","executable":worker_pin,"businessErrorCode":"etl.rows-rejected"}]}})
            executed=api.request("/bpmn/executions",{"requestId":"start-model","sourceReferenceId":model_publication["sourceReferenceId"]})
            model_execution=executed["executionId"]
            if args.model_cancel_before_admission:
                def admission_view():
                    return {"model":api.request("/bpmn/executions/"+model_execution),
                        "native":api.request("/runtime/workflows/instances"),"runs":api.request("/etl/runs?limit=100")}
                def admission_checked():
                    return json.loads(subprocess.check_output(["dotnet",str(reader),"--read-bpmn-retirement",str(root/"elsa.db"),model_execution],text=True,timeout=30))
                def quiesce():
                    os.kill(host.pid,signal.SIGSTOP)
                    wait_for(lambda:q.process_field(host.pid,"stat"),lambda value:"T" in value,"The owned Workbench did not quiesce.",5)
                try:
                    before=admission_view();before_wire=wire();quiesce()
                    before_stores=snapshots("before");before_checked=admission_checked()
                    q.write_json(output/"before.json",before);q.write_json(output/"before-checked.json",before_checked);q.write_json(output/"before-wire.json",before_wire)
                    if (before["model"]["status"]!="Running" or before["native"] or before["runs"]["items"] or
                            before_checked["worker"] is not None or before_checked["model"]["revision"]!=1 or len(before_checked["sources"])!=1 or
                            before_checked["sources"][0]["status"]==2):
                        raise ValueError("The actual native worker was already admitted before the cancellation cut.")
                    os.kill(host.pid,signal.SIGCONT)
                    cancellation=api.request("/bpmn/executions/"+model_execution+"/cancel",
                        {"requestId":"cancel-before-native-admission","reason":"Retire the original native start before admission."})
                    pending=admission_view();quiesce();pending_stores=snapshots("pending");pending_checked=admission_checked()
                    q.write_json(output/"cancel-response.json",cancellation);q.write_json(output/"pending.json",pending);q.write_json(output/"pending-checked.json",pending_checked)
                    if (cancellation["pending"] or cancellation["result"]["status"]!="Accepted" or pending["model"]["status"]!="TerminalPendingSettlement" or
                            pending_checked["model"]["revision"]!=2 or pending_checked["worker"] is not None or pending["native"] or pending["runs"]["items"] or
                            len(pending_checked["sources"])!=2 or any(item["status"]==2 for item in pending_checked["sources"])):
                        raise ValueError("The actual pending cancellation/start responsibility was not retained before process loss.")
                    stop(host,crash=True);exit_code=host.returncode
                    if exit_code!=-signal.SIGKILL:raise ValueError("The owned predecessor did not lose its actual process.")
                    print("Actual BPMN cancellation retained before native admission; owned predecessor killed.",flush=True)
                    features["WorkflowsRuntimeResumption"]={"SweepIntervalSeconds":.2};q.write_json(root/"shells.json",shells)
                    api=launch()
                    finished_checked=wait_for(admission_checked,lambda value:value["model"]["lifecycle"]==3 and
                        len(value["inputs"])==3 and len(value["sources"])==2 and all(item["status"]==2 for item in value["sources"]) and
                        not value["queuedNativeWork"] and not value["queuedModelInputs"],"The real cancelled native start did not retire after restart.",90)
                    finished=admission_view();finished_wire=wire();stop(host)
                    finished_checked=admission_checked();finished_stores=snapshots("finished")
                    q.write_json(output/"finished.json",finished);q.write_json(output/"finished-checked.json",finished_checked);q.write_json(output/"finished-wire.json",finished_wire)
                    if finished["native"] or finished["runs"]["items"] or finished_checked["worker"] is not None or finished_wire!=before_wire:
                        raise ValueError("A retired native start created a worker, ETL run or external effect.")
                    api=launch();until=time.monotonic()+2
                    while time.monotonic()<until:
                        if admission_view()!=finished or wire()!=finished_wire:raise ValueError("The retired native start revived across another host.")
                        time.sleep(.2)
                    retained=admission_view();retained_wire=wire();stop(host);retained_checked=admission_checked()
                    q.write_json(output/"retained.json",retained);q.write_json(output/"retained-checked.json",retained_checked);q.write_json(output/"retained-wire.json",retained_wire)
                    if retained_checked!=finished_checked:raise ValueError("The actual start retirement proof changed after another host.")
                    q.write_json(output/"process.json",{"schemaVersion":1,"kind":"actual-bpmn-cancellation-before-native-admission","sourceRevision":source,
                        "products":products,"processes":processes,"predecessorExitCode":exit_code,"modelPublication":model_publication,"publication":publication,
                        "etlPublication":published,"modelExecutionId":model_execution,"origin":before_checked["origin"],"beforeStores":before_stores,
                        "pendingStores":pending_stores,"finishedStores":finished_stores,"sweepIntervalsSeconds":[300,.2,.2],
                        "controllerSha256":q.digest(Path(__file__)),"fixtureSha256":q.digest(fixture_script),
                        "scopeLimits":["Original native start source owed before first delivery; positive model retirement across actual SIGKILL and fresh standard hosts.",
                            "The real ETL worker is published and pinned, but no native workflow, ETL run or HTTP effect is admitted.",
                            "Queued native-start checkpoint/admission races, pending result crashes, embedding, held retry and final upgrade/Studio remain separate gates."]})
                    print("Original native start retired after process loss; no worker or ETL run across a third host.",flush=True)
                except Exception as failure:
                    stop(host);failed_stores=snapshots("failed")
                    q.write_json(output/"failure.json",{"sourceRevision":source,"failureType":type(failure).__name__,"failedStores":failed_stores,"processes":processes,"products":products})
                    raise
                return
            instances=wait_for(lambda:api.request("/runtime/workflows/instances"),lambda value:len(value)==1,"The BPMN binding did not admit its real native worker.")
            execution=instances[0]["workflowExecutionId"]
        else:
            executed=api.request("/runtime/workflows/executables/"+publication["artifactId"]+"/execute",{"sourceReferenceId":publication["sourceReferenceId"]})
            execution=executed["workflowExecutionId"]
        summaries=wait_for(lambda:api.request("/etl/runs?limit=100"),lambda value:len(value["items"])==1,"The real RunEtl activity did not admit its run.")
        run=summaries["items"][0]["runId"];escaped=urllib.parse.quote(run,safe="")
        def view():
            value={"native":api.view(execution),"run":api.request("/etl/runs/"+escaped),"partitions":api.request("/etl/runs/"+escaped+"/connector-partitions?limit=50")}
            if direct_bpmn:value["model"]=api.request("/bpmn/executions/"+model_execution)
            return value
        def checked_native_model():
            return json.loads(subprocess.check_output(["dotnet",str(reader),"--read-bpmn-native",str(root/"elsa.db"),model_execution,execution],text=True,timeout=30))
        def cut_ready(value):
            rows=value["partitions"]["items"]
            return len(rows)==2 and any(item["http"]["fetch"] and item["http"]["fetch"]["lastStatusCode"]==429 and item["http"]["fetch"]["phase"]=="waiting" for item in rows) and any(item["http"]["exhausted"] and item["owedBatch"] is None for item in rows)
        try:before=wait_for(view,cut_ready,"No real 429 wait with a completed sibling was observed.",30)
        except TimeoutError:
            q.write_json(output/"failed-before.json",view());q.write_json(output/"failed-wire.json",wire());raise
        retry=next(item["http"]["fetch"] for item in before["partitions"]["items"] if item["http"]["fetch"] and item["http"]["fetch"]["lastStatusCode"]==429)
        if retry["attemptCount"]!=1 or utc_instant(retry["notBefore"])<=datetime.now(timezone.utc):
            raise ValueError("The crash cut did not retain its original future retry deadline.")
        if host.poll() is not None or int(q.process_field(host.pid,"ppid"))!=os.getpid():raise ValueError("The cut no longer belongs to this live controller child.")
        os.kill(host.pid,signal.SIGSTOP)
        wait_for(lambda:q.process_field(host.pid,"stat"),lambda value:"T" in value,"The owned Workbench did not stop at its actual cut.",5)
        cut_at=datetime.now(timezone.utc).isoformat()
        before_wire=wire();q.write_json(output/"before.json",before);q.write_json(output/"before-wire.json",before_wire)
        if len([item for item in before_wire["requests"] if item.get("shard")=="shard-b"])!=1:
            raise ValueError("The actual source already retried before the recorded crash cut.")
        before_stores=snapshots("before")
        if direct_bpmn:q.write_json(output/"before-checked-model.json",checked_native_model())
        stop(host,crash=True);exit_code=host.returncode
        if exit_code!=-signal.SIGKILL:raise ValueError("The predecessor did not exit through actual process loss.")
        print("Observed durable 429 and completed sibling; owned predecessor terminated.",flush=True)
        api=launch();recovered=view();q.write_json(output/"recovered.json",recovered)
        if args.pipeline_cancel:
            if recovered["run"]["status"]!="running":raise ValueError("The actual pipeline completed before its cancellation cut.")
            cancelled=api.request("/etl/runs/"+escaped+"/cancel",{})
            if not cancelled["cancellationAccepted"] or cancelled["terminalResult"]["outcome"]!="cancelled":
                raise ValueError("The real ETL cancel API did not accept the running pipeline's cancellation.")
            q.write_json(output/"cancel-response.json",cancelled)
        elif args.model_cancel:
            if recovered["run"]["status"]!="running":raise ValueError("The actual ETL run completed before BPMN cancellation.")
            cancelled=api.request("/bpmn/executions/"+urllib.parse.quote(model_execution,safe="")+"/cancel",
                {"requestId":"cancel-actual-bpmn-etl","reason":"Cancel the recovered BPMN service invocation."})
            q.write_json(output/"model-cancel-response.json",cancelled)
        elif args.native_cancel:
            if recovered["run"]["status"]!="running":raise ValueError("The actual ETL run completed before native cancellation.")
            request=urllib.request.Request(api.base+"/runtime/workflows/alteration-plans",data=json.dumps({
                "target":{"executionIds":[execution]},"alterations":[{"kind":"CancelWorkflow","schemaVersion":1,"payload":{}}]}).encode(),
                headers={"Content-Type":"application/json","Idempotency-Key":"cancel-actual-native-etl-worker"})
            with api.opener.open(request,timeout=30) as response:cancelled=json.load(response)
            q.write_json(output/"native-cancel-response.json",cancelled)
        outcome="cancelled" if cancelling else "businessError" if args.business_error else "succeeded"
        native_terminal="Cancelled" if cancelling else "Completed"
        model_terminal="Cancelled" if cancelling else "Faulted" if args.business_error else "Completed"
        try:
            finished=wait_for(view,lambda value:value["run"]["status"]==outcome and value["native"]["instance"]["status"]==native_terminal and
                (not direct_bpmn or value["model"]["status"]=="TerminalSettled" and value["model"]["result"]["status"]==model_terminal),"The actual ETL/native/model chain did not finish after process loss.",120)
        except Exception as failure:
            # Retain actual failed production state before any integration writer is changed.
            q.write_json(output/"failed-after-request.json",view());q.write_json(output/"failed-wire.json",wire())
            stop(host);failed_stores=snapshots("failed")
            if direct_bpmn:q.write_json(output/"failed-checked-model.json",checked_native_model())
            q.write_json(output/"failure.json",{"sourceRevision":source,"failureType":type(failure).__name__,"pipelineCancel":args.pipeline_cancel,
                "nativeCancel":args.native_cancel,"modelCancel":args.model_cancel,"processes":processes,"products":products,"workflowExecutionId":execution,"modelExecutionId":model_execution,
                "runId":run,"beforeStores":before_stores,"failedStores":failed_stores,"publication":publication,"modelPublication":model_publication,
                "predecessorExitCode":exit_code,"controllerSha256":q.digest(Path(__file__))})
            raise
        if direct_bpmn:
            checked=wait_for(checked_native_model,lambda value:value["resultSource"] is not None and value["resultSource"]["status"]==2 and value["resultInput"] is not None,
                "The native result remained unacknowledged after model consumption.",90)
            q.write_json(output/"finished-checked-model.json",checked)
        if args.model_cancel:
            command=api.request("/bpmn/executions/"+urllib.parse.quote(model_execution,safe="")+"/commands/cancel-actual-bpmn-etl")
            if command["pending"] or command["result"]["status"]!="Accepted" or command["result"]["lifecycle"]!="TerminalPendingSettlement":
                raise ValueError("The BPMN cancel command did not retain its durable pending-settlement decision.")
            q.write_json(output/"model-cancel-command.json",command)
        result=finished["run"];rows=finished["partitions"]["items"];native_view=finished["native"]
        if (result["formatVersion"]!=2 or result["selection"]!=selection or result["terminalResult"]["outcome"]!=outcome or
                result["acceptedRecords"]!=(2 if cancelling else 3 if args.business_error else 4) or result["rejectedRecords"]!=(1 if args.business_error else 0) or result["filteredRecords"]!=0 or result["outstandingRecords"]!=0 or result["outstandingBytes"]!=0 or
                len(rows)!=2 or finished["partitions"]["nextCursor"] is not None or any(item["owedBatch"] is not None for item in rows) or
                not cancelling and any(not item["http"]["exhausted"] or item["http"]["pageSequence"]!=1 for item in rows)):
            raise ValueError("The recovered ETL run did not retain the expected output accounting and exact partitions.")
        if native_view["incidents"] or len(native_view["activities"])!=(1 if args.parent in ("RunEtl","BpmnModel") else 3) or any(item["status"]!=native_terminal for item in native_view["activities"]):
            raise ValueError("The recovered native parent did not execute RunEtl and its successor once.")
        old_activities={(item.get("authoredActivityId") or item["executableNodeId"]):item for item in before["native"]["activities"]}
        etl_activity=next(item for item in native_view["activities"] if (item.get("authoredActivityId") or item["executableNodeId"])=="etl")
        if etl_activity["outcomeNames"]!=([] if cancelling else ["BusinessError" if args.business_error else "Done"]):
            raise ValueError("The real RunEtl activity did not preserve its typed terminal outcome.")
        if "after" in old_activities or old_activities["etl"]["activityExecutionId"]!=next(item["activityExecutionId"] for item in native_view["activities"] if (item.get("authoredActivityId") or item["executableNodeId"])=="etl"):
            raise ValueError("The native RunEtl invocation was recreated or its successor ran before the cut.")
        for field in ("artifactId","artifactHash","artifactVersion","definitionId","definitionVersionId"):
            if before["native"]["instance"][field]!=native_view["instance"][field]:raise ValueError("The actual native executable full pin changed during ETL recovery.")
        q.write_json(output/"finished.json",finished);finished_wire=wire();q.write_json(output/"finished-wire.json",finished_wire)
        b_requests=[item for item in finished_wire["requests"] if item.get("shard")=="shard-b"]
        if (cancelling and (len(b_requests)!=1 or b_requests[0]["status"]!=429) or
                not cancelling and (len(b_requests)!=3 or [item["status"] for item in b_requests]!=[429,200,200] or b_requests[1]["receivedAtMs"]-b_requests[0]["receivedAtMs"]<20000)):
            raise ValueError("The actual source retried early or failed to resume its original two-page shard.")
        records=[item for post in finished_wire["outputPosts"] for item in post["records"]]
        expected_records={1:"Ada",2:"Grace"} if cancelling else {1:"Ada",2:"Grace",3:"Linus"} if args.business_error else {1:"Ada",2:"Grace",3:"Linus",4:"Margaret"}
        if {item["id"]:item["name"] for item in records}!=expected_records or any(not post["hasIdempotencyKey"] for post in finished_wire["outputPosts"]):
            raise ValueError("The actual HTTP receiver lacks mapped output or idempotency headers.")
        errors=[json.loads(line) for file in (root/"errors").glob("*.jsonl") for line in file.read_text().splitlines() if line.strip()]
        if len(errors)!=(1 if args.business_error else 0):raise ValueError("The real safe-error destination differs from the rejected record count.")
        q.write_json(output/"safe-errors.json",errors)
        stop(host);finished_stores=snapshots("finished")
        api=launch()
        observed_cancelled_after_deadline=None
        if cancelling:
            deadline=utc_instant(retry["notBefore"])+timedelta(seconds=2)
            while datetime.now(timezone.utc)<deadline:
                if view()!=finished or wire()!=finished_wire:raise ValueError("Cancelled work changed while its original HTTP retry became due.")
                time.sleep(.25)
            observed_cancelled_after_deadline=datetime.now(timezone.utc).isoformat()
        retained=view();retained_wire=wire();q.write_json(output/"retained.json",retained);q.write_json(output/"retained-wire.json",retained_wire)
        if direct_bpmn:q.write_json(output/"retained-checked-model.json",checked_native_model())
        stop(host)
        if retained!=finished or retained_wire!=finished_wire:raise ValueError("The terminal pair or physical HTTP observations changed after another process.")
        q.write_json(output/"process.json",{"schemaVersion":1,"kind":"actual-direct-bpmn-native-etl-model-cancellation" if args.model_cancel else "actual-direct-bpmn-native-etl-worker-cancellation" if args.native_cancel else "actual-direct-bpmn-native-etl-pipeline-cancellation" if args.pipeline_cancel else "actual-direct-bpmn-native-etl-http-recovery" if direct_bpmn else "actual-native-etl-http-recovery","sourceRevision":source,"products":products,"parent":args.parent,"processes":processes,"predecessorExitCode":exit_code,
            "definition":definition,"selection":selection,"workflowExecutionId":execution,"runId":run,"publication":publication,"etlPublication":published,"bindings":bindings,
            "fixtureSha256":q.digest(fixture_script),"controllerSha256":q.digest(Path(__file__)),"cutAtUtc":cut_at,"businessError":args.business_error,"pipelineCancel":args.pipeline_cancel,"nativeCancel":args.native_cancel,"modelCancel":args.model_cancel,"retryAfterSeconds":retry_after,"beforeStores":before_stores,"finishedStores":finished_stores,
            **({"observedCancelledAfterRetryDeadlineAtUtc":observed_cancelled_after_deadline} if cancelling else {}),
            **({"modelPublication":model_publication,"modelExecutionId":model_execution} if direct_bpmn else {}),
            "scopeLimits":["BPMN-originated cancellation of an admitted native RunEtl worker after process recovery; cancellation before admission, pending cancel/result crashes, embedding, held retry and final upgrade/Studio remain unproved." if args.model_cancel else "Native-worker-originated cancellation into the actual ETL run and direct BPMN only; model-to-worker cancellation, embedding, held retry and final upgrade/Studio remain unproved." if args.native_cancel else "Pipeline-originated cancellation through one actual direct BPMN/native RunEtl root only; reverse model-to-worker cancellation, embedding, held fault retry and final upgrade/Studio remain unproved." if args.pipeline_cancel else "Direct start/service/end with one real closed native RunEtl worker; embedding, broader service/event shapes, cancellation propagation and held terminal-fault retry remain unproved." if direct_bpmn else "Real transient HTTP retry and native RunEtl recovery only; no BPMN/model worker bridge or held terminal-fault retry is proved.","The crash cut uses SIGSTOP only to copy committed stores, then SIGKILL; no persisted state or clock is edited.","External HTTP effects remain at least once."]})
        print("Actual ETL/native"+("/BPMN chain" if direct_bpmn else " journey")+" completed and survived another Workbench process.",flush=True)
    finally:
        for child in reversed(children):
            if child.poll() is None:
                if child is host and "T" in q.process_field(child.pid,"stat"):os.kill(child.pid,signal.SIGCONT)
                stop(child)
        for log in logs:log.close()
        print("Owned private diagnostic root: "+str(root),flush=True)


if __name__=="__main__":main()
