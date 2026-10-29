#!/usr/bin/env python3
"""Read-only verification of actual native ETL HTTP recovery reference journeys."""
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


def verify(path):
    manifest=json.loads(path.read_text());root=path.parent
    if manifest.get("schemaVersion")!=1 or manifest.get("kind")!="actual-native-etl-recovery-reference":raise ValueError("Unknown ETL recovery reference evidence.")
    source=manifest["sourceRevision"];assets=manifest["assets"]
    if len(source)!=40 or any(c not in "0123456789abcdef" for c in source):raise ValueError("The ETL reference lacks an exact source.")
    expected_cases={"sequence":("Sequence",False),"flowchart":("Flowchart",False),"worker-business":("RunEtl",True)}
    names={"before.json","recovered.json","finished.json","retained.json","before-wire.json","finished-wire.json","retained-wire.json","safe-errors.json","process.json"}
    names|={phase+"-"+store+".sqlite.gz" for phase in ("before","finished") for store in ("elsa","etl-runtime","etl-design")}
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
        if products["sourceCommit"]!=source or products["metadataOnly"] is not True or len(products["actualProductVersions"])!=6 or any(not item["productVersion"].endswith("+"+source) for item in products["actualProductVersions"]):raise ValueError("The actual Workbench and ETL products differ from their declared source.")
        before=read("before.json");finished=read("finished.json");recovered=read("recovered.json")
        before_wire=read("before-wire.json");wire=read("finished-wire.json")
        if finished!=read("retained.json") or wire!=read("retained-wire.json"):raise ValueError("The terminal native/ETL views or physical effects changed after another process.")
        identity=process["workflowExecutionId"];run_id=process["runId"];selection=process["selection"]
        old={node(item):item for item in before["native"]["activities"]};current={node(item):item for item in finished["native"]["activities"]}
        if before["native"]["instance"]["status"]!="Running" or before["run"]["status"]!="running" or "after" in old or old["etl"]["status"]!="Suspended":raise ValueError("The actual predecessor was not held at the real RunEtl wait.")
        if current["etl"]["activityExecutionId"]!=old["etl"]["activityExecutionId"] or len(current)!=(1 if parent=="RunEtl" else 3) or any(item["status"]!="Completed" for item in current.values()) or current["etl"]["outcomeNames"]!=["BusinessError" if business_error else "Done"]:raise ValueError("The actual native continuation changed its invocation or typed outcome.")
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
    return {"actualReferenceJourneys":results,"wholeStoresVerified":18,"sourceRevision":source,"directOrRunBpmnEtlIntegrationProved":False,"modelHeldFaultRetryProved":False}


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("--manifest",type=Path,required=True)
    print(json.dumps(verify(parser.parse_args().manifest.resolve(strict=True)),indent=2))
