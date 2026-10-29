#!/usr/bin/env python3
"""Read-only verification of actual native and direct BPMN ETL HTTP recovery journeys."""
import argparse
import base64
from contextlib import closing
from datetime import datetime, timezone
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile

_spec=importlib.util.spec_from_file_location("etl_recovery",Path(__file__).with_name("qualify_http_recovery.py"))
runner=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(runner)
q=runner.q


def key(value): return base64.b64encode(value.encode("utf-16le")).decode()
def decoded(value):
    if not value.startswith("u16:"):raise ValueError("The ETL invocation lost its checked lossless-text encoding.")
    return base64.b64decode(value[4:],validate=True).decode("utf-16le")
def node(value): return value.get("authoredActivityId") or value["executableNodeId"]


def encoded_tree(value):
    if isinstance(value,str):return key(value)
    if isinstance(value,dict):return {name:encoded_tree(item) for name,item in value.items()}
    if isinstance(value,list):return [encoded_tree(item) for item in value]
    return value


def model_bridge(read,process,before,finished,business_error):
    checked={phase:read(phase+"-checked-model.json") for phase in ("before","finished","retained")}
    if checked["finished"]!=checked["retained"]:raise ValueError("The actual native/model return proof changed after another process.")
    first,last=checked["before"],checked["finished"];origin=first["worker"]["modelWorkOrigin"]
    pin={name:process["publication"][name] for name in ("artifactId","artifactHash","artifactVersion","definitionId","definitionVersionId")}
    model_pin=process["modelPublication"]["executable"];model_id=process["modelExecutionId"]
    for phase,value in checked.items():
        model,worker=value["model"],value["worker"]
        if (value["readOnly"] is not True or model["executionId"]!=model_id or model["executable"]!=model_pin or
                model["materialIncarnationId"]!=process["modelPublication"]["incarnationId"] or worker["workflowExecutionId"]!=process["workflowExecutionId"] or
                worker["pinnedExecutable"]!=pin or worker["modelWorkOrigin"]!=origin or worker["parentWorkflowExecutionId"] is not None or
                worker["authority"]!=model["authority"] or origin["executable"]!=model_pin or origin["workerExecutable"]!=pin or
                origin["executionId"]!=model_id or origin["materialIncarnationId"]!=model["materialIncarnationId"] or
                origin["workerExecutionId"]!=worker["workflowExecutionId"] or origin["scope"]!=model["scope"]):
            raise ValueError("The actual worker lost its complete model/native origin or authority.")
        source=value["startSource"]
        if (source["outboxItemId"]!=origin["sourceOutboxItemId"] or source["status"]!=2 or source["intent"]["payload"]!=origin or
                source["intent"]["kind"]!="Elsa.Bpmn.StartNativeWork.v1"):
            raise ValueError("The actual native worker lacks its committed model start source.")
    if (first["model"]["revision"]!=1 or first["model"]["lifecycle"]!=0 or first["worker"]["status"]!=1 or
            [item["nativeWork"] for item in first["model"]["payload"]["work"]]!=[origin] or first["resultInput"] is not None or first["resultSource"] is not None):
        raise ValueError("The actual crash cut lost its live model invocation.")
    result,=last["model"]["payload"]["nativeResults"];receipt=last["resultInput"];source=last["resultSource"]
    expected="Faulted" if business_error else "Completed"
    if (last["model"]["lifecycle"]!=3 or last["model"]["revision"]!=2 or last["model"]["payload"]["work"] or last["worker"]["status"]!=3 or
            result["origin"]!=origin or result["status"]!=3 or last["model"]["terminalResult"]["status"]!=expected or
            before["model"]["status"]!="Running" or finished["model"]["status"]!="TerminalSettled" or finished["model"]["result"]["status"]!=expected):
        raise ValueError("The actual model did not retain the native worker's typed terminal outcome.")
    if (receipt is None or receipt["inputId"]!=origin["resultInputId"] or receipt["inputKind"]!="native-result" or receipt["executionId"]!=model_id or
            receipt["invocationId"]!=origin["invocationId"] or receipt["attemptId"]!=origin["attemptId"] or receipt["acceptedRevision"]!=2 or receipt["result"]["status"]!="Accepted" or
            source is None or source["status"]!=2 or source["stimulusDeliverySourceScope"]!=origin["scope"] or source["outboxItemId"]!=result["sourceOutboxItemId"] or source["intent"]["payload"]!=result or
            source["intent"]["kind"]!=origin["resultIntentKind"] or datetime.fromisoformat(source["deliveredAt"])<datetime.fromisoformat(receipt["acceptedAt"])):
        raise ValueError("The actual result was acknowledged without its exact model consumption proof.")
    completion=result["completion"];typed=completion["result"]
    if (completion["isRedacted"] or completion["resultType"]["alias"]!="Elsa.Etl.Workflows.Runtime.Models.RunEtlResult" or
            completion["outcomeKey"]!=("BusinessError" if business_error else "Done") or typed!={"runId":process["runId"],"outcome":1 if business_error else 0,
                "acceptedRecords":3 if business_error else 4,"rejectedRecords":1 if business_error else 0,"filteredRecords":0,"diagnosticCode":"etl.rows-rejected" if business_error else None}):
        raise ValueError("The actual typed RunEtl result was lost or contradicted its ETL terminal.")
    return checked,origin,result


def physical_model_bridge(connection,phase,checked,origin,result):
    observed=checked[phase];model=observed["model"]
    row=connection.execute("SELECT Lifecycle,Revision,ContentJson FROM elsa_runtime_model_execution WHERE ExecutionId=?",(key(model["executionId"]),)).fetchone()
    stored=json.loads(row[2]) if row else None
    if (row is None or row[:2]!=(model["lifecycle"],model["revision"]) or stored["payload"]!=model["payload"] or
            stored["terminalResult"]!=model["terminalResult"] or stored["executable"]!=encoded_tree(model["executable"]) or stored["materialIncarnationId"]!=key(model["materialIncarnationId"])):
        raise ValueError("The physical model state differs from its checked reader.")
    worker=connection.execute("SELECT SchemaVersion,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?",(key(origin["workerExecutionId"]),)).fetchone()
    if worker is None or worker[0]!="2.0.0" or json.loads(worker[1])["modelWorkOrigin"]!=encoded_tree(origin) or connection.execute("SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state").fetchone()[0]!=1:
        raise ValueError("The physical worker lost its protected exact model origin.")
    materials=[json.loads(row[0]).get("material") for row in connection.execute("SELECT ContentJson FROM elsa_runtime_workflow_executable")]
    material,=[item for item in materials if item is not None and item["identity"]==encoded_tree(model["executable"])]
    if material["dependencies"]!=[{"bindingId":key(origin["bindingId"]),"executable":encoded_tree(origin["workerExecutable"])}]:
        raise ValueError("The physical model lost its exact native worker dependency.")
    for name in ("startSource","resultSource"):
        source=observed[name]
        if source is None:continue
        row=connection.execute("SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?",(key(source["outboxItemId"]),)).fetchone()
        stored=json.loads(row[1]) if row else None
        if row is None or row[0]!=2 or stored["intent"]["payload"]!=source["intent"]["payload"] or stored["deliveredAt"]!=source["deliveredAt"]:
            raise ValueError("The physical native/model delivery source differs from its consumption proof.")
    rows=connection.execute("SELECT InputId,ContentJson FROM elsa_runtime_model_input WHERE ExecutionId=?",(key(model["executionId"]),)).fetchall()
    if len(rows)!=(1 if phase=="before" else 2):raise ValueError("The physical model accepted an unexpected input.")
    receipt=observed["resultInput"]
    if receipt is not None:
        stored,=[json.loads(row[1]) for row in rows if row[0]==key(receipt["inputId"])]
        if (stored["contentFingerprint"]!=key(receipt["contentFingerprint"]) or stored["inputKind"]!=key("native-result") or
                stored["invocationId"]!=key(origin["invocationId"]) or stored["attemptId"]!=key(origin["attemptId"]) or stored["result"]!=receipt["result"]):
            raise ValueError("The physical model receipt lost its exact accepted input identity.")
        activity,=connection.execute("SELECT ContentJson FROM elsa_runtime_activity_execution_state").fetchall()
        completion=json.loads(activity[0])["completion"];expected=result["completion"]
        if (completion["invocationId"]!=key(expected["invocationId"]) or completion["attemptId"]!=key(expected["attemptId"]) or
                completion["contractFingerprint"]!=key(expected["contractFingerprint"]) or completion["outcomeKey"]!=key(expected["outcomeKey"]) or
                completion["result"]["inlineValue"]!=expected["result"] or completion["result"]["type"]["alias"]!=key(expected["resultType"]["alias"])):
            raise ValueError("The model return differs from the authoritative physical native root completion.")


def verify(path):
    manifest=json.loads(path.read_text());root=path.parent
    direct=manifest.get("kind")=="actual-direct-bpmn-native-etl-recovery"
    if manifest.get("schemaVersion")!=1 or manifest.get("kind") not in ("actual-native-etl-recovery-reference","actual-direct-bpmn-native-etl-recovery"):raise ValueError("Unknown ETL recovery reference evidence.")
    source=manifest["sourceRevision"];assets=manifest["assets"]
    if len(source)!=40 or any(c not in "0123456789abcdef" for c in source):raise ValueError("The ETL reference lacks an exact source.")
    expected_cases={"direct-success":("BpmnModel",False),"direct-business":("BpmnModel",True)} if direct else {"sequence":("Sequence",False),"flowchart":("Flowchart",False),"worker-business":("RunEtl",True)}
    names={"before.json","recovered.json","finished.json","retained.json","before-wire.json","finished-wire.json","retained-wire.json","safe-errors.json","process.json"}
    names|={phase+"-"+store+".sqlite.gz" for phase in ("before","finished") for store in ("elsa","etl-runtime","etl-design")}
    if direct:names|={phase+"-checked-model.json" for phase in ("before","finished","retained")}
    declared={asset["path"] for asset in assets}
    if len(declared)!=len(assets) or declared!={case+"/"+name for case in expected_cases for name in names}:raise ValueError("The ETL recovery bundle lacks its exact actual assets.")
    for asset in assets:
        if q.digest(q.evidence_file(root,asset["path"]))!=asset["sha256"]:raise ValueError("An actual ETL recovery asset differs.")
    results=[]
    for case,(parent,business_error) in expected_cases.items():
        def read(name):return json.loads(q.evidence_file(root,case+"/"+name).read_text())
        process=read("process.json");products=process["products"]
        if (process["sourceRevision"]!=source or process["parent"]!=parent or process["businessError"] is not business_error or process["predecessorExitCode"]!=-9 or
                [item["role"] for item in process["processes"]]!=["http-fixture","workbench","workbench","workbench"] or len({item["processId"] for item in process["processes"]})!=4):raise ValueError("The actual ETL reference lacks its distinct process-loss/restart provenance.")
        if products["sourceCommit"]!=source or products["metadataOnly"] is not True or len(products["actualProductVersions"])!=(12 if direct else 6) or any(not item["productVersion"].endswith("+"+source) for item in products["actualProductVersions"]):raise ValueError("The actual Workbench and ETL products differ from their declared source.")
        before=read("before.json");finished=read("finished.json");recovered=read("recovered.json")
        before_wire=read("before-wire.json");wire=read("finished-wire.json")
        bridge=model_bridge(read,process,before,finished,business_error) if direct else None
        if finished!=read("retained.json") or wire!=read("retained-wire.json"):raise ValueError("The terminal native/ETL views or physical effects changed after another process.")
        identity=process["workflowExecutionId"];run_id=process["runId"];selection=process["selection"]
        old={node(item):item for item in before["native"]["activities"]};current={node(item):item for item in finished["native"]["activities"]}
        if before["native"]["instance"]["status"]!="Running" or before["run"]["status"]!="running" or "after" in old or old["etl"]["status"]!="Suspended":raise ValueError("The actual predecessor was not held at the real RunEtl wait.")
        if current["etl"]["activityExecutionId"]!=old["etl"]["activityExecutionId"] or len(current)!=(1 if parent in ("RunEtl","BpmnModel") else 3) or any(item["status"]!="Completed" for item in current.values()) or current["etl"]["outcomeNames"]!=["BusinessError" if business_error else "Done"]:raise ValueError("The actual native continuation changed its invocation or typed outcome.")
        if finished["native"]["instance"]["status"]!="Completed" or finished["native"]["incidents"]:raise ValueError("The actual native parent did not complete without incidents.")
        pin={name:before["native"]["instance"][name] for name in ("artifactId","artifactHash","artifactVersion","definitionId","definitionVersionId")}
        for view in (before,recovered,finished):
            if (view["native"]["instance"]["workflowExecutionId"]!=identity or view["run"]["runId"]!=run_id or view["run"]["selection"]!=selection or
                    any(view["native"]["instance"][name]!=value for name,value in pin.items())):raise ValueError("An actual ETL/native identity or full pin changed on recovery.")
        retry=next((item["http"]["fetch"] for item in before["partitions"]["items"] if item["http"]["fetch"] and item["http"]["fetch"]["lastStatusCode"]==429),None)
        if retry is None or retry["phase"]!="waiting" or retry["attemptCount"]!=1 or datetime.fromisoformat(retry["notBefore"])<=datetime.fromisoformat(process["cutAtUtc"]):raise ValueError("The actual crash cut lacks its original future 429 retry.")
        if not any(item["http"]["exhausted"] and item["owedBatch"] is None for item in before["partitions"]["items"]):raise ValueError("The sibling had not completed before the actual crash cut.")
        accepted,rejected=(3,1) if business_error else (4,0);outcome="businessError" if business_error else "succeeded"
        result=finished["run"];rows=finished["partitions"]["items"]
        if (result["formatVersion"]!=2 or result["status"]!=outcome or result["terminalResult"]["outcome"]!=outcome or result["acceptedRecords"]!=accepted or result["rejectedRecords"]!=rejected or result["filteredRecords"]!=0 or
                result["outstandingRecords"]!=0 or result["outstandingBytes"]!=0 or len(rows)!=2 or any(not item["http"]["exhausted"] or item["owedBatch"] is not None or item["http"]["pageSequence"]!=1 for item in rows)):raise ValueError("The actual ETL terminal or partition accounting differs.")
        if business_error and result["terminalResult"]["diagnostic"]["code"]!="etl.rows-rejected":raise ValueError("The actual ETL business-error key differs.")
        if wire["overflow"] or before_wire["overflow"]:raise ValueError("The physical HTTP observations exceeded their bounds.")
        earlier=[item for item in before_wire["requests"] if item.get("shard")=="shard-b"];later=[item for item in wire["requests"] if item.get("shard")=="shard-b"]
        if len(earlier)!=1 or len(later)!=3 or [item["status"] for item in later]!=[429,200,200] or later[0]!=earlier[0] or later[1]["receivedAtMs"]-later[0]["receivedAtMs"]<20000:raise ValueError("The physical source retried early or changed its saved page sequence.")
        records=[record for post in wire["outputPosts"] for record in post["records"]];expected_records={1:"Ada",2:"Grace",3:"Linus"}
        if not business_error:expected_records[4]="Margaret"
        if {item["id"]:item["name"] for item in records}!=expected_records or any(not post["hasIdempotencyKey"] for post in wire["outputPosts"]):raise ValueError("The physical HTTP receiver lacks the actual mapped effects.")
        errors=read("safe-errors.json")
        if errors!=([{"recordOrdinal":1,"code":"etl.invalid-value","field":"id"}] if business_error else []):raise ValueError("The actual safe-error file output differs.")
        invocation=None;native_bookmark=None
        for phase,stores in (("before",process["beforeStores"]),("finished",process["finishedStores"])):
            for store,snapshot in stores.items():
                file=q.evidence_file(root,case+"/"+snapshot["file"]);raw=gzip.decompress(file.read_bytes())
                if q.digest(file)!=snapshot["gzipSha256"] or hashlib.sha256(raw).hexdigest()!=snapshot["databaseSha256"] or snapshot["observer"]["snapshotSha256"]!=snapshot["databaseSha256"] or snapshot["observer"]["readOnly"] is not True:raise ValueError("A provider-observed ETL snapshot differs from its actual bytes.")
                with tempfile.TemporaryDirectory(prefix="elsa-etl-verify-") as temporary:
                    database=Path(temporary)/"snapshot.sqlite";database.write_bytes(raw)
                    with closing(sqlite3.connect(database.as_uri()+"?mode=ro&immutable=1",uri=True)) as connection:
                        if connection.execute("PRAGMA integrity_check").fetchall()!=[("ok",)]:raise ValueError("An actual ETL snapshot failed integrity verification.")
                        if store=="elsa":
                            if bridge is not None:physical_model_bridge(connection,phase,*bridge)
                            row=connection.execute("SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?",(key(identity),)).fetchone()
                            if row is None or row[0]!=(1 if phase=="before" else 3) or json.loads(row[1])["pinnedExecutable"]!={name:key(value) for name,value in pin.items()}:raise ValueError("The physical native ETL workflow lost its exact pin or state.")
                            bookmarks=connection.execute("SELECT BookmarkId FROM elsa_runtime_bookmark_state WHERE WorkflowExecutionId=?",(identity,)).fetchall()
                            if len(bookmarks)!=(1 if phase=="before" else 0):raise ValueError("The physical RunEtl wait was recreated or not consumed.")
                            if phase=="before":native_bookmark=bookmarks[0][0]
                        if store=="etl-runtime":
                            row=connection.execute("SELECT InvocationJson,Status,AcceptedRecords,RejectedRecords,OutstandingRecords,OutstandingBytes FROM elsa_etl_runtime_runs WHERE RunId=?",(key(run_id),)).fetchone()
                            if row is None:raise ValueError("The physical ETL run is absent.")
                            current_invocation=json.loads(row[0]);address=current_invocation["resultAddress"]
                            if (decoded(current_invocation["scope"]["tenantId"])!="default" or decoded(current_invocation["scope"]["persistencePartition"])!="default" or
                                    decoded(current_invocation["runId"])!=run_id or decoded(address["workflowExecutionId"])!=identity or decoded(address["activityExecutionId"])!=old["etl"]["activityExecutionId"] or
                                    decoded(address["executableId"])!=pin["artifactId"] or decoded(address["executableHash"])!=pin["artifactHash"]):raise ValueError("The physical ETL result address does not name its real native invocation.")
                            if invocation is None:invocation=current_invocation
                            if invocation!=current_invocation:raise ValueError("The immutable ETL invocation changed after recovery.")
                            version=process["etlPublication"]["version"];artifact=current_invocation["artifact"]
                            if artifact["formatVersion"]!=2 or artifact["definitionVersion"]!=version["definitionVersion"] or decoded(artifact["definitionId"])!=version["definitionId"] or decoded(artifact["contentHash"])!=version["contentHash"]:raise ValueError("The physical ETL artifact lost its exact published pin.")
                            terminal=connection.execute("SELECT Outcome FROM elsa_etl_runtime_terminals").fetchall();outbox=connection.execute("SELECT Attempts,Disposition,AcknowledgedAtUtcTicks FROM elsa_etl_runtime_outbox").fetchall()
                            if phase=="before" and (terminal or outbox or row[1]!=1):raise ValueError("The actual retry cut already carried a terminal result.")
                            if phase=="finished" and (row[1]!=(3 if business_error else 2) or row[2:]!=(accepted,rejected,0,0) or terminal!=[(1 if business_error else 0,)] or len(outbox)!=1 or outbox[0][0]<1 or outbox[0][1] is None or outbox[0][2] is None):raise ValueError("The physical ETL terminal or owed native result did not settle.")
        if (native_bookmark!=decoded(invocation["resultAddress"]["bookmarkId"]) or native_bookmark!=decoded(invocation["resultAddress"]["registrationId"]) or
                decoded(invocation["resultAddress"]["nodeId"])!=old["etl"]["executableNodeId"]):raise ValueError("The ETL result address did not retain its actual native bookmark.")
        results.append({"case":case,"acceptedRecords":accepted,"rejectedRecords":rejected,"physicalOutputRecords":len(records),"businessErrorPreserved":business_error})
    result={"actualReferenceJourneys":results,"wholeStoresVerified":len(expected_cases)*6,"sourceRevision":source,"directBpmnClosedWorkerIntegrationProved":direct,
        "runBpmnEtlIntegrationProved":False,"modelHeldFaultRetryProved":False}
    if not direct:result["directOrRunBpmnEtlIntegrationProved"]=False
    return result


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("--manifest",type=Path,required=True)
    print(json.dumps(verify(parser.parse_args().manifest.resolve(strict=True)),indent=2))
