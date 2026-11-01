#!/usr/bin/env python3
"""Actual published RunEtl: durable HTTP 429 wait, Workbench process loss, recovery.

Reuses the delivered HTTP fixture and public ETL/native APIs. Only this controller's
Workbench and Node children are stopped; original/user stores are never opened.
"""
from __future__ import annotations

import argparse
import sqlite3
import base64
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

REPO = Path(os.environ.get("ELSA_NESTED_REPO", Path.cwd())).resolve()
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


def pipeline(with_lookup=False):
    return {"source": {"kind": "httpJson", "binding": "http-source", "recordsPointer": "/items",
        "fields": [{"name": "id", "pointer": "/id", "type": "int64", "required": True}, {"name": "name", "pointer": "/name", "type": "string", "required": True}],
        "pagination": {"kind": "token", "continuationPointer": "/next", "queryParameter": "pageToken"}},
        "lookup": {"binding":"lookup","schema":[{"name":"id","type":"int64","required":True},{"name":"tag","type":"string","required":True}],
            "sourceKeyField":"id","lookupKeyField":"id"} if with_lookup else None,
        "routes": {"main": {"kind": "http", "binding": "http-output"}, "errors": {"kind": "jsonl", "binding": "errors"}},
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
    parser.add_argument("--embedded-parent", choices=("Root", "Sequence", "Flowchart", "CompensationScope"), help="Invoke the published service model through a real native RunBpmn parent.")
    parser.add_argument("--business-error-boundary", choices=("matching", "different"), help="Attach a coded business-error boundary; requires a business-error or technical-fault journey.")
    service_policy=parser.add_mutually_exclusive_group()
    service_policy.add_argument('--hold-service-retry',action='store_true',help='Pin HoldForRetry/2 and recover one authorized physical retry after a second process loss.')
    service_policy.add_argument('--final-service-fault',action='store_true',help='Pin explicit FinalFault/1 and prove a real current incident cannot retry after semantic finality.')
    parser.add_argument('--held-action',choices=('retry','cancel','exhaust'),default='retry',help='After the held failure survives its second process loss, retry successfully, cancel, or exhaust its budget through a real unavailable bound input.')
    parser.add_argument('--retry-auth-controls',action='store_true',help='Use real owned Identity accounts to verify retry/inspection permissions and foreign-tenant refusal before and after replay.')
    parser.add_argument('--obsolete-result-controls',action='store_true',help='Replay the actual old native result and reject modified/misaddressed inputs while the replacement is in a real HTTP429 wait.')
    parser.add_argument('--service-timer',choices=('worker-first','timer-first','held-interruption'),help='Qualify the actual native ETL worker against its interrupting duration boundary.')
    parser.add_argument('--held-race-order',choices=('retry-first','cancel-first'),help='Submit both actual held commands in separate SDK processes while the first unchanged proposal is gated; recover the retained FIFO after stale ownership refusal.')
    parser.add_argument("--queued-native-stage", choices=("start", "checkpoint"), help="With cancellation before admission, use the actual SDK to retain the selected native queue stage first.")
    parser.add_argument("--dispatcher-assembly", type=Path, help="Built owned SDK client required for the queued-native cut.")
    parser.add_argument("--admission-race-order", choices=("cancel-first", "admit-first"), help="Gate an unchanged actual proposal before its provider transaction, commit the competitor, then cold-recover both writers' obligations.")
    parser.add_argument("--pending-cancellation-crash", action="store_true", help="With model/native cancellation, kill a second real host while its cancellation/result deliveries remain owed.")
    parser.add_argument("--consumed-before-ack-crash", action="store_true", help="With pending cancellation, use the production SDK to consume the result before killing the host with source ACK still owed.")
    parser.add_argument("--native-result-before-etl-cancel", action="store_true", help="Attempt the independent cancelled-worker result while actual ETL cancellation is still pending, then cold-recover both original sources.")
    parser.add_argument("--pending-parent-result-crash", action="store_true", help="With whole-native-parent cancellation, consume the actual worker result through the production SDK, then kill a second host while parent settlement is still owed.")
    parser.add_argument("--successful-parent-result-crash", choices=("pending","consumed","settled"), help="After actual ETL success, capture a second process loss before native parent consumption, after consumption, or after model settlement while source ACK remains owed.")
    outcome_group = parser.add_mutually_exclusive_group()
    outcome_group.add_argument("--business-error", action="store_true", help="Reject one actual HTTP record through its declared Int64 schema.")
    outcome_group.add_argument("--technical-fault", action="store_true", help="After HTTP429/process loss, temporarily make the original retained CSV lookup snapshot unavailable without changing its bytes.")
    outcome_group.add_argument("--pipeline-cancel", action="store_true", help="Cancel the actual ETL run after host recovery and observe native/BPMN cancellation.")
    outcome_group.add_argument("--native-cancel", action="store_true", help="Cancel the actual native worker after recovery and verify cancellation reaches ETL and BPMN.")
    outcome_group.add_argument("--native-parent-cancel", action="store_true", help="Cancel the real outer native RunBpmn parent after recovery.")
    outcome_group.add_argument("--model-cancel", action="store_true", help="Cancel the BPMN model after recovery and verify its durable cancellation reaches the admitted worker and ETL.")
    outcome_group.add_argument("--model-cancel-before-admission", action="store_true", help="Cancel the model while its original native start is owed, kill the host and recover positive retirement without admitting a worker.")
    args = parser.parse_args()
    cancelling=args.pipeline_cancel or args.native_cancel or args.native_parent_cancel or args.model_cancel or args.model_cancel_before_admission
    if args.service_timer and (args.parent!='BpmnModel' or cancelling or args.business_error or args.retry_auth_controls or args.obsolete_result_controls or
            args.pending_cancellation_crash or args.pending_parent_result_crash or args.successful_parent_result_crash or args.queued_native_stage):
        parser.error('Service/timer journeys require the normal BPMN/native ETL path, separately from other capture controls.')
    if args.service_timer and ((args.service_timer=='held-interruption') != bool(args.technical_fault and args.hold_service_retry)):
        parser.error('Held interruption requires --technical-fault --hold-service-retry; other service/timer orders use the successful worker.')
    if args.service_timer in ('worker-first','timer-first') and (args.technical_fault or args.hold_service_retry or args.final_service_fault or args.business_error_boundary):
        parser.error('The worker/timer orders use the successful finite ETL worker without fault controls.')
    if args.technical_fault and args.parent!='BpmnModel':parser.error("Technical fault currently qualifies the direct BPMN/native RunEtl root.")
    if (args.hold_service_retry or args.final_service_fault) and (not args.technical_fault or args.business_error_boundary!='matching'):
        parser.error('The explicit service fault policy journey requires --technical-fault --business-error-boundary matching.')
    if args.held_action!='retry' and not args.hold_service_retry:parser.error('--held-action requires --hold-service-retry.')
    if args.retry_auth_controls and (not args.hold_service_retry or args.held_action!='retry' or args.dispatcher_assembly is None):
        parser.error('Retry authorization controls require --hold-service-retry --held-action retry and --dispatcher-assembly.')
    if args.obsolete_result_controls and (not args.hold_service_retry or args.held_action!='retry' or args.dispatcher_assembly is None or args.retry_auth_controls):
        parser.error('Obsolete-result controls require --hold-service-retry --held-action retry and --dispatcher-assembly, separately from authorization controls.')
    if args.held_race_order and (not args.hold_service_retry or args.held_action!='retry' or args.dispatcher_assembly is None or args.retry_auth_controls or args.obsolete_result_controls or args.service_timer):
        parser.error('Held command races require --hold-service-retry --held-action retry and --dispatcher-assembly, separately from other controls.')
    if args.business_error_boundary and (not (args.business_error or args.technical_fault) or args.parent!='BpmnModel'):
        parser.error("A business-error boundary requires --parent BpmnModel and a business-error or technical-fault journey.")
    caught_business_error=args.business_error and args.business_error_boundary=='matching'
    if cancelling and args.parent != "BpmnModel":parser.error("Cancellation currently qualifies the direct BPMN/native RunEtl root only.")
    if args.embedded_parent and (args.parent!='BpmnModel' or args.model_cancel_before_admission or args.pending_cancellation_crash and not args.native_result_before_etl_cancel):
        parser.error("Embedding requires --parent BpmnModel and the normal admitted-worker recovery journey.")
    if args.native_parent_cancel and not args.embedded_parent:parser.error("Native parent cancellation requires --embedded-parent.")
    if args.pending_parent_result_crash and (not args.native_parent_cancel or args.dispatcher_assembly is None):
        parser.error("The pending native-parent result cut requires --native-parent-cancel and --dispatcher-assembly.")
    if args.successful_parent_result_crash and (not args.embedded_parent or args.dispatcher_assembly is None or cancelling or args.business_error or args.technical_fault or args.pending_cancellation_crash):
        parser.error("The successful native-parent cut requires --embedded-parent, --dispatcher-assembly and the normal successful admitted-worker journey.")
    if args.queued_native_stage and (not args.model_cancel_before_admission or args.dispatcher_assembly is None):
        parser.error("A queued native stage requires --model-cancel-before-admission and --dispatcher-assembly.")
    if args.admission_race_order and args.queued_native_stage != "checkpoint":
        parser.error("An admission race requires the actual queued WorkflowStarted checkpoint cut.")
    if args.pending_cancellation_crash and not (args.model_cancel or args.native_cancel):
        parser.error("A pending cancellation crash requires --model-cancel or --native-cancel.")
    if args.consumed_before_ack_crash and (not args.pending_cancellation_crash or args.dispatcher_assembly is None):
        parser.error("The consumed-before-ACK cut requires --pending-cancellation-crash and --dispatcher-assembly.")
    if args.native_result_before_etl_cancel and (not args.native_cancel or not args.pending_cancellation_crash or
            args.dispatcher_assembly is None or args.consumed_before_ack_crash):
        parser.error("Inverse native-result delivery requires --native-cancel --pending-cancellation-crash --dispatcher-assembly, separately from pre-ACK consumption.")
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
    processes = []; children = []; logs = []; host = None; retry_input_unavailable = None; auth_accounts = None

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

    def auth_controls(stage,route,request,expected_command):
        sessions=[];outcomes=[]
        def observe(client,profile,operation,path,value,expected):
            call=urllib.request.Request(client.base+path,data=None if value is None else json.dumps(value).encode(),headers={'Content-Type':'application/json'})
            try:
                with client.opener.open(call,timeout=30) as response:status=response.status;raw=response.read()
            except urllib.error.HTTPError as error:
                status=error.code;raw=error.read();error.close()
            try:body=json.loads(raw) if raw else {}
            except json.JSONDecodeError:body={}
            has_outcome=isinstance(body,dict) and any(k in body for k in ('result','replay','pending','executionId','items'))
            # Middleware can forbid before binding (403); the BPMN resource authorizer
            # conceals unavailable/denied executions (404). Neither may disclose an outcome.
            permitted={403,404} if expected in (403,404) else {expected}
            outcomes.append({'profile':profile,'operation':operation,'httpStatus':status,'returnedOutcome':has_outcome,'permittedHttpStatuses':sorted(permitted)})
            if status not in permitted or expected!=200 and has_outcome:
                q.write_json(output/('auth-'+stage+'-partial.json'),{'schemaVersion':1,'sourceRevision':source,'stage':stage,'principals':sessions,'outcomes':outcomes,'credentialsIncluded':False})
                raise ValueError(f'Actual authorization control {stage}/{profile}/{operation} returned HTTP {status}, expected {expected}, outcome={has_outcome}.')
            if expected==200 and operation=='read-command' and (body.get('inputId')!=expected_command['inputId'] or body.get('result')!=expected_command['result']):
                raise ValueError('The read-authorized principal did not inspect the exact retained command result.')
            if expected==200 and operation=='retry-replay' and (not body.get('replay') or body.get('pending') or body.get('result')!=expected_command['result']):
                raise ValueError('The retry-authorized principal did not replay its exact already-accepted result.')
        anonymous=q.Api(api.base)
        clients=[('anonymous',anonymous,401,401)]
        for account in auth_accounts:
            client=q.Api(api.base);session=client.request('/_elsa/identity/login',{'username':account['userName'],'password':account['password'],'tenantId':account['tenantId']})
            if session['status']!='authenticated' or session['tenantId']!=account['tenantId'] or set(session['permissions'])!=set(account['permissions']):
                raise ValueError('The real sign-in did not produce the owned account\'s exact tenant and bounded permission set.')
            sessions.append({'profile':account['profile'],'session':session})
            clients.append((account['profile'],client,404 if account['profile']=='foreign' else 200 if account['profile']=='reader' else 403,
                404 if account['profile']=='foreign' else 200 if account['profile']=='retrier' else 403))
        for profile,client,read_status,retry_status in clients:
            observe(client,profile,'read-execution',route,None,read_status)
            observe(client,profile,'read-incidents',route+'/incidents?limit=10',None,read_status)
            observe(client,profile,'read-command',route+'/commands/'+urllib.parse.quote(expected_command['inputId'],safe=''),None,read_status)
            if stage!='before' or profile!='retrier':observe(client,profile,'retry-replay' if stage!='before' else 'retry-before-admission',route+'/retry-service',request,retry_status)
            if profile in ('anonymous','nobody','retrier','foreign'):
                observe(client,profile,'read-unknown-command',route+'/commands/unknown-auth-command',None,read_status)
                pair=[o['httpStatus'] for o in outcomes if o['profile']==profile and o['operation'] in ('read-command','read-unknown-command')]
                if len(set(pair))!=1:raise ValueError('Denied existing/missing command requests exposed a resource-existence distinction.')
        evidence={'schemaVersion':1,'kind':'actual-bpmn-retry-http-authorization','sourceRevision':source,'stage':stage,'modelExecutionId':model_execution,
            'principals':sessions,'outcomes':outcomes,'expectedCommand':expected_command,'credentialsIncluded':False,'mockAuthenticationUsed':False}
        q.write_json(output/('auth-'+stage+'.json'),evidence)
        return evidence

    try:
        fixture_script=REPO/"e2e-tests/etl/EtlHttpFixture.mjs";ready=root/"http-ready.json"
        retry_after=60 if cancelling or args.service_timer=='timer-first' else 20
        fixture=start(["node",str(fixture_script),"--ready-file",str(ready),"--source-retry-after-seconds",str(retry_after)]+(["--invalid-last-id"] if args.business_error else [])+(['--allow-retry-rearm'] if args.obsolete_result_controls else []),"http-fixture")
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
        if args.technical_fault:
            lookup=root/'lookup.csv';lookup.write_bytes(b'id,tag\n1,one\n2,two\n3,three\n4,four\n')
            shutil.copyfile(lookup,output/'lookup-source.csv')
            features['EtlRuntime']['FileBindings'].append({'TenantId':'default','PersistencePartition':'default','Alias':'lookup','Role':'Lookup','Revision':'lookup-v1',
                'Path':str(lookup),'ExpectedSha256':q.digest(lookup),'IsCurrent':True,'ImmutableInput':True,'AtomicPublication':False})
        if direct_bpmn:
            features.update({"ActivitiesBpmnHosting":{"AdmitNewExecutions":True},"ActivitiesBpmnHostingApi":{}})
            if args.model_cancel_before_admission:features["WorkflowsRuntimeResumption"]={"SweepIntervalSeconds":300}
            elif args.pending_cancellation_crash or args.pending_parent_result_crash or args.successful_parent_result_crash:features["WorkflowsRuntimeResumption"]={"SweepIntervalSeconds":.2}
            elif args.business_error_boundary or args.service_timer:features["WorkflowsRuntimeResumption"]={"SweepIntervalSeconds":.2}
            # This first model profile qualifies the built-in single-node actor, as the hosting tests do.
            for name in ("WorkflowsRuntimeDistributed", "WorkflowsRuntimeDistributedEntityFrameworkCorePersistence", "WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence"):
                features.pop(name, None)
        features["WorkflowsRuntimeResumption"]={"SweepIntervalSeconds":.2}
        q.write_json(root/"shells.json",shells)
        api=launch();definition="recovery-"+uuid.uuid4().hex[:12]
        bindings=api.request("/etl/v2/bindings?limit=100")
        draft=api.request("/etl/v2/definitions",{"id":definition,"pipeline":pipeline(args.technical_fault)})
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
        def model(worker, identity):
            pin={field:worker[field] for field in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')}
            process={'processId':identity,'elements':[{'elementId':'start','elementType':'startEvent'},{'elementId':'service','elementType':'serviceTask','bindingRef':'worker'},{'elementId':'end','elementType':'endEvent'}],
                'sequenceFlows':[{'flowId':'start-work','sourceRef':'start','targetRef':'service'},{'flowId':'work-end','sourceRef':'service','targetRef':'end'}]}
            return api.request('/bpmn/artifacts',{'requestId':identity,'definition':{'formatVersion':2,'process':process,'humanBindings':[],
                'serviceBindings':[{'bindingId':'worker','executable':pin,'businessErrorCode':'etl.rows-rejected'}]}})
        model_publication=model(publication,'nested-inner-'+definition)
        middle=node('run-bpmn','Elsa.Activities.Bpmn.Activities.RunBpmn',[q.literal('Model',model_publication['sourceReferenceId'])])
        middle_definition=api.request('/design/workflows/definitions/submit',{'name':'Nested model service '+definition,'state':{'variables':[],'inputs':[],'outputs':[],'rootActivity':middle}})
        middle_publication=api.request('/publishing/workflows/'+middle_definition['version']['id']+'/publish',{})
        outer_publication=model(middle_publication,'nested-outer-'+definition)
        accepted=api.request('/bpmn/executions',{'requestId':'start-nested-cancel','sourceReferenceId':outer_publication['sourceReferenceId']})
        outer_execution=accepted['executionId']
        instances=wait_for(lambda:api.request('/runtime/workflows/instances'),lambda rows:len(rows)==2,'Both nested native workers were not admitted.')
        native_parent_execution=next(x['workflowExecutionId'] for x in instances if x['artifactId']==middle_publication['artifactId'])
        execution=next(x['workflowExecutionId'] for x in instances if x['artifactId']==publication['artifactId'])
        parent=api.view(native_parent_execution);activity=next(x for x in parent['activities'] if x['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn')
        detail=api.request('/runtime/workflows/instances/'+native_parent_execution+'/activity-executions/'+activity['activityExecutionId'])
        model_execution=detail['bookmarks'][0]['stimulusHash']
        runs=wait_for(lambda:api.request('/etl/runs?limit=100'),lambda rows:len(rows['items'])==1,'The nested RunEtl worker did not admit its run.')
        run=runs['items'][0]['runId'];escaped=urllib.parse.quote(run,safe='')
        def view():
            return {'outerModel':api.request('/bpmn/executions/'+outer_execution),'innerModel':api.request('/bpmn/executions/'+model_execution),
                'outerNative':api.view(native_parent_execution),'innerNative':api.view(execution),'run':api.request('/etl/runs/'+escaped),
                'partitions':api.request('/etl/runs/'+escaped+'/connector-partitions?limit=50')}
        def ready(value):
            rows=value['partitions']['items']
            return len(rows)==2 and any(x['http']['fetch'] and x['http']['fetch']['lastStatusCode']==429 and x['http']['fetch']['phase']=='waiting' for x in rows) and any(x['http']['exhausted'] and x['owedBatch'] is None for x in rows)
        before=wait_for(view,ready,'No actual nested HTTP429 retry and completed sibling was observed.',45)
        os.kill(host.pid,signal.SIGSTOP);wait_for(lambda:q.process_field(host.pid,'stat'),lambda value:'T' in value,'Owned host did not quiesce.',5)
        stores=snapshots('waiting');before_wire=wire()
        q.write_json(output/'waiting-api.json',before);q.write_json(output/'waiting-wire.json',before_wire)
        checks={}
        for name,model_id,worker_id in [('outer',outer_execution,native_parent_execution),('inner',model_execution,execution)]:
            checks[name]=json.loads(subprocess.check_output(['dotnet',str(reader),'--read-bpmn-native',str(root/'elsa.db'),model_id,worker_id],text=True,timeout=30))
            q.write_json(output/('waiting-'+name+'-physical.json'),checks[name])
        stop(host,crash=True);assert host.returncode==-signal.SIGKILL
        record={'schemaVersion':1,'kind':'actual-pre-correction-nested-model-http-wait','sourceRevision':source,'products':products,
            'outerModelPublication':outer_publication,'outerModelExecutionId':outer_execution,'middleNativePublication':middle_publication,'middleNativeExecutionId':native_parent_execution,
            'innerModelPublication':model_publication,'innerModelExecutionId':model_execution,'etlWorkerPublication':publication,'etlWorkerExecutionId':execution,
            'etlPublication':published,'runId':run,'stores':stores,'processes':processes,'predecessorExitCode':host.returncode,'controllerSha256':q.digest(Path(__file__)),
            'scopeLimits':['Actual published finite acyclic model/native/model/native/ETL chain and whole coupled stores before correcting delivery behavior.',
                'Original stores contain the real admitted HTTP429 wait and completed sibling; no SQL state, clock, receipt, pin, queue or claim was edited.',
                'Cancellation interleaving, premature ancestor settlement, corrected recovery and post-deadline stability are not yet proved by this waiting capture.']}
        q.write_json(output/'process.json',record)
        print('Actual nested model/native/model/native/ETL waiting checkpoint retained; owned Workbench SIGKILL confirmed.',flush=True)
        api=launch()
        cancellation=api.request('/bpmn/executions/'+outer_execution+'/cancel',{'requestId':'cancel-nested-root','reason':'Settle the original finite nested model and ETL worker.'})
        q.write_json(output/'cancel-response.json',cancellation)
        observed=[];premature=False;deadline=time.monotonic()+30
        connection=sqlite3.connect((root/'elsa.db').as_uri()+'?mode=ro',uri=True,timeout=2)
        try:
            while time.monotonic()<deadline:
                frames={}
                for lifecycle,content in connection.execute('select Lifecycle,ContentJson from elsa_runtime_model_execution'):
                    state=json.loads(content);identity=base64.b64decode(state['executionId']).decode('utf-16le')
                    frames[identity]={'lifecycle':lifecycle,'revision':state['revision'],'state':state}
                a,b=frames[outer_execution],frames[model_execution]
                owed=[x for x in b['state']['payload'].get('cancellations',[]) if not x['settled']]
                key=(a['lifecycle'],a['revision'],b['lifecycle'],b['revision'],len(owed))
                if not observed or observed[-1]['key']!=list(key):observed.append({'atUtc':datetime.now(timezone.utc).isoformat(),'key':key,'frames':frames})
                if a['lifecycle']==3 and owed:
                    premature=True;break
                if a['lifecycle']==3 and b['lifecycle']==3:break
                time.sleep(.005)
        finally:connection.close()
        os.kill(host.pid,signal.SIGSTOP);wait_for(lambda:q.process_field(host.pid,'stat'),lambda value:'T' in value,'Owned cancellation host did not quiesce.',5)
        cancelled_stores=snapshots('after-cancel');after_wire=wire()
        q.write_json(output/'cancellation-observations.json',observed);q.write_json(output/'after-cancel-wire.json',after_wire)
        for name,model_id,worker_id in [('outer',outer_execution,native_parent_execution),('inner',model_execution,execution)]:
            checked=json.loads(subprocess.check_output(['dotnet',str(reader),'--read-bpmn-native',str(root/'elsa.db'),model_id,worker_id],text=True,timeout=30))
            q.write_json(output/('after-cancel-'+name+'-physical.json'),checked)
        stop(host,crash=True);assert host.returncode==-signal.SIGKILL
        record.update({'secondHostExitCode':host.returncode,'afterCancelStores':cancelled_stores,'prematureAncestorSettlementObserved':premature,'naturalOrderOnly':True})
        q.write_json(output/'process.json',record)
        print('Actual natural nested cancellation observation: premature ancestor settlement='+str(premature),flush=True)

    except Exception as failure:
        if not (output/'failure.json').exists():
            for child in children:
                if child is not host and child is not fixture:stop(child)
            if host is not None and host.poll() is None and 'T' in q.process_field(host.pid,'stat'):os.kill(host.pid,signal.SIGCONT)
            stop(host)
            failed_stores=snapshots('failed') if host is not None and all((root/(name+'.db')).exists() for name in ('elsa','etl-runtime','etl-design')) else {}
            q.write_json(output/'failure.json',{'sourceRevision':source,'failureType':type(failure).__name__,'processes':processes,'products':products,
                'failedStores':failed_stores,'modelExecutionId':locals().get('model_execution'),'nativeParentExecutionId':locals().get('native_parent_execution')})
        raise
    finally:
        for child in reversed(children):
            if child.poll() is None:
                if child is host and "T" in q.process_field(child.pid,"stat"):os.kill(child.pid,signal.SIGCONT)
                stop(child)
        for log in logs:log.close()
        if retry_input_unavailable is not None and retry_input_unavailable.exists() and not (root/'lookup.csv').exists():retry_input_unavailable.rename(root/'lookup.csv')
        print("Owned private diagnostic root: "+str(root),flush=True)


if __name__=="__main__":main()
