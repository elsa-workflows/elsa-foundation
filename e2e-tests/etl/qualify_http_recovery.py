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
        model_publication=None;model_execution=None;native_parent_execution=None;native_parent_publication=None
        if direct_bpmn:
            worker_pin={field:publication[field] for field in ("artifactId","artifactHash","artifactVersion","definitionId","definitionVersionId")}
            process={"processId":"direct-etl","elements":[{"elementId":"start","elementType":"startEvent"},{"elementId":"service","elementType":"serviceTask","bindingRef":"pipeline"},{"elementId":"end","elementType":"endEvent"}],
                "sequenceFlows":[{"flowId":"to-service","sourceRef":"start","targetRef":"service"},{"flowId":"to-end","sourceRef":"service","targetRef":"end"}]}
            if args.business_error_boundary:
                process['elements'] += [{'elementId':'error','elementType':'boundaryEvent','attachedToRef':'service','cancelActivity':True,
                    'eventDefinitions':[{'type':'error','properties':{'code':'etl.rows-rejected' if args.business_error_boundary=='matching' else 'etl.other-error'}}]},
                    {'elementId':'error-end','elementType':'endEvent'}]
                process['sequenceFlows'].append({'flowId':'caught-end','sourceRef':'error','targetRef':'error-end'})
            service_binding={'bindingId':'pipeline','executable':worker_pin,'businessErrorCode':'etl.rows-rejected'}
            if args.hold_service_retry or args.final_service_fault:
                service_binding['faultPolicy']={'formatVersion':1,'disposition':'HoldForRetry' if args.hold_service_retry else 'FinalFault','maxAttempts':2 if args.hold_service_retry else 1}
            timer_duration=10 if args.service_timer=='timer-first' else 90 if args.service_timer=='held-interruption' else 60
            if args.service_timer:
                service_binding.setdefault('faultPolicy',{'formatVersion':1,'disposition':'FinalFault','maxAttempts':1})
                process['elements'] += [{'elementId':'timeout','elementType':'boundaryEvent','bindingRef':'timeout-work','attachedToRef':'service','cancelActivity':True,
                    'eventDefinitions':[{'type':'timer','properties':{'interval':'PT'+str(timer_duration)+'S'}}]}, {'elementId':'timeout-end','elementType':'endEvent'}]
                process['sequenceFlows'].append({'flowId':'to-timeout-end','sourceRef':'timeout','targetRef':'timeout-end'})
            model_definition={"formatVersion":4 if args.service_timer else 3 if args.hold_service_retry or args.final_service_fault else 2,"process":process,"humanBindings":[],"serviceBindings":[service_binding]}
            if args.service_timer:model_definition['timerBindings']=['timeout-work']
            model_publication=api.request("/bpmn/artifacts",{"requestId":"publish-model","definition":model_definition})
            if args.embedded_parent:
                outer=node("run-bpmn","Elsa.Activities.Bpmn.Activities.RunBpmn",[q.literal("Model",model_publication["sourceReferenceId"])])
                if args.embedded_parent!='Root':
                    successor=node("after","Elsa.Activities.Primitives.Activities.WriteLine",[q.literal("text","Recovered embedded ETL "+args.embedded_parent)])
                    if args.embedded_parent=='Sequence':
                        composite={"kind":"elsa.sequence.structure","schemaVersion":"1.0.0","payload":{"activities":[outer,successor]}}
                        activity_type='Elsa.Activities.Sequence.Activities.Sequence'
                    elif args.embedded_parent=='Flowchart':
                        composite={"kind":"elsa.flowchart.structure","schemaVersion":"1.0.0","payload":{"startNodeId":"run-bpmn","activities":[outer,successor],"connections":[{"id":"after","source":{"nodeId":"run-bpmn","port":"Done"},"target":{"nodeId":"after"}}]}}
                        activity_type='Elsa.Activities.Flowchart.Activities.Flowchart'
                    else:
                        composite={"kind":"elsa.compensation-scope.structure","schemaVersion":"1.0.0","payload":{"steps":[{"name":"process","activity":outer},{"name":"after","activity":successor}]}}
                        activity_type='Elsa.Activities.Compensation.Activities.CompensationScope'
                    outer=node('native-parent',activity_type,structure=composite)
                outer_definition=api.request('/design/workflows/definitions/submit',{"name":"Actual RunBpmn ETL "+args.embedded_parent+" "+definition,"state":{"variables":[],"inputs":[],"outputs":[],"rootActivity":outer}})
                native_parent_publication=api.request('/publishing/workflows/'+outer_definition['version']['id']+'/publish',{})
                outer_start=api.request('/runtime/workflows/executables/'+native_parent_publication['artifactId']+'/execute',{'sourceReferenceId':native_parent_publication['sourceReferenceId']})
                native_parent_execution=outer_start['workflowExecutionId']
                def embedded_model():
                    parent=api.view(native_parent_execution)
                    if parent['incidents']:raise ValueError('The actual native RunBpmn invocation failed before model admission: '+json.dumps(parent['incidents']))
                    activities=[a for a in parent['activities'] if a['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
                    if not activities:return None
                    detail=api.request('/runtime/workflows/instances/'+native_parent_execution+'/activity-executions/'+activities[0]['activityExecutionId'])
                    if len(detail['bookmarks'])!=1:return None
                    identity=detail['bookmarks'][0]['stimulusHash']
                    try:
                        with api.opener.open(api.base+'/bpmn/executions/'+urllib.parse.quote(identity,safe=''),timeout=30) as response:json.load(response)
                    except urllib.error.HTTPError as error:
                        if error.code==404:return None
                        raise
                    return identity
                model_execution=wait_for(embedded_model,lambda value:value is not None,'The actual native RunBpmn wait did not admit its service model.',90)
            else:
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
                    dispatch=None
                    if args.queued_native_stage:
                        dispatcher=args.dispatcher_assembly.resolve(strict=True);nonce=uuid.uuid4().hex
                        q.write_json(root/"owned-bpmn-recovery.json",{"schemaVersion":1,"controllerProcessId":os.getpid(),"modelExecutionId":model_execution,"nonce":nonce})
                        sdk=start(["dotnet",str(dispatcher),"--enqueue-"+args.queued_native_stage,str(root),model_execution,nonce],"native-sdk")
                        sdk_log=root/f"native-sdk-{len(processes)-1}.log"
                        sdk.wait(timeout=90)
                        dispatch=json.loads(sdk_log.read_text())
                        q.write_json(output/"sdk-dispatch.json",dispatch)
                        if sdk.returncode!=0 or not dispatch.get("modelUnchanged") or dispatch.get("nativeWorkerAdmitted") is not False or any(source not in p["informationalVersion"] for p in dispatch["products"]):
                            raise ValueError("The actual SDK did not retain a source-matched, unadmitted native queue item.")
                    before=admission_view();before_wire=wire();quiesce()
                    before_stores=snapshots("before");before_checked=admission_checked()
                    q.write_json(output/"before.json",before);q.write_json(output/"before-checked.json",before_checked);q.write_json(output/"before-wire.json",before_wire)
                    if (before["model"]["status"]!="Running" or before["native"] or before["runs"]["items"] or
                            before_checked["worker"] is not None or before_checked["model"]["revision"]!=1 or len(before_checked["sources"])!=1 or
                            before_checked["sources"][0]["status"]==2):
                        raise ValueError("The actual native worker was already admitted before the cancellation cut.")
                    if args.queued_native_stage:
                        queued=before_checked["queuedNativeWork"]
                        if len(queued)!=1 or queued!=dispatch["queuedWork"] or queued[0]["commandKind"]!=(0 if args.queued_native_stage=="start" else 11):
                            raise ValueError("The actual native start/checkpoint was not queued before cancellation.")
                    os.kill(host.pid,signal.SIGCONT)
                    if args.admission_race_order:
                        gate=root/"admission-gate"
                        first_op="--drain-checkpoint" if args.admission_race_order=="cancel-first" else "--cancel-model"
                        second_op="--cancel-model" if args.admission_race_order=="cancel-first" else "--drain-checkpoint"
                        first=start(["dotnet",str(dispatcher),first_op,str(root),model_execution,nonce,"--gate",str(gate)],"gated-sdk")
                        first_log=root/f"gated-sdk-{len(processes)-1}.log"
                        def gate_ready():
                            if first.poll() is not None:raise ValueError("The actual first writer exited before its unchanged proposal gate: "+first_log.read_text())
                            return (gate/"ready.json").exists()
                        wait_for(gate_ready,bool,"The actual first writer did not reach its pre-transaction observation hook.",90)
                        observed=json.loads((gate/"ready.json").read_text());q.write_json(output/"race-proposal.json",observed)
                        second=start(["dotnet",str(dispatcher),second_op,str(root),model_execution,nonce],"competing-sdk")
                        second_log=root/f"competing-sdk-{len(processes)-1}.log";second.wait(timeout=90)
                        competing=json.loads(second_log.read_text());q.write_json(output/"race-competitor.json",competing)
                        if second.returncode!=0:raise ValueError("The actual competing SDK writer failed: "+second_log.read_text())
                        competitor_checked=admission_checked();q.write_json(output/"race-competing-checked.json",competitor_checked)
                        release_at=datetime.now(timezone.utc).isoformat();(gate/"release").write_text(release_at)
                        first.wait(timeout=90);gated=json.loads(first_log.read_text());q.write_json(output/"race-gated.json",gated)
                        q.write_json(output/"race-release.json",json.loads((gate/"released.json").read_text()))
                        if first.returncode!=0:raise ValueError("The actual gated SDK writer failed: "+first_log.read_text())
                        if any(source not in p["informationalVersion"] for client in (competing,gated) for p in client["products"]):
                            raise ValueError("A race writer does not match the exact committed source.")
                        cancellation=competing["outcome"] if args.admission_race_order=="cancel-first" else gated["outcome"]
                        pending=admission_view();quiesce();pending_stores=snapshots("pending");pending_checked=admission_checked()
                        q.write_json(output/"cancel-response.json",cancellation);q.write_json(output/"pending.json",pending);q.write_json(output/"pending-checked.json",pending_checked)
                        if (cancellation["pending"] or cancellation["result"]["status"]!="Accepted" or pending["model"]["status"]!="TerminalPendingSettlement" or
                                pending_checked["model"]["revision"]!=2 or pending["runs"]["items"] or
                                len([s for s in pending_checked["sources"] if s["intent"]["kind"] in ("Elsa.Bpmn.StartNativeWork.v1","Elsa.Bpmn.CancelNativeWork.v1")])!=2):
                            raise ValueError("The actual ordered admission/cancellation did not retain its decision and obligations.")
                        admitted=args.admission_race_order=="admit-first"
                        if (pending_checked["worker"] is not None)!=admitted or len(pending["native"])!=int(admitted):
                            raise ValueError("The competing writer order did not determine actual native admission.")
                        stop(host,crash=True);exit_code=host.returncode
                        if exit_code!=-signal.SIGKILL:raise ValueError("The owned race predecessor did not lose its actual process.")
                        print("Actual "+args.admission_race_order+" interleaving retained; owned predecessor killed.",flush=True)
                        features["WorkflowsRuntimeResumption"]={"SweepIntervalSeconds":.2};q.write_json(root/"shells.json",shells)
                        api=launch()
                        finished_checked=wait_for(admission_checked,lambda value:value["model"]["lifecycle"]==3 and
                            len(value["inputs"])==3 and all(item["status"]==2 for item in value["sources"]) and
                            not value["queuedNativeWork"] and not value["queuedModelInputs"],"The actual admission/cancellation race did not settle after restart.",90)
                        finished=admission_view();finished_wire=wire();stop(host);finished_checked=admission_checked();finished_stores=snapshots("finished")
                        q.write_json(output/"finished.json",finished);q.write_json(output/"finished-checked.json",finished_checked);q.write_json(output/"finished-wire.json",finished_wire)
                        api=launch();until=time.monotonic()+2
                        while time.monotonic()<until:
                            if admission_view()!=finished or wire()!=finished_wire:raise ValueError("The admission/cancellation race revived work across another host.")
                            time.sleep(.2)
                        retained=admission_view();retained_wire=wire();stop(host);retained_checked=admission_checked()
                        q.write_json(output/"retained.json",retained);q.write_json(output/"retained-checked.json",retained_checked);q.write_json(output/"retained-wire.json",retained_wire)
                        if retained_checked!=finished_checked:raise ValueError("The actual race settlement changed after another host.")
                        q.write_json(output/"process.json",{"schemaVersion":1,"kind":"actual-bpmn-native-admission-cancellation-race","sourceRevision":source,
                            "raceOrder":args.admission_race_order,"products":products,"processes":processes,"predecessorExitCode":exit_code,
                            "modelPublication":model_publication,"publication":publication,"etlPublication":published,"modelExecutionId":model_execution,
                            "origin":before_checked["origin"],"beforeStores":before_stores,"pendingStores":pending_stores,"finishedStores":finished_stores,
                            "sweepIntervalsSeconds":[300,.2,.2],"controllerSha256":q.digest(Path(__file__)),"fixtureSha256":q.digest(fixture_script),
                            "dispatcherSha256":q.digest(dispatcher),"queuedNativeStage":"checkpoint","releaseRequestedAtUtc":release_at,
                            "scopeLimits":["Actual unchanged native/model checkpoint proposals interleave before the real EF writer transaction; no persisted state or clock is edited.",
                                "Owned SDK uses existing checkpoint-validation and drain-budget seams; three standard Workbench hosts perform publication, process loss and cold recovery.",
                                "Pending-result crash cuts, embedding, held retry, other providers and final upgrade/Studio remain separate gates."]})
                        print("Actual "+args.admission_race_order+" race settled and survived another standard Workbench host.",flush=True)
                        return
                    cancellation=api.request("/bpmn/executions/"+model_execution+"/cancel",
                        {"requestId":"cancel-before-native-admission","reason":"Retire the original native start before admission."})
                    pending=admission_view();quiesce();pending_stores=snapshots("pending");pending_checked=admission_checked()
                    q.write_json(output/"cancel-response.json",cancellation);q.write_json(output/"pending.json",pending);q.write_json(output/"pending-checked.json",pending_checked)
                    if (cancellation["pending"] or cancellation["result"]["status"]!="Accepted" or pending["model"]["status"]!="TerminalPendingSettlement" or
                            pending_checked["model"]["revision"]!=2 or pending_checked["worker"] is not None or pending["native"] or pending["runs"]["items"] or
                            len(pending_checked["sources"])!=2 or any(item["status"]==2 for item in pending_checked["sources"])):
                        raise ValueError("The actual pending cancellation/start responsibility was not retained before process loss.")
                    if args.queued_native_stage and pending_checked["queuedNativeWork"]!=before_checked["queuedNativeWork"]:
                        raise ValueError("The actual queued native work advanced before the pending-cancellation crash cut.")
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
                    q.write_json(output/"process.json",{"schemaVersion":1,"kind":"actual-bpmn-queued-native-start-cancellation" if args.queued_native_stage else "actual-bpmn-cancellation-before-native-admission","sourceRevision":source,
                        "products":products,"processes":processes,"predecessorExitCode":exit_code,"modelPublication":model_publication,"publication":publication,
                        "etlPublication":published,"modelExecutionId":model_execution,"origin":before_checked["origin"],"beforeStores":before_stores,
                        "pendingStores":pending_stores,"finishedStores":finished_stores,"sweepIntervalsSeconds":[300,.2,.2],
                        "controllerSha256":q.digest(Path(__file__)),"fixtureSha256":q.digest(fixture_script),
                        **({"queuedNativeStage":args.queued_native_stage,"dispatcherSha256":q.digest(dispatcher)} if args.queued_native_stage else {}),
                        "scopeLimits":["Actual SDK queues the original pinned native start/checkpoint through the existing drain-policy seam; standard Workbench hosts recover it after SIGKILL." if args.queued_native_stage else "Original native start source owed before first delivery; positive model retirement across actual SIGKILL and fresh standard hosts.",
                            "The real ETL worker is published and pinned, but no native workflow, ETL run or HTTP effect is admitted.",
                            "Simultaneous native admission/cancellation, pending result crashes, embedding, held retry and final upgrade/Studio remain separate gates." if args.queued_native_stage else "Queued native-start checkpoint/admission races, pending result crashes, embedding, held retry and final upgrade/Studio remain separate gates."]})
                    print("Original native start retired after process loss; no worker or ETL run across a third host.",flush=True)
                except Exception as failure:
                    for child in children:
                        if child is not host and child is not fixture:stop(child)
                    stop(host);failed_stores=snapshots("failed")
                    q.write_json(output/"failure.json",{"sourceRevision":source,"failureType":type(failure).__name__,"failedStores":failed_stores,"processes":processes,"products":products})
                    raise
                return
            instances=wait_for(lambda:api.request("/runtime/workflows/instances"),lambda value:len(value)==(2 if native_parent_execution else 1) and
                len([v for v in value if v['artifactId']==publication['artifactId'] and v['workflowExecutionId']!=native_parent_execution])==1,"The BPMN binding did not admit its real native worker.")
            execution=next(v['workflowExecutionId'] for v in instances if v['artifactId']==publication['artifactId'] and v['workflowExecutionId']!=native_parent_execution)
        else:
            executed=api.request("/runtime/workflows/executables/"+publication["artifactId"]+"/execute",{"sourceReferenceId":publication["sourceReferenceId"]})
            execution=executed["workflowExecutionId"]
        summaries=wait_for(lambda:api.request("/etl/runs?limit=100"),lambda value:len(value["items"])==1,"The real RunEtl activity did not admit its run.")
        run=summaries["items"][0]["runId"];escaped=urllib.parse.quote(run,safe="")
        def view():
            value={"native":api.view(execution),"run":api.request("/etl/runs/"+escaped),"partitions":api.request("/etl/runs/"+escaped+"/connector-partitions?limit=50")}
            if direct_bpmn:value["model"]=api.request("/bpmn/executions/"+model_execution)
            if native_parent_execution:value['embedding']=api.view(native_parent_execution)
            return value
        def checked_native_model():
            return json.loads(subprocess.check_output(["dotnet",str(reader),"--read-bpmn-native",str(root/"elsa.db"),model_execution,execution],text=True,timeout=30))
        def checked_timers():
            return json.loads(subprocess.check_output(['dotnet',str(reader),'--read-bpmn-direct',str(root/'elsa.db'),model_execution],text=True,timeout=30))
        def checked_embedding():
            return json.loads(subprocess.check_output(['dotnet',str(reader),'--read-bpmn-embedding',str(root/'elsa.db'),model_execution,native_parent_execution],text=True,timeout=30))
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
        if args.service_timer:q.write_json(output/'before-checked-timers.json',checked_timers())
        if native_parent_execution:q.write_json(output/'before-checked-embedding.json',checked_embedding())
        stop(host,crash=True);exit_code=host.returncode
        if exit_code!=-signal.SIGKILL:raise ValueError("The predecessor did not exit through actual process loss.")
        print("Observed durable 429 and completed sibling; owned predecessor terminated.",flush=True)
        snapshot_unavailability=None
        if args.technical_fault:
            lookup_snapshot,=list((root/'etl-snapshots').glob('*.csv'))
            if lookup_snapshot.is_symlink() or q.digest(lookup_snapshot)!=q.digest(root/'lookup.csv'):
                raise ValueError('The actual prepared lookup snapshot does not match its immutable source.')
            shutil.copyfile(lookup_snapshot,output/'lookup-snapshot.csv')
            unavailable=lookup_snapshot.with_name(lookup_snapshot.name+'.unavailable')
            lookup_snapshot.rename(unavailable)
            snapshot_unavailability={'schemaVersion':1,'kind':'actual-owned-lookup-snapshot-unavailability','sourceRevision':source,
                'snapshotKey':lookup_snapshot.name,'unavailableKey':unavailable.name,'sourceSha256':q.digest(root/'lookup.csv'),
                'snapshotSha256Before':q.digest(output/'lookup-snapshot.csv'),'snapshotSha256WhileUnavailable':q.digest(unavailable),
                'sourceStillAvailable':(root/'lookup.csv').is_file(),'originalSnapshotReadPathMissing':not lookup_snapshot.exists(),
                'inducedAtUtc':datetime.now(timezone.utc).isoformat(),'predecessorExitCode':exit_code}
        pending_cancellation_evidence=None
        if args.pending_cancellation_crash or args.pending_parent_result_crash or args.successful_parent_result_crash:
            features["WorkflowsRuntimeResumption"]={"SweepIntervalSeconds":300};q.write_json(root/"shells.json",shells)
        api=launch();recovered=view();q.write_json(output/"recovered.json",recovered)
        if args.service_timer:
            route='/bpmn/executions/'+urllib.parse.quote(model_execution,safe='')
            first_timer,=[w['timer'] for w in json.loads((output/'before-checked-timers.json').read_text())['model']['payload']['work'] if w.get('timer')]
            def timed_view():return {'execution':view(),'incidents':api.request(route+'/incidents?limit=10')}
            expected_worker='Faulted' if args.service_timer=='held-interruption' else 'Cancelled' if args.service_timer=='timer-first' else 'Completed'
            expected_etl='faulted' if args.service_timer=='held-interruption' else 'cancelled' if args.service_timer=='timer-first' else 'succeeded'
            if args.service_timer=='held-interruption':
                middle=wait_for(timed_view,lambda v:v['execution']['model']['status']=='WaitingIncident' and v['execution']['native']['instance']['status']=='Faulted' and
                    v['execution']['run']['status']=='faulted' and len(v['incidents']['items'])==1,'The actual timed worker did not retain its held failure.',90)
                middle_kind='held'
            else:
                middle=wait_for(timed_view,lambda v:v['execution']['model']['status']=='TerminalSettled' and v['execution']['native']['instance']['status']==expected_worker and
                    v['execution']['run']['status']==expected_etl,'The real worker/timer order did not reach its original terminal result.',120)
                middle_kind='winner'
            checked_middle=wait_for(checked_native_model,lambda v:v['resultInput'] is not None and v['resultSource'] is not None and v['resultSource']['status']==2,
                'The actual timed worker result still owes its exact consumption/ACK.',90)
            if args.service_timer!='timer-first' and datetime.now(timezone.utc)>=utc_instant(first_timer['dueAt']):
                raise ValueError('The intended worker/held cut did not precede its real timer deadline.')
            os.kill(host.pid,signal.SIGSTOP)
            wait_for(lambda:q.process_field(host.pid,'stat'),lambda v:'T' in v,'The timed worker host did not quiesce.',5)
            middle_cut=datetime.now(timezone.utc).isoformat();middle_stores=snapshots('middle')
            q.write_json(output/'middle.json',middle);q.write_json(output/'middle-checked-model.json',checked_native_model())
            q.write_json(output/'middle-checked-timers.json',checked_timers());q.write_json(output/'middle-wire.json',wire())
            if native_parent_execution:q.write_json(output/'middle-checked-embedding.json',checked_embedding())
            stop(host,crash=True);middle_exit=host.returncode
            if middle_exit!=-signal.SIGKILL:raise ValueError('The retained timed worker decision did not lose its actual process.')
            if args.service_timer=='held-interruption':
                if lookup_snapshot.exists() or q.digest(unavailable)!=snapshot_unavailability['snapshotSha256Before']:
                    raise ValueError('The timed held failure lost its original immutable lookup snapshot.')
                unavailable.rename(lookup_snapshot)
                snapshot_unavailability.update(restoredBeforeTimerInterruption=True,restoredAtUtc=datetime.now(timezone.utc).isoformat(),restoredSha256=q.digest(lookup_snapshot))
                q.write_json(output/'snapshot-unavailability.json',snapshot_unavailability)
            api=launch();after_restart=timed_view();q.write_json(output/'middle-after-restart.json',after_restart)
            if after_restart!=middle:raise ValueError('The timed worker decision or held failure changed at process recovery.')
            final=wait_for(timed_view,lambda v:v['execution']['model']['status']=='TerminalSettled' and v['execution']['model']['result']['status']=='Completed' and
                v['execution']['native']['instance']['status']==expected_worker and v['execution']['run']['status']==expected_etl and
                (not native_parent_execution or v['execution']['embedding']['instance']['status']=='Completed'),
                'The actual timer did not settle the original model/native parent.',120)
            timer_checked=wait_for(checked_timers,lambda v:all(s['status']==2 for s in v['sources']) and not v['queuedModelInputs'],
                'The original duration source still owes its actual model receipt or ACK.',90)
            timer_receipt,=[r for r in timer_checked['inputs'] if r['inputKind']=='timer-elapsed']
            if timer_receipt['result']['status']!=('Rejected' if args.service_timer=='worker-first' else 'Accepted'):
                raise ValueError('The original worker/timer winner was not retained in its exact elapsed input receipt.')
            terminal=timer_checked['model']['payload'];expected_end='end' if args.service_timer=='worker-first' else 'timeout-end'
            if not any(d['elementId']==expected_end for d in terminal['state']['diagnostics']) or any(d['elementId']==('timeout-end' if expected_end=='end' else 'end') for d in terminal['state']['diagnostics']):
                raise ValueError('The real worker/timer journey followed the wrong semantic route.')
            if args.service_timer=='held-interruption':
                failure,=terminal['failures'];incident,=final['incidents']['items']
                if failure['status']!='Retired' or incident['status']!='Retired' or failure['resolutionInputId']!=timer_receipt['inputId'] or len(terminal['nativeResults'])!=1:
                    raise ValueError('Timer interruption revived or lost the original held failure evidence.')
                request={'requestId':'retry-after-actual-held-timeout','incidentId':incident['incidentId'],'invocationId':incident['invocationId'],'failedAttemptId':incident['failedAttemptId']}
                call=urllib.request.Request(api.base+route+'/retry-service',data=json.dumps(request).encode(),headers={'Content-Type':'application/json'})
                try:
                    with api.opener.open(call,timeout=30):raise ValueError('A timer-retired held attempt reopened through retry.')
                except urllib.error.HTTPError as error:
                    if error.code!=409:raise
                    refused=json.loads(error.read())
                q.write_json(output/'retry-refusal.json',{'request':request,'httpStatus':409,'outcome':refused})
            # Real timer cancellation must suppress a delayed HTTP retry after its original deadline.
            while datetime.now(timezone.utc)<utc_instant(retry['notBefore'])+timedelta(seconds=2):
                if timed_view()!=final:raise ValueError('The timer winner changed while the original HTTP retry became due.')
                time.sleep(.25)
            final_wire=wire();checked_final=checked_native_model();timer_checked=checked_timers()
            if args.service_timer!='worker-first' and final_wire!=before_wire:raise ValueError('The interrupted/held worker performed late HTTP work.')
            q.write_json(output/'finished.json',final);q.write_json(output/'finished-wire.json',final_wire)
            q.write_json(output/'finished-checked-model.json',checked_final);q.write_json(output/'finished-checked-timers.json',timer_checked)
            if native_parent_execution:q.write_json(output/'finished-checked-embedding.json',checked_embedding())
            stop(host);finished_stores=snapshots('finished');api=launch()
            retained=timed_view();retained_checked=checked_native_model();retained_timers=checked_timers();retained_wire=wire()
            q.write_json(output/'retained.json',retained);q.write_json(output/'retained-checked-model.json',retained_checked)
            q.write_json(output/'retained-checked-timers.json',retained_timers);q.write_json(output/'retained-wire.json',retained_wire)
            if native_parent_execution:q.write_json(output/'retained-checked-embedding.json',checked_embedding())
            stop(host)
            if retained!=final or retained_checked!=checked_final or retained_timers!=timer_checked or retained_wire!=final_wire:
                raise ValueError('The actual worker/timer result changed in a fourth standard host.')
            q.write_json(output/'process.json',{'schemaVersion':1,'kind':'actual-bpmn-etl-duration-race','sourceRevision':source,'products':products,
                'order':args.service_timer,'durationSeconds':timer_duration,'timer':first_timer,'parent':args.parent,'embeddedParent':args.embedded_parent,
                'modelExecutionId':model_execution,'workflowExecutionId':execution,'runId':run,'nativeParentExecutionId':native_parent_execution,
                'publication':publication,'modelPublication':model_publication,'nativeParentPublication':native_parent_publication,'etlPublication':published,
                'processes':processes,'predecessorExitCode':exit_code,'middleExitCode':middle_exit,'cutAtUtc':cut_at,'middleCutAtUtc':middle_cut,'middleKind':middle_kind,
                'beforeStores':before_stores,'middleStores':middle_stores,'finishedStores':finished_stores,'selection':selection,'bindings':bindings,
                'fixtureSha256':q.digest(fixture_script),'controllerSha256':q.digest(Path(__file__)),'retryAfterSeconds':retry_after,'resumptionSweepIntervalSeconds':.2,
                'scopeLimits':['Actual ETL/native worker against its original duration boundary after process loss. No SQL state or clock edits.',
                    'Simultaneous commit orders, direct-model broadcast/provider/retention/rollback and final upgrade/Studio remain separate.']})
            print('Actual '+args.service_timer+' preserved its original worker and timer through process loss and a fourth host.',flush=True)
            return
        if args.hold_service_retry:
            route='/bpmn/executions/'+urllib.parse.quote(model_execution,safe='')
            held=wait_for(lambda:{'execution':view(),'incidents':api.request(route+'/incidents?limit=10')},
                lambda value:value['execution']['model']['status']=='WaitingIncident' and value['execution']['run']['status']=='faulted' and
                    value['execution']['native']['instance']['status']=='Faulted' and len(value['incidents']['items'])==1,
                'The actual worker failure did not remain a held, nonterminal BPMN incident.',90)
            checked_held=wait_for(checked_native_model,lambda value:value['resultInput'] is not None and value['resultSource'] is not None and value['resultSource']['status']==2,
                'The actual held failure did not retain consumption and original source acknowledgment.',90)
            incident,=held['incidents']['items'];held_document=checked_held['model']['payload'];old_result,=held_document['nativeResults'];failure,=held_document['failures']
            if (incident['status']!='Open' or incident['attemptNumber']!=1 or incident['maxAttempts']!=2 or incident['faultCode']!='etl.snapshot-read-failed' or
                    incident['workerExecutionId']!=execution or held['execution']['model']['result'] is not None or
                    held_document['state']!=json.loads((output/'before-checked-model.json').read_text())['model']['payload']['state'] or
                    failure['semanticState']!=held_document['state'] or failure['status']!='Open' or wire()!=before_wire or
                    native_parent_execution and held['execution']['embedding']['instance']['status']!='Running'):
                raise ValueError('The actual held failure changed token semantics, worker truth, parent wait or prior HTTP effects.')
            os.kill(host.pid,signal.SIGSTOP)
            wait_for(lambda:q.process_field(host.pid,'stat'),lambda value:'T' in value,'The second owned host did not quiesce.',5)
            held_stores=snapshots('held');held_cut=datetime.now(timezone.utc).isoformat()
            q.write_json(output/'held.json',held);q.write_json(output/'held-checked-model.json',checked_native_model());q.write_json(output/'held-wire.json',wire())
            if native_parent_execution:q.write_json(output/'held-checked-embedding.json',checked_embedding())
            stop(host,crash=True);held_exit=host.returncode
            if held_exit!=-signal.SIGKILL:raise ValueError('The actual held incident did not survive a second process loss.')
            if args.retry_auth_controls:
                dispatcher=args.dispatcher_assembly.resolve(strict=True);nonce=uuid.uuid4().hex
                q.write_json(root/'owned-bpmn-recovery.json',{'schemaVersion':1,'controllerProcessId':os.getpid(),'modelExecutionId':model_execution,'nonce':nonce})
                auth_accounts=[{'profile':name,'userName':'bpmn-probe-'+name,'tenantId':'bpmn-probe-foreign' if name=='foreign' else 'default',
                    'password':'Probe-'+uuid.uuid4().hex+'a1!','permissions':permissions}
                    for name,permissions in [('reader',['bpmn:read']),('retrier',['bpmn:retry-service']),('nobody',[]),('foreign',['bpmn:read','bpmn:retry-service'])]]
                private=root/'owned-auth-probe-request.json';q.write_json(private,{'nonce':nonce,'users':auth_accounts});private.chmod(0o600)
                sdk=start(['dotnet',str(dispatcher),'--create-auth-probe-users',str(root),model_execution,nonce],'auth-sdk')
                sdk.wait(timeout=60);private.unlink()
                if sdk.returncode!=0:raise ValueError('The production Identity SDK could not create its bounded owned test accounts; see private diagnostics.')
                setup=json.loads((root/f'auth-sdk-{len(processes)-1}.log').read_text().splitlines()[-1])
                if setup['credentialsIncluded'] or setup['executionStateSeeded'] or any(not item['informationalVersion'].endswith('+'+source) for item in setup['products']):
                    raise ValueError('The Identity setup is not source-matched normal production-service work.')
                q.write_json(output/'auth-probe-users.json',setup)
            if lookup_snapshot.exists() or q.digest(unavailable)!=snapshot_unavailability['snapshotSha256Before']:
                raise ValueError('The retained lookup changed while its failed attempt was held.')
            unavailable.rename(lookup_snapshot)
            snapshot_unavailability.update(**{'restoredBeforeAuthorizedCancellation' if args.held_action=='cancel' else 'restoredBeforeAuthorizedRetry':True},
                restoredAtUtc=datetime.now(timezone.utc).isoformat(),restoredSha256=q.digest(lookup_snapshot))
            q.write_json(output/'snapshot-unavailability.json',snapshot_unavailability)
            if args.held_race_order:
                dispatcher=args.dispatcher_assembly.resolve(strict=True);nonce=uuid.uuid4().hex;gate=root/'held-command-gate'
                q.write_json(root/'owned-bpmn-recovery.json',{'schemaVersion':1,'controllerProcessId':os.getpid(),'modelExecutionId':model_execution,'nonce':nonce})
                first_op='--retry-held-model' if args.held_race_order=='retry-first' else '--cancel-held-model'
                second_op='--cancel-held-model' if args.held_race_order=='retry-first' else '--retry-held-model'
                def full_checked():
                    return json.loads(subprocess.check_output(['dotnet',str(reader),'--read-bpmn-direct',str(root/'elsa.db'),model_execution],text=True,timeout=30))
                first=start(['dotnet',str(dispatcher),first_op,str(root),model_execution,nonce,'--gate',str(gate)],'gated-held-sdk')
                first_log=root/f'gated-held-sdk-{len(processes)-1}.log'
                def reached():
                    if first.poll() is not None:raise ValueError('The actual held writer left before its proposal gate: '+first_log.read_text())
                    return (gate/'ready.json').is_file()
                wait_for(reached,bool,'The first held command did not reach its unchanged proposal gate.',90)
                proposal=json.loads((gate/'ready.json').read_text());q.write_json(output/'race-proposal.json',proposal)
                second=start(['dotnet',str(dispatcher),second_op,str(root),model_execution,nonce],'competing-held-sdk')
                second_log=root/f'competing-held-sdk-{len(processes)-1}.log';second.wait(timeout=60)
                if second.returncode!=0:raise ValueError('The actual competing held writer failed: '+second_log.read_text())
                competing=json.loads(second_log.read_text());q.write_json(output/'race-competitor.json',competing)
                competing_checked=full_checked();q.write_json(output/'race-competing-checked.json',competing_checked)
                (gate/'release').write_text(datetime.now(timezone.utc).isoformat());first.wait(timeout=60)
                if first.returncode!=0:raise ValueError('The stale actual held writer failed outside retained admission: '+first_log.read_text())
                gated=json.loads(first_log.read_text());q.write_json(output/'race-gated.json',gated)
                release=json.loads((gate/'released.json').read_text());q.write_json(output/'race-release.json',release)
                raced=full_checked();q.write_json(output/'raced-checked-model.json',raced)
                for receipt,operation in ((competing,second_op),(gated,first_op)):
                    if receipt['operation']!=operation or not receipt['outcome']['pending'] or receipt['outcome']['result'] is not None or receipt['before']!=checked_held['model'] or receipt['model']!=checked_held['model'] or any(not item['informationalVersion'].endswith('+'+source) for item in receipt['products']):
                        raise ValueError('A concurrent SDK either advanced the gated failure or lost its source-matched durable pending command.')
                expected_ids=['retry-competing-held-service','cancel-competing-held-service'] if args.held_race_order=='retry-first' else ['cancel-competing-held-service','retry-competing-held-service']
                if raced['model']!=checked_held['model'] or competing_checked['model']!=raced['model'] or [x['commandId'] for x in raced['queuedModelInputs']]!=expected_ids or not release['proposalUnchanged'] or release['proposalFingerprint']!=proposal['proposalFingerprint'] or wire()!=before_wire:
                    raise ValueError('The concurrent held commands did not retain unchanged revision-two state and their actual FIFO order.')
                raced_stores=snapshots('raced')
                print('Actual '+args.held_race_order+' concurrent submissions retained after stale proposal refusal; cold recovery follows.',flush=True)
                api=launch()
                def race_view():
                    return {'execution':view(),'incidents':api.request(route+'/incidents?limit=10'),
                        'retry':api.request(route+'/commands/retry-competing-held-service'),'cancel':api.request(route+'/commands/cancel-competing-held-service')}
                final=wait_for(race_view,lambda value:value['execution']['model']['status']=='TerminalSettled' and
                    value['execution']['model']['result']['status']=='Cancelled' and not value['retry']['pending'] and not value['cancel']['pending'] and
                    (not native_parent_execution or value['execution']['embedding']['instance']['status']=='Cancelled'),
                    'Cold recovery did not consume both competing held commands and settle cancellation.',120)
                full_final=wait_for(full_checked,lambda value:not value['queuedModelInputs'] and all(s['status']==2 for s in value['sources']),
                    'The concurrent held commands left exact sources or inputs owed.',90)
                checked_final=checked_native_model()
                retirement=json.loads(subprocess.check_output(['dotnet',str(reader),'--read-bpmn-retirement',str(root/'elsa.db'),model_execution],text=True,timeout=30))
                retry_result=final['retry']['result'];cancel_result=final['cancel']['result'];fm=full_final['model'];resolved,=fm['payload']['failures']
                if cancel_result['status']!='Accepted' or cancel_result['revision']!=(4 if args.held_race_order=='retry-first' else 3) or (
                    retry_result['status']!=('Accepted' if args.held_race_order=='retry-first' else 'Rejected')) or retry_result['revision']!=3 or (
                    args.held_race_order=='cancel-first' and retry_result['code']!='execution-terminal') or (
                    final['execution']['native']!=held['execution']['native'] or final['execution']['run']!=held['execution']['run'] or wire()!=before_wire or len(api.request('/etl/runs?limit=100')['items'])!=1 or
                    fm['payload']['state']!=failure['semanticState'] or fm['payload']['nativeResults']!=[old_result] or
                    resolved['status']!=('Retried' if args.held_race_order=='retry-first' else 'Retired')):
                    raise ValueError('Competing retry/cancel rewrote the old fault, admitted duplicate work or lost exact FIFO command outcomes.')
                q.write_json(output/'finished.json',final);q.write_json(output/'finished-checked-model.json',checked_final)
                q.write_json(output/'finished-checked-inputs.json',full_final);q.write_json(output/'finished-checked-retirement.json',retirement);q.write_json(output/'finished-wire.json',wire())
                if native_parent_execution:q.write_json(output/'finished-checked-embedding.json',checked_embedding())
                stop(host);finished_stores=snapshots('finished');api=launch()
                retained=race_view();retained_checked=checked_native_model();retained_full=full_checked();retained_wire=wire()
                q.write_json(output/'retained.json',retained);q.write_json(output/'retained-checked-model.json',retained_checked)
                q.write_json(output/'retained-checked-inputs.json',retained_full);q.write_json(output/'retained-wire.json',retained_wire)
                if native_parent_execution:q.write_json(output/'retained-checked-embedding.json',checked_embedding())
                stop(host)
                if retained!=final or retained_checked!=checked_final or retained_full!=full_final or retained_wire!=before_wire:
                    raise ValueError('Competing held decisions or physical fault truth changed in another standard process.')
                q.write_json(output/'process.json',{'schemaVersion':1,'kind':'actual-held-retry-cancel-submission-race','sourceRevision':source,'products':products,
                    'order':args.held_race_order,'parent':args.parent,'embeddedParent':args.embedded_parent,'modelExecutionId':model_execution,'workflowExecutionId':execution,'runId':run,
                    'processes':processes,'predecessorExitCode':exit_code,'heldPredecessorExitCode':held_exit,'cutAtUtc':cut_at,'heldCutAtUtc':held_cut,
                    'beforeStores':before_stores,'heldStores':held_stores,'racedStores':raced_stores,'finishedStores':finished_stores,'publication':publication,'modelPublication':model_publication,
                    'etlPublication':published,'nativeParentExecutionId':native_parent_execution,'nativeParentPublication':native_parent_publication,'selection':selection,'bindings':bindings,
                    'fixtureSha256':q.digest(fixture_script),'controllerSha256':q.digest(Path(__file__)),'technicalFaultPolicy':'HoldForRetry','maxServiceAttempts':2,
                    'retryAfterSeconds':retry_after,'resumptionSweepIntervalSeconds':.2,'scopeLimits':['Actual same-model concurrent durable submissions, unchanged gated proposal and successor ownership; protected FIFO prevents a second proposal before first-input recovery.',
                    'Both semantic commit orders are checked through exact receipts after cold recovery. Provider/rollback/direct-model broadcast/final upgrade and Studio acceptance remain separate.']})
                print('Actual '+args.held_race_order+' concurrent held commands settled with original fault and prior effects unchanged.',flush=True)
                return
            api=launch()
            after_restart={'execution':view(),'incidents':api.request(route+'/incidents?limit=10')}
            if after_restart!=held or checked_native_model()!=checked_held or wire()!=before_wire:
                raise ValueError('Restoring the external snapshot or restarting revived the held interpreter or failed worker.')
            while datetime.now(timezone.utc)<utc_instant(retry['notBefore'])+timedelta(seconds=2):
                if view()!=held['execution'] or wire()!=before_wire:raise ValueError('The held failed attempt revived at its original HTTP retry deadline.')
                time.sleep(.25)
            q.write_json(output/'held-after-restart.json',after_restart)
            if args.held_action=='cancel':
                cancel_request={'requestId':'cancel-actual-held-service','reason':'Retire the actual held service failure.'}
                submitted=api.request(route+'/cancel',cancel_request)
                accepted=wait_for(lambda:api.request(route+'/commands/'+cancel_request['requestId']),lambda value:not value['pending'],
                    'The actual held cancellation did not retain its exact command result.',90)
                if (accepted['result']['status']!='Accepted' or accepted['result']['revision']!=3 or
                        accepted['result']['lifecycle']!=('TerminalPendingSettlement' if native_parent_execution else 'TerminalSettled')):
                    raise ValueError('Held cancellation did not accept one terminal semantic decision.')
                final=wait_for(view,lambda value:value['model']['status']=='TerminalSettled' and value['model']['result']['status']=='Cancelled' and
                    (not native_parent_execution or value['embedding']['instance']['status']=='Cancelled'),
                    'The cancelled held model did not settle its actual native parent.',90)
                current_incidents=api.request(route+'/incidents?limit=10')
                retired,=current_incidents['items']
                refused={'requestId':'retry-actual-retired-service','incidentId':incident['incidentId'],'invocationId':incident['invocationId'],'failedAttemptId':incident['failedAttemptId']}
                request=urllib.request.Request(api.base+route+'/retry-service',data=json.dumps(refused).encode(),headers={'Content-Type':'application/json'})
                try:
                    with api.opener.open(request,timeout=30):raise ValueError('A retired held service was allowed to retry.')
                except urllib.error.HTTPError as error:
                    if error.code!=409:raise
                refusal=api.request(route+'/commands/'+refused['requestId'])
                if refusal['pending'] or refusal['result']['status']!='Rejected' or refusal['result']['code']!='execution-terminal':
                    raise ValueError('Retired held service lost its durable terminal retry refusal.')
                checked_final=checked_native_model();document=checked_final['model']['payload'];record,=document['failures']
                if (checked_final['model']['revision']!=(4 if native_parent_execution else 3) or document['work'] or
                        checked_final['model']['terminalResult']!={'status':'Cancelled','reason':cancel_request['reason']} or
                        document['state']!=failure['semanticState'] or document['nativeResults']!=[old_result] or
                        record!={**failure,'status':'Retired','resolutionInputId':cancel_request['requestId']} or retired!={**incident,'status':'Retired'} or
                        final['run']!=held['execution']['run'] or final['native']!=held['execution']['native'] or wire()!=before_wire or
                        len(api.request('/etl/runs?limit=100')['items'])!=1 or checked_final['resultInput']!=checked_held['resultInput'] or
                        document.get('cancellations') is not None):
                    raise ValueError('Held cancellation rewrote the failed attempt, restarted work or owed cancellation to an already terminal worker.')
                if native_parent_execution:
                    outer=wait_for(checked_embedding,lambda value:not value['queuedModelInputs'] and all(s['status']==2 for s in value['sources']),
                        'The held model cancellation left native parent settlement or source ACK owed.',90)
                    run_bpmn,=[a for a in final['embedding']['activities'] if a['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
                    if run_bpmn['status']!='Cancelled' or final['embedding']['incidents'] or any((a.get('authoredActivityId') or a['executableNodeId'])=='after' for a in final['embedding']['activities']):
                        raise ValueError('The native parent advanced or faulted after the held model was cancelled.')
                    q.write_json(output/'finished-checked-embedding.json',outer)
                q.write_json(output/'held-cancel-command.json',{'request':cancel_request,'submitted':submitted,'accepted':accepted,'retiredIncident':retired})
                q.write_json(output/'retired-retry-refusal.json',{'request':refused,'httpStatus':409,'command':refusal})
                q.write_json(output/'finished.json',final);q.write_json(output/'finished-checked-model.json',checked_final);q.write_json(output/'finished-wire.json',wire())
                stop(host);finished_stores=snapshots('finished');api=launch()
                retained=view();retained_checked=checked_native_model();retained_incidents=api.request(route+'/incidents?limit=10');retained_wire=wire()
                q.write_json(output/'retained.json',retained);q.write_json(output/'retained-checked-model.json',retained_checked);q.write_json(output/'retained-wire.json',retained_wire)
                q.write_json(output/'retained-incidents.json',retained_incidents)
                if native_parent_execution:q.write_json(output/'retained-checked-embedding.json',checked_embedding())
                stop(host)
                if retained!=final or retained_checked!=checked_final or retained_incidents!=current_incidents or retained_wire!=before_wire:
                    raise ValueError('The cancelled held failure or its original worker revived in another standard process.')
                q.write_json(output/'process.json',{'schemaVersion':1,'kind':'actual-model-held-service-cancellation','sourceRevision':source,'products':products,'parent':args.parent,'embeddedParent':args.embedded_parent,
                    'modelExecutionId':model_execution,'workflowExecutionId':execution,'runId':run,'processes':processes,'predecessorExitCode':exit_code,'heldPredecessorExitCode':held_exit,
                    'cutAtUtc':cut_at,'heldCutAtUtc':held_cut,'beforeStores':before_stores,'heldStores':held_stores,'finishedStores':finished_stores,
                    'publication':publication,'modelPublication':model_publication,'etlPublication':published,'nativeParentExecutionId':native_parent_execution,'nativeParentPublication':native_parent_publication,
                    'selection':selection,'bindings':bindings,'fixtureSha256':q.digest(fixture_script),'controllerSha256':q.digest(Path(__file__)),'technicalFaultPolicy':'HoldForRetry','maxServiceAttempts':2,
                    'retryAfterSeconds':retry_after,'resumptionSweepIntervalSeconds':.2,'scopeLimits':['Actual cancellation of a held technical fault after two process losses; original failed ETL/native truth and prior two effects remain unchanged.',
                        'Remaining timer/concurrent-input/obsolete-result/auth/provider/retention/rollback/final upgrade/Studio acceptance remains separate.']})
                print('Actual held failure survived second SIGKILL; model cancellation retired its semantic work and refused retry without another worker or effect.',flush=True)
                return
            if args.held_action=='exhaust':
                retry_input_unavailable=root/'lookup.csv.unavailable'
                if q.digest(root/'lookup.csv')!=snapshot_unavailability['sourceSha256'] or retry_input_unavailable.exists():
                    raise ValueError('The original bound lookup input is not the unchanged owned source.')
                (root/'lookup.csv').rename(retry_input_unavailable)
                input_unavailability={'schemaVersion':1,'kind':'actual-owned-bound-lookup-input-unavailability','sourceRevision':source,
                    'sourceFile':'lookup.csv','unavailableFile':retry_input_unavailable.name,'sourceSha256Before':q.digest(retry_input_unavailable),
                    'sourceSha256WhileUnavailable':q.digest(retry_input_unavailable),'originalBoundReadPathMissing':not (root/'lookup.csv').exists(),
                    'originalRetainedSnapshotStillAvailable':lookup_snapshot.is_file(),'inducedAtUtc':datetime.now(timezone.utc).isoformat()}
                q.write_json(output/'retry-input-unavailability.json',input_unavailability)
            request={'requestId':'retry-actual-held-service','incidentId':incident['incidentId'],'invocationId':incident['invocationId'],'failedAttemptId':incident['failedAttemptId']}
            if args.retry_auth_controls:
                expected={'inputId':old_result['origin']['resultInputId'],'result':checked_held['resultInput']['result']}
                auth_controls('before',route,request,expected)
                if checked_native_model()!=checked_held or wire()!=before_wire:
                    raise ValueError('A denied principal changed the held execution before the authorized retry.')
            if args.obsolete_result_controls:
                fixture_api=q.Api('http://127.0.0.1:'+str(json.loads(ready.read_text())['port']))
                q.write_json(output/'retry-rearm.json',fixture_api.request('/__fixture/rearm-source-retry',{}))
            submitted=api.request(route+'/retry-service',request)
            accepted=wait_for(lambda:api.request(route+'/commands/'+request['requestId']),lambda value:not value['pending'],
                'The authorized retry did not retain its exact command result.',90)
            if accepted['result']['status']!='Accepted' or accepted['result']['revision']!=3 or accepted['result']['lifecycle']!='Running':
                raise ValueError('The actual retry did not commit one physical attempt for its retained semantic work.')
            duplicate=api.request(route+'/retry-service',request)
            if not duplicate['replay'] or duplicate['pending'] or duplicate['result']!=accepted['result']:
                raise ValueError('Identical authorized retry did not replay the original attempt receipt.')
            conflict=urllib.request.Request(api.base+route+'/retry-service',data=json.dumps({**request,'failedAttemptId':'conflicting-attempt'}).encode(),headers={'Content-Type':'application/json'})
            try:
                with api.opener.open(conflict,timeout=30):raise ValueError('Conflicting retry content was accepted.')
            except urllib.error.HTTPError as error:
                if error.code!=409:raise
            q.write_json(output/'retry-command.json',{'request':request,'submitted':submitted,'accepted':accepted,'duplicate':duplicate,'conflictingContentHttpStatus':409})
            if args.retry_auth_controls:auth_controls('after',route,request,accepted)
            def retried_run():
                runs=api.request('/etl/runs?limit=100')['items']
                if len(runs)!=2:return None
                item,=[item for item in runs if item['runId']!=run]
                model=checked_native_model()['model'];origins=[w['nativeWork'] for w in model['payload']['work']]+[r['origin'] for r in model['payload']['nativeResults']]
                origin=next((o for o in origins if o['workerExecutionId']!=execution),None)
                if origin is None:return None
                return {'runId':item['runId'],'origin':origin}
            replacement=wait_for(retried_run,bool,'The authorized retry did not admit its one actual new worker and ETL run.',90)
            def pair():
                second_run=replacement['runId'];worker=replacement['origin']['workerExecutionId'];escaped_second=urllib.parse.quote(second_run,safe='')
                return {'original':view(),'replacement':{'native':api.view(worker),'run':api.request('/etl/runs/'+escaped_second),
                    'partitions':api.request('/etl/runs/'+escaped_second+'/connector-partitions?limit=50')},'incidents':api.request(route+'/incidents?limit=10')}
            if args.obsolete_result_controls:
                waiting=wait_for(pair,lambda value:value['replacement']['run']['status']=='running' and
                    any(x['http'] and x['http']['fetch'] and x['http']['fetch']['phase']=='waiting' and x['http']['fetch']['lastStatusCode']==429 for x in value['replacement']['partitions']['items']) and
                    sum(x['acceptedMainEffects'] for x in value['replacement']['partitions']['items'])==2 and
                    all(not x['isClaimed'] and x['owedBatch'] is None for x in value['replacement']['partitions']['items']),
                    'The actual replacement did not retain its own HTTP429 wait before obsolete-result controls.',90)
                os.kill(host.pid,signal.SIGSTOP)
                wait_for(lambda:q.process_field(host.pid,'stat'),lambda value:'T' in value,'The replacement host did not quiesce.',5)
                try:
                    before_probe=checked_native_model();q.write_json(output/'replacement-wait-before.json',waiting)
                    dispatcher=args.dispatcher_assembly.resolve(strict=True);nonce=uuid.uuid4().hex
                    q.write_json(root/'owned-bpmn-recovery.json',{'schemaVersion':1,'controllerProcessId':os.getpid(),'modelExecutionId':model_execution,'nonce':nonce})
                    sdk=start(['dotnet',str(dispatcher),'--probe-obsolete-result',str(root),model_execution,nonce],'obsolete-sdk')
                    sdk_log=root/f'obsolete-sdk-{len(processes)-1}.log';sdk.wait(timeout=90)
                    probe=json.loads(sdk_log.read_text());q.write_json(output/'obsolete-sdk.json',probe)
                    if sdk.returncode!=0 or any(source not in p['informationalVersion'] for p in probe['products']):
                        raise ValueError('The source-matched actual obsolete-result SDK controls failed.')
                    after_probe=checked_native_model();q.write_json(output/'replacement-wait-checked-model.json',after_probe)
                    replacement_wait_stores=snapshots('replacement-wait')
                    if before_probe!=after_probe:raise ValueError('An obsolete result changed the model or original source/receipt while the replacement was live.')
                finally:os.kill(host.pid,signal.SIGCONT)
                after_probe_view=pair();q.write_json(output/'replacement-wait-after.json',after_probe_view)
                q.write_json(output/'replacement-wait-wire.json',wire())
                if after_probe_view!=waiting:raise ValueError('An obsolete result changed the replacement wait or the original physical fault.')
            if args.held_action=='exhaust':
                absorbed=native_parent_execution and args.embedded_parent=='CompensationScope'
                final=wait_for(pair,lambda value:value['replacement']['run']['status']=='faulted' and value['replacement']['native']['instance']['status']=='Faulted' and
                    value['original']['model']['status']=='TerminalSettled' and value['original']['model']['result']['status']=='Faulted' and
                    (not native_parent_execution or value['original']['embedding']['instance']['status']==('Completed' if absorbed else 'Faulted')),
                    'The actual second technical fault did not exhaust the pinned attempt budget.',120)
                checked_final=wait_for(checked_native_model,lambda value:len(value['model']['payload']['nativeResults'])==2 and value['model']['revision']==(5 if native_parent_execution else 4),
                    'Budget exhaustion lost an original attempt or final model settlement.',90)
                def checked_exhausted_worker():
                    return json.loads(subprocess.check_output(['dotnet',str(reader),'--read-bpmn-native',str(root/'elsa.db'),model_execution,replacement['origin']['workerExecutionId']],text=True,timeout=30))
                checked_replacement=wait_for(checked_exhausted_worker,lambda value:value['resultInput'] is not None and value['resultSource'] is not None and value['resultSource']['status']==2,
                    'The exhausted worker fault did not retain its original source acknowledgment.',90)
                document=checked_final['model']['payload'];first_failure,second_failure=document['failures'];old_final,new_result=document['nativeResults']
                final_incident,=[i for i in final['incidents']['items'] if i['workerExecutionId']==replacement['origin']['workerExecutionId']]
                if (final['original']['run']!=held['execution']['run'] or final['original']['native']!=held['execution']['native'] or old_final!=old_result or
                        first_failure!={**failure,'status':'Retried','retryAttemptId':replacement['origin']['attemptId'],'resolutionInputId':request['requestId']} or
                        second_failure['status']!='Final' or second_failure['attemptNumber']!=2 or second_failure['maxAttempts']!=2 or second_failure['semanticState']!=failure['semanticState'] or
                        second_failure['invocationId']!=incident['invocationId'] or second_failure['failedAttemptId']!=replacement['origin']['attemptId'] or
                        final_incident['status']!='Final' or final_incident['attemptNumber']!=2 or final_incident['maxAttempts']!=2 or document['work'] or
                        replacement['origin']['invocationId']!=incident['invocationId'] or replacement['origin']['attemptId']==incident['failedAttemptId'] or
                        checked_replacement['worker']['pinnedExecutable']!=worker_pin or checked_replacement['worker']['modelWorkOrigin']!=replacement['origin'] or
                        new_result['origin']!=replacement['origin'] or new_result['status']!=4 or new_result['faultCode']!='etl.binding-read-failed' or new_result['completion'] is not None or
                        final['replacement']['run']['terminalResult']['diagnostic']['code']!='etl.binding-read-failed' or final['replacement']['run']['selection']!=selection or
                        final['replacement']['run']['acceptedRecords']!=0 or final['replacement']['run']['rejectedRecords']!=0 or final['replacement']['partitions']['items'] or wire()!=before_wire):
                    raise ValueError('The exhausted budget lost its genuine preparation fault, immutable old failure, exact physical attempts or prior effects.')
                refused={'requestId':'retry-actual-exhausted-service','incidentId':final_incident['incidentId'],'invocationId':final_incident['invocationId'],'failedAttemptId':final_incident['failedAttemptId']}
                forbidden=urllib.request.Request(api.base+route+'/retry-service',data=json.dumps(refused).encode(),headers={'Content-Type':'application/json'})
                try:
                    with api.opener.open(forbidden,timeout=30):raise ValueError('An exhausted service budget admitted another physical attempt.')
                except urllib.error.HTTPError as error:
                    if error.code!=409:raise
                refusal=api.request(route+'/commands/'+refused['requestId'])
                if refusal['pending'] or refusal['result']['status']!='Rejected' or refusal['result']['code']!='execution-terminal':
                    raise ValueError('Budget exhaustion lost its exact terminal retry refusal.')
                q.write_json(output/'exhausted-retry-refusal.json',{'request':refused,'incident':final_incident,'httpStatus':409,'command':refusal})
                if native_parent_execution:
                    outer=wait_for(checked_embedding,lambda value:not value['queuedModelInputs'] and all(s['status']==2 for s in value['sources']),
                        'Budget exhaustion left native parent settlement or source ACK owed.',90)
                    run_bpmn,=[a for a in final['original']['embedding']['activities'] if a['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
                    if run_bpmn['status']!='Faulted' or any((a.get('authoredActivityId') or a['executableNodeId'])=='after' for a in final['original']['embedding']['activities']):
                        raise ValueError('The native parent advanced after the model exhausted its service budget.')
                    q.write_json(output/'finished-checked-embedding.json',outer)
                q.write_json(output/'finished.json',final);q.write_json(output/'finished-checked-model.json',checked_final)
                q.write_json(output/'finished-checked-replacement.json',checked_replacement);q.write_json(output/'finished-wire.json',wire())
                stop(host);finished_stores=snapshots('finished')
                if (root/'lookup.csv').exists() or q.digest(retry_input_unavailable)!=input_unavailability['sourceSha256Before']:
                    raise ValueError('The unavailable original bound input changed during the genuine retry preparation fault.')
                retry_input_unavailable.rename(root/'lookup.csv')
                input_unavailability.update(restoredAfterFailedHostStopped=True,restoredAtUtc=datetime.now(timezone.utc).isoformat(),restoredSha256=q.digest(root/'lookup.csv'))
                q.write_json(output/'retry-input-unavailability.json',input_unavailability)
                api=launch();retained=pair();retained_wire=wire();retained_checked=checked_native_model();retained_replacement=checked_exhausted_worker()
                q.write_json(output/'retained.json',retained);q.write_json(output/'retained-wire.json',retained_wire);q.write_json(output/'retained-checked-model.json',retained_checked)
                q.write_json(output/'retained-checked-replacement.json',retained_replacement)
                if native_parent_execution:q.write_json(output/'retained-checked-embedding.json',checked_embedding())
                stop(host)
                if retained!=final or retained_wire!=before_wire or retained_checked!=checked_final or retained_replacement!=checked_replacement:
                    raise ValueError('Repairing the bound input or restarting revived an exhausted service attempt.')
                q.write_json(output/'process.json',{'schemaVersion':1,'kind':'actual-model-service-budget-exhaustion','sourceRevision':source,'products':products,'parent':args.parent,'embeddedParent':args.embedded_parent,
                    'modelExecutionId':model_execution,'workflowExecutionId':execution,'runId':run,'replacementWorkerExecutionId':replacement['origin']['workerExecutionId'],'replacementRunId':replacement['runId'],
                    'processes':processes,'predecessorExitCode':exit_code,'heldPredecessorExitCode':held_exit,'heldCutAtUtc':held_cut,'cutAtUtc':cut_at,
                    'beforeStores':before_stores,'heldStores':held_stores,'finishedStores':finished_stores,'publication':publication,'modelPublication':model_publication,'etlPublication':published,
                    'nativeParentExecutionId':native_parent_execution,'nativeParentPublication':native_parent_publication,'selection':selection,'bindings':bindings,
                    'fixtureSha256':q.digest(fixture_script),'controllerSha256':q.digest(Path(__file__)),'technicalFaultPolicy':'HoldForRetry','maxServiceAttempts':2,
                    'retryAfterSeconds':retry_after,'resumptionSweepIntervalSeconds':.2,'scopeLimits':['Actual final technical fault on physical attempt2 exhausts the pinned budget after two process losses; one immutable original fault and one genuine preparation fault remain.',
                        'The owned bound lookup input is temporarily renamed without changing bytes and restored after the failed host stops. No SQL state, clock or classifier is changed.',
                        'Timer/competing-input/obsolete-result/auth/provider/retention/rollback/final upgrade/Studio remain separate.']})
                print('Actual second worker fault exhausted its pinned budget; further retry refused and repaired input did not revive either attempt.',flush=True)
                return
            final=wait_for(pair,lambda value:value['replacement']['run']['status']=='succeeded' and value['replacement']['native']['instance']['status']=='Completed' and
                value['original']['model']['status']=='TerminalSettled' and value['original']['model']['result']['status']=='Completed' and
                (not native_parent_execution or value['original']['embedding']['instance']['status']=='Completed'),
                'The real held retry did not complete the replacement worker/model/native parent.',120)
            checked_final=wait_for(checked_native_model,lambda value:len(value['model']['payload']['nativeResults'])==2 and value['model']['revision']==(5 if native_parent_execution else 4),
                'The actual successful retry lost its original failed attempt or model settlement.',90)
            def checked_retry_worker():
                return json.loads(subprocess.check_output(['dotnet',str(reader),'--read-bpmn-native',str(root/'elsa.db'),model_execution,replacement['origin']['workerExecutionId']],text=True,timeout=30))
            checked_replacement=wait_for(checked_retry_worker,lambda value:value['resultInput'] is not None and value['resultSource'] is not None and value['resultSource']['status']==2,
                'The replacement worker result did not retain its original source acknowledgment.',90)
            original_final=checked_final['model']['payload'];resolved,=original_final['failures'];results=original_final['nativeResults']
            if (final['original']['run']!=held['execution']['run'] or final['original']['native']!=held['execution']['native'] or results[0]!=old_result or
                    resolved['semanticState']!=failure['semanticState'] or resolved['status']!='Retried' or resolved['retryAttemptId']!=replacement['origin']['attemptId'] or
                    replacement['origin']['invocationId']!=incident['invocationId'] or replacement['origin']['attemptId']==incident['failedAttemptId'] or
                    checked_replacement['worker']['modelWorkOrigin']!=replacement['origin'] or checked_replacement['worker']['pinnedExecutable']!=worker_pin or
                    checked_replacement['resultInput']['attemptId']!=replacement['origin']['attemptId'] or
                    final['replacement']['run']['selection']!=selection or final['replacement']['run']['acceptedRecords']!=4 or final['replacement']['run']['rejectedRecords']!=0):
                raise ValueError('The actual physical retry lost its original failure, logical work, complete pin or successful ETL truth.')
            final_wire=wire();records=[record for post in final_wire['outputPosts'] for record in post['records']]
            from collections import Counter
            if Counter(r['id'] for r in records)!=Counter({1:2,2:2,3:1,4:1}) or any(not p['hasIdempotencyKey'] for p in final_wire['outputPosts']):
                raise ValueError('The actual new ETL attempt did not retain its at-least-once physical effects and prior two outputs.')
            q.write_json(output/'finished.json',final);q.write_json(output/'finished-wire.json',final_wire)
            q.write_json(output/'finished-checked-model.json',checked_final);q.write_json(output/'finished-checked-replacement.json',checked_replacement)
            if native_parent_execution:q.write_json(output/'finished-checked-embedding.json',checked_embedding())
            stop(host);finished_stores=snapshots('finished');api=launch()
            if args.retry_auth_controls:auth_controls('retained',route,request,accepted)
            retained=pair();retained_wire=wire();q.write_json(output/'retained.json',retained);q.write_json(output/'retained-wire.json',retained_wire)
            q.write_json(output/'retained-checked-model.json',checked_native_model())
            q.write_json(output/'retained-checked-replacement.json',json.loads(subprocess.check_output(['dotnet',str(reader),'--read-bpmn-native',str(root/'elsa.db'),model_execution,replacement['origin']['workerExecutionId']],text=True,timeout=30)))
            if native_parent_execution:q.write_json(output/'retained-checked-embedding.json',checked_embedding())
            stop(host)
            if retained!=final or retained_wire!=final_wire:raise ValueError('The successful retry or prior failed worker changed in another standard process.')
            q.write_json(output/'process.json',{'schemaVersion':1,'kind':'actual-model-held-service-retry','sourceRevision':source,'products':products,'parent':args.parent,'embeddedParent':args.embedded_parent,
                'modelExecutionId':model_execution,'workflowExecutionId':execution,'runId':run,'replacementWorkerExecutionId':replacement['origin']['workerExecutionId'],'replacementRunId':replacement['runId'],
                'processes':processes,'predecessorExitCode':exit_code,'heldPredecessorExitCode':held_exit,'heldCutAtUtc':held_cut,'cutAtUtc':cut_at,
                'beforeStores':before_stores,'heldStores':held_stores,'finishedStores':finished_stores,'publication':publication,'modelPublication':model_publication,'etlPublication':published,
                'nativeParentExecutionId':native_parent_execution,'nativeParentPublication':native_parent_publication,'selection':selection,'bindings':bindings,
                'fixtureSha256':q.digest(fixture_script),'controllerSha256':q.digest(Path(__file__)),'businessError':False,'technicalFault':True,
                **({'authorizationControls':True,'dispatcherSha256':q.digest(dispatcher)} if args.retry_auth_controls else {}),
                **({'obsoleteResultControls':True,'dispatcherSha256':q.digest(dispatcher),'replacementWaitStores':replacement_wait_stores} if args.obsolete_result_controls else {}),
                'technicalFaultPolicy':'HoldForRetry','maxServiceAttempts':2,'retryAfterSeconds':retry_after,'resumptionSweepIntervalSeconds':.2,
                'scopeLimits':['Actual one authorized held-service retry after process loss; old failed state stays terminal, with one new physical worker/run for the same semantic work. Prior effects may repeat.',
                    'Snapshot availability is repaired externally without changing bytes or SQL state. Budget exhaustion, cancellation while held, old-result controls, provider/retention/rollback and final upgrade/Studio remain separate gates.']})
            print('Actual held failure survived second SIGKILL; authorized retry completed one new worker and remained stable in another process.',flush=True)
            return
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
        elif args.native_parent_cancel:
            request=urllib.request.Request(api.base+'/runtime/workflows/alteration-plans',data=json.dumps({'target':{'executionIds':[native_parent_execution]},'alterations':[{'kind':'CancelWorkflow','schemaVersion':1,'payload':{}}]}).encode(),
                headers={'Content-Type':'application/json','Idempotency-Key':'cancel-actual-runbpmn-parent'})
            with api.opener.open(request,timeout=30) as response:cancelled=json.load(response)
            q.write_json(output/'native-parent-cancel-response.json',cancelled)
        if args.pending_parent_result_crash or args.successful_parent_result_crash:
            if args.successful_parent_result_crash:
                pending=wait_for(view,lambda value:value['embedding']['instance']['status']=='Running' and value['model']['status']=='Running' and value['run']['status']=='succeeded' and value['native']['instance']['status']=='Completed',
                    'Actual ETL/native success did not retain its owed model result and original native parent wait.',60)
            else:
                pending=wait_for(view,lambda value:value['embedding']['instance']['status']=='Cancelled' and value['model']['status']=='Running' and value['run']['status']=='running',
                    'The actual cancelled native parent did not retain its owed model cancellation.',30)
            os.kill(host.pid,signal.SIGSTOP)
            wait_for(lambda:q.process_field(host.pid,'stat'),lambda value:'T' in value,'The pending-parent host did not quiesce.',5)
            dispatcher=args.dispatcher_assembly.resolve(strict=True);nonce=uuid.uuid4().hex
            original_parent=json.loads((output/'before-checked-embedding.json').read_text())
            original_run_bpmn,=[a for a in original_parent['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
            q.write_json(root/'owned-bpmn-recovery.json',{'schemaVersion':1,'controllerProcessId':os.getpid(),'modelExecutionId':model_execution,'nativeParentExecutionId':native_parent_execution,
                'nativeParentInvocationId':original_run_bpmn['invocationId'],'nonce':nonce})
            sdk=start(['dotnet',str(dispatcher),'--consume-native-success' if args.successful_parent_result_crash else '--consume-native-result',str(root),model_execution,nonce]+
                (['--parent-return-stage',args.successful_parent_result_crash] if args.successful_parent_result_crash else []),'consumption-sdk')
            sdk_log=root/f'consumption-sdk-{len(processes)-1}.log';sdk.wait(timeout=90)
            consumed=json.loads(sdk_log.read_text());q.write_json(output/'sdk-consume.json',consumed)
            if sdk.returncode!=0 or any(source not in p['informationalVersion'] for p in consumed['products']):
                raise ValueError('The source-matched production SDK did not consume the actual embedded result: '+sdk_log.read_text())
            os.kill(host.pid,signal.SIGCONT);pending=view();pending_wire=wire();os.kill(host.pid,signal.SIGSTOP)
            wait_for(lambda:q.process_field(host.pid,'stat'),lambda value:'T' in value,'The consumed pending-parent host did not quiesce.',5)
            pending_cut_at=datetime.now(timezone.utc).isoformat();pending_stores=snapshots('parent-pending');pending_checked=checked_native_model();pending_embedding=checked_embedding()
            q.write_json(output/'parent-pending.json',pending);q.write_json(output/'parent-pending-wire.json',pending_wire)
            q.write_json(output/'parent-pending-checked-model.json',pending_checked);q.write_json(output/'parent-pending-checked-embedding.json',pending_embedding)
            parent_result,=[s for s in pending_embedding['sources'] if s['intent']['kind']=='Elsa.Bpmn.ResumeNativeParent.v1']
            if args.successful_parent_result_crash:
                stage=args.successful_parent_result_crash;settled=stage=='settled';parent_consumed=stage!='pending'
                actual_run_bpmn,=[a for a in pending_embedding['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
                if (pending_checked['model']['lifecycle']!=(3 if settled else 2) or pending_checked['model']['revision']!=(3 if settled else 2) or pending_checked['worker']['status']!=3 or pending_checked['resultInput']!=consumed['receipt'] or
                        pending_checked['resultSource']['status']!=3 or pending_checked['resultSource']['deliveredAt'] is not None or parent_result['status']!=(3 if parent_consumed else 0) or parent_result['deliveredAt'] is not None or
                        len([r for r in pending_embedding['inputs'] if r['inputKind']=='native-parent-settled'])!=int(settled) or
                        pending['run']['status']!='succeeded' or pending['embedding']['instance']['status'] not in (('Running','Completed') if parent_consumed else ('Running',)) or
                        actual_run_bpmn['status']!=(4 if parent_consumed else 3) or
                        (pending_embedding['queuedModelInputs'] and (not parent_consumed or settled))):
                    raise ValueError('The actual successful parent result did not retain the selected consumption/settlement/ACK boundary.')
            elif (pending_checked['model']['lifecycle']!=2 or pending_checked['model']['revision']!=3 or pending_checked['worker']['status']!=5 or pending_checked['resultInput']!=consumed['receipt'] or
                    pending_checked['resultSource']['status']!=3 or pending_checked['resultSource']['deliveredAt'] is not None or parent_result['status']!=0 or parent_result['deliveredAt'] is not None or
                    any(r['inputKind']=='native-parent-settled' for r in pending_embedding['inputs']) or pending_wire!=before_wire or pending['run']['status']!='cancelled' or
                    pending_embedding['queuedModelInputs'] or any(not c['settled'] for c in pending_checked['model']['payload']['cancellations'])):
                raise ValueError('The actual terminal model did not retain its original parent result and source acknowledgment as owed.')
            stop(host,crash=True)
            if host.returncode!=-signal.SIGKILL:raise ValueError('The pending-parent host did not lose its actual process.')
            pending_cancellation_evidence={**({'successfulParentResultCrashStage':args.successful_parent_result_crash} if args.successful_parent_result_crash else {'pendingParentResultCrash':True}),
                'parentPendingStores':pending_stores,'parentPendingCutAtUtc':pending_cut_at,'parentPendingPredecessorExitCode':host.returncode,
                'sweepIntervalsSeconds':[.2,300,.2,.2],'dispatcherSha256':q.digest(dispatcher)}
            features['WorkflowsRuntimeResumption']={'SweepIntervalSeconds':.2};q.write_json(root/'shells.json',shells);api=launch()
        if args.pending_cancellation_crash:
            try:
                stage="model-command" if args.model_cancel else "native-terminal"
                pending=wait_for(view,lambda value:value["run"]["status"]=="running" and
                    value["native"]["instance"]["status"]==("Running" if args.model_cancel else "Cancelled") and
                    value["model"]["status"]==("TerminalPendingSettlement" if args.model_cancel else "Running"),
                    "The actual pending cancellation/result cut was not retained.",30)
                os.kill(host.pid,signal.SIGSTOP)
                wait_for(lambda:q.process_field(host.pid,"stat"),lambda value:"T" in value,"The pending-cancellation host did not quiesce.",5)
                pending_cut_at=datetime.now(timezone.utc).isoformat();pending_wire=wire();pending_stores=snapshots("pending")
                pending_checked=json.loads(subprocess.check_output(["dotnet",str(reader),"--read-bpmn-retirement",str(root/"elsa.db"),model_execution],text=True,timeout=30))
                q.write_json(output/"pending.json",pending);q.write_json(output/"pending-checked.json",pending_checked);q.write_json(output/"pending-wire.json",pending_wire)
                owed_kind="Elsa.Bpmn.CancelNativeWork.v1" if args.model_cancel else "Elsa.Bpmn.NativeWorkResult.v1"
                owed=[s for s in pending_checked["sources"] if s["intent"]["kind"]==owed_kind]
                if (len(owed)!=1 or owed[0]["status"] in (2,4) or pending_checked["model"]["revision"]!=(2 if args.model_cancel else 1) or
                        pending_checked["worker"]["status"]!=(1 if args.model_cancel else 5) or
                        any(x["inputKind"]=="native-result" for x in pending_checked["inputs"]) or pending_wire!=before_wire):
                    raise ValueError("The actual cancellation/result source was not still owed at the second process-loss cut.")
                consumed_evidence={}
                if args.native_result_before_etl_cancel:
                    dispatcher=args.dispatcher_assembly.resolve(strict=True);nonce=uuid.uuid4().hex
                    q.write_json(root/"owned-bpmn-recovery.json",{"schemaVersion":1,"controllerProcessId":os.getpid(),"modelExecutionId":model_execution,"nonce":nonce})
                    sdk=start(["dotnet",str(dispatcher),"--probe-pending-native-cancel",str(root),model_execution,nonce],"inverse-cancel-sdk")
                    sdk_log=root/f"inverse-cancel-sdk-{len(processes)-1}.log";sdk.wait(timeout=90)
                    probe=json.loads(sdk_log.read_text());q.write_json(output/"sdk-inverse-cancel.json",probe)
                    if sdk.returncode!=0 or probe["disposition"]!="Deferred" or any(source not in p["informationalVersion"] for p in probe["products"]):
                        raise ValueError("The actual inverse-order cancellation was not deferred: "+sdk_log.read_text())
                    deferred_stores=snapshots("deferred")
                    deferred_checked=json.loads(subprocess.check_output(["dotnet",str(reader),"--read-bpmn-retirement",str(root/"elsa.db"),model_execution],text=True,timeout=30))
                    q.write_json(output/"deferred-checked.json",deferred_checked)
                    if deferred_checked["model"]!=pending_checked["model"] or deferred_checked["queuedModelInputs"] or any(x["inputKind"]=="native-result"for x in deferred_checked["inputs"]):
                        raise ValueError("The premature cancelled-worker result changed model state or retained an accepted input.")
                    consumed_evidence={"nativeResultBeforeEtlCancel":True,"deferredStores":deferred_stores,"dispatcherSha256":q.digest(dispatcher)}
                if args.consumed_before_ack_crash:
                    dispatcher=args.dispatcher_assembly.resolve(strict=True);nonce=uuid.uuid4().hex
                    q.write_json(root/"owned-bpmn-recovery.json",{"schemaVersion":1,"controllerProcessId":os.getpid(),"modelExecutionId":model_execution,"nonce":nonce})
                    sdk=start(["dotnet",str(dispatcher),"--consume-native-result",str(root),model_execution,nonce],"consumption-sdk")
                    sdk_log=root/f"consumption-sdk-{len(processes)-1}.log";sdk.wait(timeout=90)
                    consumed=json.loads(sdk_log.read_text());q.write_json(output/"sdk-consume.json",consumed)
                    if sdk.returncode!=0 or any(source not in p["informationalVersion"] for p in consumed["products"]):
                        raise ValueError("The source-matched production SDK did not consume the actual result: "+sdk_log.read_text())
                    os.kill(host.pid,signal.SIGCONT);consumed_view=view();consumed_wire=wire();os.kill(host.pid,signal.SIGSTOP)
                    wait_for(lambda:q.process_field(host.pid,"stat"),lambda value:"T" in value,"The consumed-before-ACK host did not quiesce.",5)
                    consumed_cut_at=datetime.now(timezone.utc).isoformat();consumed_stores=snapshots("consumed")
                    consumed_checked=json.loads(subprocess.check_output(["dotnet",str(reader),"--read-bpmn-retirement",str(root/"elsa.db"),model_execution],text=True,timeout=30))
                    q.write_json(output/"consumed.json",consumed_view);q.write_json(output/"consumed-checked.json",consumed_checked);q.write_json(output/"consumed-wire.json",consumed_wire)
                    result_source,=[s for s in consumed_checked["sources"] if s["intent"]["kind"]=="Elsa.Bpmn.NativeWorkResult.v1"]
                    receipt,=[x for x in consumed_checked["inputs"] if x["inputKind"]=="native-result"]
                    if (consumed_checked["model"]["lifecycle"]!=3 or consumed_checked["model"]["revision"]!=(3 if args.model_cancel else 2) or
                            consumed_checked["queuedModelInputs"] or result_source["status"]!=3 or result_source["deliveredAt"] is not None or
                            receipt!=consumed["receipt"] or consumed_view["run"]["status"]!="cancelled" or consumed_wire!=before_wire):
                        raise ValueError("The actual consumed result lacks its exact receipt and still-owed source ACK.")
                    consumed_evidence={"postConsumptionPreAck":True,"consumedStores":consumed_stores,"consumedCutAtUtc":consumed_cut_at,"dispatcherSha256":q.digest(dispatcher)}
                stop(host,crash=True)
                if host.returncode!=-signal.SIGKILL:raise ValueError("The pending cancellation host did not lose its actual process.")
                pending_cancellation_evidence={"pendingCancellationStage":stage,"pendingStores":pending_stores,"pendingCutAtUtc":pending_cut_at,
                    "pendingPredecessorExitCode":host.returncode,"sweepIntervalsSeconds":[.2,300,.2,.2],**consumed_evidence}
                print("Actual "+stage+" cancellation/result deliveries retained; second owned Workbench killed.",flush=True)
                features["WorkflowsRuntimeResumption"]={"SweepIntervalSeconds":.2};q.write_json(root/"shells.json",shells);api=launch()
            except Exception as failure:
                for child in children:
                    if child is not host and child is not fixture:stop(child)
                stop(host);failed_stores=snapshots("failed")
                q.write_json(output/"failure.json",{"sourceRevision":source,"failureType":type(failure).__name__,"processes":processes,"products":products,
                    "pendingCancellationStage":"model-command" if args.model_cancel else "native-terminal","beforeStores":before_stores,"failedStores":failed_stores})
                raise
        outcome="cancelled" if cancelling else "faulted" if args.technical_fault else "businessError" if args.business_error else "succeeded"
        native_terminal="Cancelled" if cancelling else "Faulted" if args.technical_fault else "Completed"
        model_terminal="Cancelled" if cancelling else "Faulted" if args.technical_fault or args.business_error and not caught_business_error else "Completed"
        faulted=model_terminal=='Faulted'
        absorbed=faulted and args.embedded_parent=='CompensationScope'
        parent_terminal='Completed' if absorbed else model_terminal
        try:
            finished=wait_for(view,lambda value:value["run"]["status"]==outcome and value["native"]["instance"]["status"]==native_terminal and
                (not direct_bpmn or value["model"]["status"]=="TerminalSettled" and value["model"]["result"]["status"]==model_terminal) and
                (not native_parent_execution or value['embedding']['instance']['status']==parent_terminal),"The actual ETL/native/model chain did not finish after process loss.",120)
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
        if args.final_service_fault:
            route='/bpmn/executions/'+urllib.parse.quote(model_execution,safe='')
            incident,=api.request(route+'/incidents?limit=10')['items']
            if incident['status']!='Final' or incident['attemptNumber']!=1 or incident['maxAttempts']!=1:
                raise ValueError('The explicit final fault lost its retained immutable failure evidence.')
            refusal={'requestId':'retry-actual-final-service','incidentId':incident['incidentId'],'invocationId':incident['invocationId'],'failedAttemptId':incident['failedAttemptId']}
            request=urllib.request.Request(api.base+route+'/retry-service',data=json.dumps(refusal).encode(),headers={'Content-Type':'application/json'})
            try:
                with api.opener.open(request,timeout=30):raise ValueError('A final semantic fault was allowed to retry.')
            except urllib.error.HTTPError as error:
                if error.code!=409:raise
            receipt=api.request(route+'/commands/'+refusal['requestId'])
            if receipt['pending'] or receipt['result']['status']!='Rejected' or receipt['result']['code']!='execution-terminal':
                raise ValueError('The actual final-fault retry did not retain its terminal conflict.')
            q.write_json(output/'final-retry-refusal.json',{'request':refusal,'incident':incident,'httpStatus':409,'command':receipt})
        if args.model_cancel:
            command=api.request("/bpmn/executions/"+urllib.parse.quote(model_execution,safe="")+"/commands/cancel-actual-bpmn-etl")
            if command["pending"] or command["result"]["status"]!="Accepted" or command["result"]["lifecycle"]!="TerminalPendingSettlement":
                raise ValueError("The BPMN cancel command did not retain its durable pending-settlement decision.")
            q.write_json(output/"model-cancel-command.json",command)
        if native_parent_execution:
            checked_outer=wait_for(checked_embedding,lambda value:not value['queuedModelInputs'] and all(s['status']==2 for s in value['sources']),
                'The real RunBpmn parent/model delivery remains owed.',90)
            q.write_json(output/'finished-checked-embedding.json',checked_outer)
            outer=finished['embedding'];run_bpmn,=[a for a in outer['activities'] if a['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
            after=[a for a in outer['activities'] if (a.get('authoredActivityId') or a['executableNodeId'])=='after']
            incidents=outer['incidents']
            faulted_ids={a['activityExecutionId'] for a in outer['activities'] if a['status']=='Faulted'}
            scopes=[a for a in outer['activities'] if a['activityType']=='Elsa.Activities.Compensation.Activities.CompensationScope']
            if (run_bpmn['status']!=model_terminal or
                    (faulted and (len(incidents)!=len(faulted_ids) or {i['activityExecutionId'] for i in incidents}!=faulted_ids or
                        any(i['failureType']!='ActivityReturnedFault' or i['status']!=('Resolved' if absorbed else 'Blocking') or
                            i['resolutionOutcome']['actionKind']!=('AbsorbFault' if absorbed else 'FaultWorkflow') for i in incidents))) or
                    (absorbed and (len(scopes)!=1 or scopes[0]['status']!='Completed' or scopes[0]['outcomeNames']!=['Compensated'])) or
                    (not faulted and incidents) or ((cancelling or faulted) and after) or
                    (not cancelling and not faulted and args.embedded_parent!='Root' and (len(after)!=1 or after[0]['status']!='Completed'))):
                raise ValueError('The real native RunBpmn parent did not preserve its completion/fault/cancellation behavior.')
        result=finished["run"];rows=finished["partitions"]["items"];native_view=finished["native"]
        if (result["formatVersion"]!=2 or result["selection"]!=selection or result["terminalResult"]["outcome"]!=outcome or
                result["acceptedRecords"]!=(2 if cancelling or args.technical_fault else 3 if args.business_error else 4) or result["rejectedRecords"]!=(1 if args.business_error else 0) or result["filteredRecords"]!=0 or result["outstandingRecords"]!=0 or result["outstandingBytes"]!=0 or
                len(rows)!=2 or finished["partitions"]["nextCursor"] is not None or any(item["owedBatch"] is not None for item in rows) or
                not cancelling and not args.technical_fault and any(not item["http"]["exhausted"] or item["http"]["pageSequence"]!=1 for item in rows)):
            raise ValueError("The recovered ETL run did not retain the expected output accounting and exact partitions.")
        if (not args.technical_fault and native_view["incidents"] or len(native_view["activities"])!=(1 if args.parent in ("RunEtl","BpmnModel") else 3) or any(item["status"]!=native_terminal for item in native_view["activities"])):
            raise ValueError("The recovered native parent did not execute RunEtl and its successor once.")
        old_activities={(item.get("authoredActivityId") or item["executableNodeId"]):item for item in before["native"]["activities"]}
        etl_activity=next(item for item in native_view["activities"] if (item.get("authoredActivityId") or item["executableNodeId"])=="etl")
        if etl_activity["outcomeNames"]!=([] if cancelling or args.technical_fault else ["BusinessError" if args.business_error else "Done"]):
            raise ValueError("The real RunEtl activity did not preserve its typed terminal outcome.")
        if args.technical_fault:
            incident,=native_view['incidents']
            failed=[r['http']['fetch'] for r in rows if r['partitionOrdinal']==1]
            if (incident['activityExecutionId']!=etl_activity['activityExecutionId'] or incident['status']!='Blocking' or
                    incident['failureType']!='ActivityReturnedFault' or incident['resolutionOutcome']['actionKind']!='FaultWorkflow' or
                    result['terminalResult']['diagnostic']['code']!='etl.snapshot-read-failed' or len(failed)!=1 or failed[0]!=retry):
                raise ValueError('The actual technical failure lost its original waiting HTTP retry or native fault incident.')
        if "after" in old_activities or old_activities["etl"]["activityExecutionId"]!=next(item["activityExecutionId"] for item in native_view["activities"] if (item.get("authoredActivityId") or item["executableNodeId"])=="etl"):
            raise ValueError("The native RunEtl invocation was recreated or its successor ran before the cut.")
        for field in ("artifactId","artifactHash","artifactVersion","definitionId","definitionVersionId"):
            if before["native"]["instance"][field]!=native_view["instance"][field]:raise ValueError("The actual native executable full pin changed during ETL recovery.")
        q.write_json(output/"finished.json",finished);finished_wire=wire();q.write_json(output/"finished-wire.json",finished_wire)
        b_requests=[item for item in finished_wire["requests"] if item.get("shard")=="shard-b"]
        if (cancelling and (len(b_requests)!=1 or b_requests[0]["status"]!=429) or
                args.technical_fault and finished_wire!=before_wire or
                not cancelling and not args.technical_fault and (len(b_requests)!=3 or [item["status"] for item in b_requests]!=[429,200,200] or b_requests[1]["receivedAtMs"]-b_requests[0]["receivedAtMs"]<20000)):
            raise ValueError("The actual source retried early or failed to resume its original two-page shard.")
        records=[item for post in finished_wire["outputPosts"] for item in post["records"]]
        expected_records={1:"Ada",2:"Grace"} if cancelling or args.technical_fault else {1:"Ada",2:"Grace",3:"Linus"} if args.business_error else {1:"Ada",2:"Grace",3:"Linus",4:"Margaret"}
        if {item["id"]:item["name"] for item in records}!=expected_records or any(not post["hasIdempotencyKey"] for post in finished_wire["outputPosts"]):
            raise ValueError("The actual HTTP receiver lacks mapped output or idempotency headers.")
        errors=[json.loads(line) for file in (root/"errors").glob("*.jsonl") for line in file.read_text().splitlines() if line.strip()]
        if len(errors)!=(1 if args.business_error else 0):raise ValueError("The real safe-error destination differs from the rejected record count.")
        q.write_json(output/"safe-errors.json",errors)
        stop(host);finished_stores=snapshots("finished")
        api=launch()
        observed_cancelled_after_deadline=None
        if cancelling or args.technical_fault:
            deadline=utc_instant(retry["notBefore"])+timedelta(seconds=2)
            while datetime.now(timezone.utc)<deadline:
                if view()!=finished or wire()!=finished_wire:raise ValueError("Terminal work changed while its original HTTP retry became due.")
                time.sleep(.25)
            observed_cancelled_after_deadline=datetime.now(timezone.utc).isoformat()
        retained=view();retained_wire=wire();q.write_json(output/"retained.json",retained);q.write_json(output/"retained-wire.json",retained_wire)
        if direct_bpmn:q.write_json(output/"retained-checked-model.json",checked_native_model())
        if native_parent_execution:q.write_json(output/'retained-checked-embedding.json',checked_embedding())
        stop(host)
        if retained!=finished or retained_wire!=finished_wire:raise ValueError("The terminal pair or physical HTTP observations changed after another process.")
        if args.technical_fault:
            if lookup_snapshot.exists() or q.digest(unavailable)!=snapshot_unavailability['snapshotSha256Before']:
                raise ValueError('The unavailable original snapshot was rewritten during actual fault recovery.')
            unavailable.rename(lookup_snapshot)
            snapshot_unavailability.update(restoredAfterOwnedHostsStopped=True,restoredAtUtc=datetime.now(timezone.utc).isoformat(),restoredSha256=q.digest(lookup_snapshot))
            q.write_json(output/'snapshot-unavailability.json',snapshot_unavailability)
        q.write_json(output/"process.json",{"schemaVersion":1,"kind":("actual-native-runbpmn-etl-parent-cancellation" if args.native_parent_cancel else "actual-native-runbpmn-etl-http-recovery") if native_parent_execution else "actual-direct-bpmn-native-etl-model-cancellation" if args.model_cancel else "actual-direct-bpmn-native-etl-worker-cancellation" if args.native_cancel else "actual-direct-bpmn-native-etl-pipeline-cancellation" if args.pipeline_cancel else "actual-direct-bpmn-native-etl-http-recovery" if direct_bpmn else "actual-native-etl-http-recovery","sourceRevision":source,"products":products,"parent":args.parent,"processes":processes,"predecessorExitCode":exit_code,
            "definition":definition,"selection":selection,"workflowExecutionId":execution,"runId":run,"publication":publication,"etlPublication":published,"bindings":bindings,
            "fixtureSha256":q.digest(fixture_script),"controllerSha256":q.digest(Path(__file__)),"cutAtUtc":cut_at,"businessError":args.business_error,"pipelineCancel":args.pipeline_cancel,"nativeCancel":args.native_cancel,"modelCancel":args.model_cancel,"retryAfterSeconds":retry_after,"beforeStores":before_stores,"finishedStores":finished_stores,
            **({'businessErrorBoundary':args.business_error_boundary} if args.business_error_boundary else {}),
            **({'technicalFault':True,'technicalFaultPolicy':'FinalFault','technicalFaultSource':'retained-lookup-snapshot-unavailable',
                'observedFaultedAfterRetryDeadlineAtUtc':observed_cancelled_after_deadline} if args.technical_fault else {}),
            **({'explicitServiceFaultPolicy':'FinalFault'} if args.final_service_fault else {}),
            **({'resumptionSweepIntervalSeconds':.2} if args.business_error_boundary else {}),
            **({"observedCancelledAfterRetryDeadlineAtUtc":observed_cancelled_after_deadline} if cancelling else {}),
            **({"modelPublication":model_publication,"modelExecutionId":model_execution} if direct_bpmn else {}),
            **({'embeddedParent':args.embedded_parent,'nativeParentExecutionId':native_parent_execution,'nativeParentPublication':native_parent_publication,'nativeParentCancel':args.native_parent_cancel} if native_parent_execution else {}),
            **(pending_cancellation_evidence or {}),
            "scopeLimits":["Actual successful native RunBpmn parent return across a second SIGKILL at the selected consumption/settlement/ACK boundary; the source and receipts remain original, with broader fault/retry/provider/upgrade/Studio acceptance separate." if args.successful_parent_result_crash else "Actual native RunBpmn parent, embedded service model and published native RunEtl worker over the real HTTP429 recovery journey; broader outcomes/parent races, held retry and final upgrade/Studio remain separate acceptance." if native_parent_execution else "Actual model consumption of the original admitted RunEtl cancellation result, followed by second SIGKILL with native result/model-cancel ACK still owed; fresh standard hosts finish acknowledgment. Embedding, held retry, other providers and final upgrade/Studio remain open." if args.consumed_before_ack_crash else "Actual admitted RunEtl/HTTP429 recovery followed by a second process loss with cancellation/result delivery still owed; post-consumption/pre-ACK, embedding, held retry and final upgrade/Studio remain separate gates." if args.pending_cancellation_crash else "BPMN-originated cancellation of an admitted native RunEtl worker after process recovery; cancellation before admission, pending cancel/result crashes, embedding, held retry and final upgrade/Studio remain unproved." if args.model_cancel else "Native-worker-originated cancellation into the actual ETL run and direct BPMN only; model-to-worker cancellation, embedding, held retry and final upgrade/Studio remain unproved." if args.native_cancel else "Pipeline-originated cancellation through one actual direct BPMN/native RunEtl root only; reverse model-to-worker cancellation, embedding, held fault retry and final upgrade/Studio remain unproved." if args.pipeline_cancel else "Direct start/service/end with one real closed native RunEtl worker; embedding, broader service/event shapes, cancellation propagation and held terminal-fault retry remain unproved." if direct_bpmn else "Real transient HTTP retry and native RunEtl recovery only; no BPMN/model worker bridge or held terminal-fault retry is proved.","The crash cut uses SIGSTOP to copy committed stores, then SIGKILL. SQL state and clocks are unchanged; only the owned retained lookup snapshot path is temporarily renamed, with identical bytes restored after all hosts stop." if args.technical_fault else "The crash cut uses SIGSTOP only to copy committed stores, then SIGKILL; no persisted state or clock is edited.","External HTTP effects remain at least once."]})
        print("Actual ETL/native"+("/BPMN chain" if direct_bpmn else " journey")+" completed and survived another Workbench process.",flush=True)
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
