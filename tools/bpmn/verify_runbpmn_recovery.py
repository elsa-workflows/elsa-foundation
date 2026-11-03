#!/usr/bin/env python3
"""Verify closed actual RunBpmn human-work recovery captures without starting hosts or changing stores."""
import argparse
import base64
from contextlib import closing
from copy import deepcopy
from datetime import datetime
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sqlite3
import tempfile

_spec=importlib.util.spec_from_file_location("bpmn_hosting",Path(__file__).with_name("qualify-hosting.py"))
q=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(q)
START="Elsa.Bpmn.StartEmbedded.v1";CANCEL="Elsa.Bpmn.CancelEmbedded.v1";RESULT="Elsa.Bpmn.ResumeNativeParent.v1"
ACTIVITY="Elsa.Activities.Bpmn.Activities.RunBpmn"
ACTIVITY_STATUSES=("Scheduled","Running","Waiting","Suspended","Completed","Faulted","Cancelled","Recovered","Superseded")
PRODUCTS={"Elsa.Workbench.dll","Elsa.Workflows.Runtime.Core.dll","Elsa.Workflows.Runtime.dll",
    "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll","Elsa.Activities.Bpmn.Core.dll","Elsa.Activities.Bpmn.dll",
    "Elsa.Activities.Bpmn.Api.dll","Elsa.Workflows.Publishing.Core.dll","Elsa.Workflows.Publishing.dll","Bpmn.Model.dll","Bpmn.Semantics.dll"}

# The authored-publication host records its coupled Design assemblies as well.
# Preserve the archived 11-product inventory and admit only the complete current 15-product set.
AUTHORING_PRODUCTS={"Elsa.Workflows.Design.Core.dll","Elsa.Workflows.Design.Api.dll",
    "Elsa.Workflows.Design.Persistence.EntityFrameworkCore.dll","Elsa.Persistence.EntityFramework.dll"}
def known_products(products):
    names={item["file"] for item in products}
    return len(products)==len(names) and (names==PRODUCTS or names==PRODUCTS|AUTHORING_PRODUCTS)

def key(value):return base64.b64encode(value.encode("utf-16le","surrogatepass")).decode()
def text(value):return base64.b64decode(value,validate=True).decode("utf-16le","surrogatepass")
def invocation(value):
    if isinstance(value,str):
        if not value.startswith("u16:"):raise ValueError("The native invocation lost its lossless text encoding.")
        return text(value[4:])
    if isinstance(value,dict):return {k:invocation(v) for k,v in value.items()}
    if isinstance(value,list):return [invocation(v) for v in value]
    return value
def pin(value):return {name:value[name] for name in ("artifactId","artifactHash","artifactVersion","definitionId","definitionVersionId")}
def encoded_pin(value):return {k:key(v) for k,v in value.items()}
def timestamp(value):
    match=re.fullmatch(r"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:\+00:00|Z)",value)
    if match is None:raise ValueError("The capture timestamp is not a UTC runtime timestamp.")
    # .NET emits 0-7 fractional digits; preserve all ticks on Python versions whose
    # fromisoformat accepts only three or six fractional digits.
    return datetime.strptime(match[1],"%Y-%m-%dT%H:%M:%S"),(match[2] or "").ljust(7,"0")

def semantic_state(model):
    value=deepcopy(model["payload"]["state"])
    # This is the single identity field that differs in the captured released-interpreter states.
    # Other fields, diagnostics, order, token/element/flow identities and outcomes must compare exactly.
    for token in value["tokens"]:
        if token["producingWorkHandle"]==model["executionId"]:token["producingWorkHandle"]="$model"
    return value

def physical(connection,checked,case,phase,origin):
    model=checked["model"];native=checked["nativeParent"]
    row=connection.execute("SELECT Lifecycle,Revision,ContentJson FROM elsa_runtime_model_execution WHERE ExecutionId=?",(key(model["executionId"]),)).fetchone()
    stored=json.loads(row[2]) if row else None
    if (row is None or row[:2]!=(model["lifecycle"],model["revision"]) or stored["payload"]!=model["payload"] or stored["terminalResult"]!=model["terminalResult"] or
            stored["executable"]!=encoded_pin(model["executable"]) or stored["materialIncarnationId"]!=key(model["materialIncarnationId"])):
        raise ValueError("The physical embedded model differs from its checked header/state.")
    expected_models=1 if case["cancel"] else 2
    if connection.execute("SELECT COUNT(*) FROM elsa_runtime_model_execution").fetchone()[0]!=expected_models:raise ValueError("The actual direct/embedded model count differs.")
    if not case["cancel"]:
        direct=checked["directCounterpart"]
        row=connection.execute("SELECT ContentJson FROM elsa_runtime_model_execution WHERE ExecutionId=?",(key(direct["executionId"]),)).fetchone()
        if row is None or json.loads(row[0])["payload"]!=direct["payload"] or json.loads(row[0])["executable"]!=encoded_pin(direct["executable"]):
            raise ValueError("The actual direct counterpart differs from its physical semantic state/pin.")
    if connection.execute("SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state").fetchone()[0]!=1:raise ValueError("The actual model has an extra native carrier/header.")
    row=connection.execute("SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?",(key(native["workflowExecutionId"]),)).fetchone()
    if row is None or row[0]!=native["status"] or json.loads(row[1])["pinnedExecutable"]!=encoded_pin(native["pinnedExecutable"]):raise ValueError("The physical native parent lost its full pin or terminal.")
    for activity in checked["activities"]:
        row=connection.execute("SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=? AND ActivityExecutionId=?",
            (key(native["workflowExecutionId"]),key(activity["invocationId"]))).fetchone()
        value=json.loads(row[1]) if row else None
        if row is None or row[0]!=ACTIVITY_STATUSES[activity["status"]] or value["execution"]["executableNodeId"]!=key(activity["execution"]["executableNodeId"]):raise ValueError("The actual native activity differs from its physical identity/status.")
        if activity["execution"]["activityType"]==ACTIVITY:
            if phase=="waiting":
                if value["privateState"]["value"]["inlineValue"]!=activity["privateState"]["value"]["inlineValue"]:raise ValueError("The physical native wait lost its frozen model invocation.")
                row=connection.execute("SELECT ContentJson FROM elsa_runtime_bookmark_state WHERE WorkflowExecutionId=? AND BookmarkId=?",
                    (native["workflowExecutionId"],origin["registrationId"])).fetchone()
                if row is None:raise ValueError("The actual RunBpmn registration has no physical bookmark.")
                bookmark=json.loads(row[0])
                # Bookmark storage retains its established plain-text identity layout.
                if bookmark["activityExecutionId"]!=origin["nativeInvocationId"] or bookmark["stimulusHash"]!=model["executionId"]:raise ValueError("The physical bookmark addresses a different native/model invocation.")
            elif not case["cancel"]:
                completion=value["completion"]
                if completion["result"]["inlineValue"]!=activity["completion"]["result"]["inlineValue"] or completion["outcomeKey"]!=key("Done"):
                    raise ValueError("The model result differs from its authoritative native completion.")
    rows=connection.execute("SELECT ContentJson FROM elsa_runtime_model_input WHERE ExecutionId=?",(key(model["executionId"]),)).fetchall()
    inputs={text(json.loads(row[0])["inputId"]):json.loads(row[0]) for row in rows}
    if set(inputs)!={item["inputId"] for item in checked["inputs"]}:raise ValueError("The physical model input set differs.")
    for receipt in checked["inputs"]:
        value=inputs[receipt["inputId"]]
        if (value["contentFingerprint"]!=key(receipt["contentFingerprint"]) or value["inputKind"]!=key(receipt["inputKind"]) or
                value["acceptedRevision"]!=receipt["acceptedRevision"] or value["result"]!=receipt["result"]):raise ValueError("The physical model consumption receipt lost its exact identity.")
    sources={text(json.loads(row[0])["outboxItemId"]):json.loads(row[0]) for row in connection.execute("SELECT ContentJson FROM elsa_runtime_post_commit_outbox")}
    for source in checked["sources"]:
        value=sources.get(source["outboxItemId"])
        if value is None or value["intent"]["payload"]!=source["intent"]["payload"] or value["deliveredAt"]!=source["deliveredAt"]:
            raise ValueError("The physical native/model source differs from its checked delivery proof.")
    material_row=connection.execute("SELECT ContentJson,IncarnationId FROM elsa_runtime_workflow_executable WHERE ArtifactId=?",(key(model["executable"]["artifactId"]),)).fetchone()
    if material_row is None or material_row[1]!=model["materialIncarnationId"]:raise ValueError("The physical model material incarnation changed.")
    native_material=connection.execute("SELECT ContentJson FROM elsa_runtime_workflow_executable WHERE ArtifactId=?",(key(native["pinnedExecutable"]["artifactId"]),)).fetchone()
    dependencies=json.loads(native_material[0])["dependencies"]
    if not any(edge["artifactId"]==key(model["executable"]["artifactId"]) and edge["artifactHash"]==key(model["executable"]["artifactHash"]) for edge in dependencies):
        raise ValueError("The physical native publication lost its model dependency.")

def verify(path):
    manifest=json.loads(path.read_text());root=path.parent;source=manifest["sourceRevision"]
    if manifest.get("schemaVersion")!=1 or manifest.get("kind")!="actual-native-runbpmn-human-recovery" or len(source)!=40:raise ValueError("Unknown RunBpmn evidence or source.")
    cases={parent.lower()+"-"+mode:(parent,mode=="cancel") for parent in ("Root","Sequence","Flowchart","CompensationScope") for mode in ("complete","cancel")}
    names={"case.json","waiting-checked.json","completed-checked.json","retained-checked.json"}|{phase+"-"+store+".sqlite.gz" for phase in ("waiting","completed") for store in ("runtime","design")}
    assets=manifest["assets"]
    if len(assets)!=len({x["path"] for x in assets}) or {x["path"] for x in assets}!={case+"/"+name for case in cases for name in names}:raise ValueError("The RunBpmn bundle lacks its exact actual assets.")
    for asset in assets:
        if q.digest(q.evidence_file(root,asset["path"]))!=asset["sha256"]:raise ValueError("A RunBpmn capture asset differs.")
    results=[]
    for name,(parent,cancel) in cases.items():
        def read(file):return json.loads(q.evidence_file(root,name+"/"+file).read_text())
        case=read("case.json");before=read("waiting-checked.json");after=read("completed-checked.json")
        snapshots=case["snapshots"]
        if len(snapshots)!=4 or {item["file"] for item in snapshots}!={file for file in names if file.endswith(".sqlite.gz")}:
            raise ValueError("The actual journey lacks its four complete closed store snapshots.")
        if (case["sourceRevision"]!=source or case["parent"]!=parent or case["cancel"] is not cancel or case["predecessorExit"]!=137 or
                len({case["firstProcess"],case["secondProcess"],case["thirdProcess"]})!=3):raise ValueError("The actual embedding lacks its three-process loss/recovery provenance.")
        products=case["binaries"]
        if not known_products(products) or any(not item["productVersion"].endswith("+"+source) for item in products if item["file"].startswith("Elsa.")) or any(not item["productVersion"].startswith("0.5.0+") for item in products if item["file"].startswith("Bpmn.")):
            raise ValueError("The actual host/runtime/BPMN/publishing products differ from their source.")
        if after!=read("retained-checked.json"):raise ValueError("The actual native/model terminal proof changed after another process.")
        model=before["model"];final=after["model"];native=before["nativeParent"]
        encoded=model["payload"]["nativeParent"]["invocation"];origin=invocation(encoded)
        if (not before["readOnly"] or not after["readOnly"] or model["payload"]["formatVersion"]!=3 or model["lifecycle"]!=0 or model["revision"]!=1 or
                model["executionId"]!=case["modelExecutionId"] or origin["executionId"]!=model["executionId"] or origin["nativeExecutionId"]!=case["nativeExecutionId"] or
                native["workflowExecutionId"]!=case["nativeExecutionId"] or native["pinnedExecutable"]!=pin(case["publication"]) or origin["nativeExecutable"]!=native["pinnedExecutable"] or
                origin["model"]["executable"]!=case["published"]["executable"] or origin["model"]["materialIncarnationId"]!=model["materialIncarnationId"]):
            raise ValueError("The real native/model invocation lost its full retained pin or address.")
        activity,=[item for item in before["activities"] if item["execution"]["activityType"]==ACTIVITY]
        if (activity["invocationId"]!=origin["nativeInvocationId"] or activity["status"]!=3 or activity["privateState"]["producedByAttemptId"]!=origin["nativeAttemptId"] or
                activity["privateState"]["value"]["inlineValue"]["Invocation"]!=encoded or len(before["bookmarks"])!=1 or
                before["bookmarks"][0]["bookmarkId"]!=origin["registrationId"] or before["bookmarks"][0]["stimulusHash"]!=model["executionId"]):
            raise ValueError("The native wait is not the actual frozen registration for this model.")
        if (final["payload"]["nativeParent"]["invocation"]!=encoded or final["executable"]!=model["executable"] or final["materialIncarnationId"]!=model["materialIncarnationId"] or
                final["authority"]!=model["authority"] or final["authority"]!=after["nativeParent"]["authority"] or final["lifecycle"]!=3 or final["revision"]!=3 or
                final["terminalResult"]["status"]!=("Cancelled" if cancel else "Completed") or after["nativeParent"]["status"]!=(5 if cancel else 3) or after["bookmarks"]):
            raise ValueError("The actual native/model terminal or immutable origin changed.")
        current,=[item for item in after["activities"] if item["execution"]["activityType"]==ACTIVITY]
        if current["invocationId"]!=activity["invocationId"] or current["status"]!=(6 if cancel else 4):raise ValueError("The actual native invocation was recreated or has the wrong outcome.")
        if not cancel and (current["completion"]["result"]["inlineValue"]!={"executionId":model["executionId"],"result":final["terminalResult"]} or
                not any(receipt["status"]==1 and receipt["registrationId"]==origin["registrationId"] and receipt["providerId"]=="elsa.bpmn.native-parent.v1" for receipt in current["triggerDeliveries"])):
            raise ValueError("The native result lacks its exact typed consumed registration.")
        successors=[item for item in after["activities"] if item["execution"]["executableNodeId"]=="after"]
        if cancel and successors or not cancel and parent!="Root" and (len(successors)!=1 or successors[0]["status"]!=4):raise ValueError("Native parent continuation differs from completion/cancellation.")
        receipts=after["inputs"];settlements=[item for item in receipts if item["inputKind"]=="native-parent-settled"]
        if len(settlements)!=1:raise ValueError("The native parent lacks its exact model settlement receipt.")
        settled=settlements[0]
        if settled["result"]["status"]!="Accepted" or settled["invocationId"]!=origin["nativeInvocationId"] or settled["attemptId"]!=origin["nativeAttemptId"] or settled["acceptedRevision"]!=3:
            raise ValueError("The native parent lacks its exact model settlement receipt.")
        sources={item["intent"]["kind"]:item for item in after["sources"]}
        if (set(sources)!=({START,CANCEL,RESULT} if cancel else {START,RESULT}) or any(item["status"]!=2 for item in sources.values()) or
                sources[START]["intent"]["payload"]!=encoded or sources[RESULT]["intent"]["payload"]!={"FormatVersion":1,"Invocation":encoded,"Result":final["terminalResult"]} or
                timestamp(sources[RESULT]["deliveredAt"])<timestamp(settled["acceptedAt"])):
            raise ValueError("The actual native/model source acknowledged without exact consumption proof.")
        if cancel:
            rejected,=[item for item in receipts if item["inputKind"]=="complete-human"]
            if rejected["result"]["code"]!="execution-terminal" or rejected["acceptedRevision"]!=3:raise ValueError("Late human completion advanced cancelled BPMN.")
        else:
            for checked in (before,after):
                direct=checked["directCounterpart"]
                if direct is None or direct["executionId"]!=case["directCounterpartId"] or direct["executable"]!=checked["model"]["executable"] or semantic_state(direct)!=semantic_state(checked["model"]):
                    raise ValueError("The actual direct and embedded semantic states differ beyond the declared host handle.")
        for snapshot in snapshots:
            file=q.evidence_file(root,name+"/"+snapshot["file"]);raw=gzip.decompress(file.read_bytes())
            if q.digest(file)!=snapshot["gzipSha256"] or hashlib.sha256(raw).hexdigest()!=snapshot["databaseSha256"]:raise ValueError("An actual complete store snapshot differs.")
            with tempfile.TemporaryDirectory(prefix="elsa-runbpmn-verify-") as temporary:
                file=Path(temporary)/"closed.sqlite";file.write_bytes(raw)
                with closing(sqlite3.connect(file.as_uri()+"?mode=ro&immutable=1",uri=True)) as connection:
                    if connection.execute("PRAGMA integrity_check").fetchall()!=[("ok",)]:raise ValueError("An actual store snapshot failed integrity.")
                    if "runtime" in snapshot["file"]:
                        phase="waiting" if snapshot["file"].startswith("waiting") else "completed"
                        physical(connection,before if phase=="waiting" else after,case,phase,origin)
        results.append({"parent":parent,"nativeCancelled":cancel,"nativeAndModelSettled":True,"directTraceCompared":not cancel})
    return {"sourceRevision":source,"actualJourneys":results,"wholeStoresVerified":32,"nativeProcesses":24,"humanModelEmbeddingProved":True,
        "etlEmbeddingProved":False,"pendingParentResultCrashProved":False,"cancellationBeforeAdmissionProved":False,"upgradedHostOrStudioProved":False}

if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("--manifest",type=Path,required=True)
    print(json.dumps(verify(parser.parse_args().manifest.resolve(strict=True)),indent=2))
