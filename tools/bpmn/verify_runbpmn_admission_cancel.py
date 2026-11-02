#!/usr/bin/env python3
"""Verify actual root cancellation before first BPMN model admission using closed stores only."""
import argparse
from contextlib import closing
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile

_spec=importlib.util.spec_from_file_location("runbpmn_recovery",Path(__file__).with_name("verify_runbpmn_recovery.py"))
b=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(b)

def verify(path):
    manifest=json.loads(path.read_text());root=path.parent;source=manifest["sourceRevision"]
    names={"case.json","prepared-checked.json","cancelled-checked.json","completed-checked.json","retained-checked.json"} | {
        phase+"-"+store+".sqlite.gz" for phase in ("cancelled","completed") for store in ("runtime","design")}
    assets=manifest["assets"]
    if (manifest.get("schemaVersion")!=1 or manifest.get("kind")!="actual-runbpmn-cancellation-before-admission" or len(source)!=40 or
            len(assets)!=9 or {item["path"] for item in assets}!=names):raise ValueError("The admission-cancel bundle lacks its exact actual assets.")
    for item in assets:
        if b.q.digest(b.q.evidence_file(root,item["path"]))!=item["sha256"]:raise ValueError("An actual admission-cancel asset differs.")
    def read(name):return json.loads(b.q.evidence_file(root,name).read_text())
    case=read("case.json");prepared=read("prepared-checked.json");cancelled=read("cancelled-checked.json");final=read("completed-checked.json")
    if (case["sourceRevision"]!=source or case["kind"]!=manifest["kind"] or case["predecessorExit"]!=137 or
            len({case["firstProcess"],case["secondProcess"],case["thirdProcess"]})!=3 or
            case["preparedRecoverySweepSeconds"]!=300 or case["recoveredSweepSeconds"]!=0.2):
        raise ValueError("The actual cancellation lacks its normal configured three-process loss/recovery provenance.")
    products=case["binaries"]
    if (not b.known_products(products) or
            any(not item["productVersion"].endswith("+"+source) for item in products if item["file"].startswith("Elsa.")) or
            any(not item["productVersion"].startswith("0.5.0+") for item in products if item["file"].startswith("Bpmn."))):
        raise ValueError("The actual cancellation products differ from their source/library release.")
    if final!=read("retained-checked.json"):raise ValueError("The settled cancellation changed in another process.")
    for checked in (prepared,cancelled,final):
        if not checked["readOnly"] or checked["queuedModelInputs"]:raise ValueError("The checked cancellation phase has unproved queued model work.")
        if checked["nativeParent"]["workflowExecutionId"]!=case["nativeExecutionId"] or checked["nativeParent"]["pinnedExecutable"]!=b.pin(case["publication"]):
            raise ValueError("The cancellation lost the real native parent's full identity/pin.")
    if prepared["model"] is not None or cancelled["model"] is not None or prepared["inputs"] or cancelled["inputs"]:
        raise ValueError("The model already existed before the actual native cancellation.")
    before_activity,=prepared["activities"];cancelled_activity,=cancelled["activities"]
    if (prepared["nativeParent"]["status"]!=1 or before_activity["status"]!=3 or cancelled["nativeParent"]["status"]!=5 or
            cancelled_activity["status"]!=6 or cancelled_activity["invocationId"]!=before_activity["invocationId"] or
            len(prepared["bookmarks"])!=1 or cancelled["bookmarks"]):raise ValueError("The native wait/cancellation boundary was not actually observed.")
    start,=prepared["sources"];origin=b.invocation(start["intent"]["payload"])
    encoded=start["intent"]["payload"];wait=case["actualWait"]
    if (start["intent"]["kind"]!=b.START or start["status"]!=0 or origin["executionId"]!=case["modelExecutionId"] or
            origin["nativeExecutionId"]!=case["nativeExecutionId"] or origin["nativeInvocationId"]!=before_activity["invocationId"] or
            origin["registrationId"]!=wait["identity"]["bookmarkId"] or wait["stimulusHash"]!=case["modelExecutionId"] or
            prepared["bookmarks"][0]["bookmarkId"]!=origin["registrationId"] or
            origin["nativeAttemptId"]!=before_activity["privateState"]["producedByAttemptId"] or
            before_activity["privateState"]["value"]["inlineValue"]["Invocation"]!=encoded or
            origin["model"]["executable"]!=case["published"]["executable"] or origin["nativeExecutable"]!=b.pin(case["publication"])):
        raise ValueError("The owed start lacks its original real native wait and immutable model address.")
    owed={item["intent"]["kind"]:item for item in cancelled["sources"]}
    if (len(owed)!=2 or set(owed)!={b.START,b.CANCEL} or owed[b.START]!=start or
            any(item["status"]!=0 or item["intent"]["payload"]!=encoded for item in owed.values())):
        raise ValueError("Native cancellation lost or delivered the original owed start/cancel sources before the crash.")
    model=final["model"];state=model["payload"]["state"]
    if (model["executionId"]!=case["modelExecutionId"] or model["executable"]!=case["published"]["executable"] or
            model["materialIncarnationId"]!=case["published"]["incarnationId"] or model["lifecycle"]!=3 or model["revision"]!=2 or
            model["terminalResult"]["status"]!="Cancelled" or model["payload"]["formatVersion"]!=3 or model["payload"]["nativeParent"]["invocation"]!=encoded or
            state["sequence"]!=0 or state["tokens"] or state["activeWork"] or state["diagnostics"] or model["payload"]["work"] or
            final["nativeParent"]["status"]!=5 or final["bookmarks"]):raise ValueError("The recovered cancellation started semantics or lost its terminal origin.")
    settled=[item for item in final["inputs"] if item["inputKind"]=="native-parent-settled"]
    if (len(settled)!=1 or settled[0]["acceptedRevision"]!=2 or settled[0]["result"]["status"]!="Accepted" or
            settled[0]["invocationId"]!=origin["nativeInvocationId"] or settled[0]["attemptId"]!=origin["nativeAttemptId"]):
        raise ValueError("The cancelled parent lacks its exact model settlement receipt.")
    sources={item["intent"]["kind"]:item for item in final["sources"]}
    if (len(sources)!=3 or set(sources)!={b.START,b.CANCEL,b.RESULT} or any(item["status"]!=2 for item in sources.values()) or
            sources[b.START]["intent"]!=start["intent"] or sources[b.CANCEL]["intent"]!=owed[b.CANCEL]["intent"] or
            sources[b.RESULT]["intent"]["payload"]!={"FormatVersion":1,"Invocation":encoded,"Result":model["terminalResult"]} or
            b.timestamp(sources[b.RESULT]["deliveredAt"])<b.timestamp(settled[0]["acceptedAt"])):
        raise ValueError("The cancelled model/native sources acknowledged without exact settlement.")
    snapshots=case["snapshots"]
    if len(snapshots)!=4 or {item["file"] for item in snapshots}!={name for name in names if name.endswith(".sqlite.gz")}:
        raise ValueError("The actual cancellation lacks its four complete stores.")
    for snapshot in snapshots:
        file=b.q.evidence_file(root,snapshot["file"]);raw=gzip.decompress(file.read_bytes())
        if b.q.digest(file)!=snapshot["gzipSha256"] or hashlib.sha256(raw).hexdigest()!=snapshot["databaseSha256"]:raise ValueError("An actual cancellation store differs.")
        with tempfile.TemporaryDirectory(prefix="elsa-runbpmn-admission-verify-") as directory:
            copy=Path(directory)/"closed.sqlite";copy.write_bytes(raw)
            with closing(sqlite3.connect(copy.as_uri()+"?mode=ro&immutable=1",uri=True)) as connection:
                if connection.execute("PRAGMA integrity_check").fetchall()!=[("ok",)]:raise ValueError("An actual cancellation store failed integrity.")
                if "runtime" not in snapshot["file"]:continue
                if connection.execute("SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state").fetchone()[0]!=1:
                    raise ValueError("The actual cancellation has an extra native carrier/header.")
                if connection.execute("SELECT COUNT(*) FROM elsa_runtime_scheduler_work_item WHERE WorkflowExecutionId=?",(b.key(model["executionId"]),)).fetchone()[0]!=0:
                    raise ValueError("The physical cancellation phase retains hidden queued model work.")
                if snapshot["file"].startswith("completed"):
                    b.physical(connection,final,{"cancel":True},"completed",origin)
                else:
                    if connection.execute("SELECT COUNT(*) FROM elsa_runtime_model_execution").fetchone()[0]!=0 or connection.execute("SELECT COUNT(*) FROM elsa_runtime_model_input").fetchone()[0]!=0:
                        raise ValueError("A physical model/receipt already existed before admission cancellation.")
                    row=connection.execute("SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state").fetchone()
                    if row[0]!=5 or json.loads(row[1])["pinnedExecutable"]!=b.encoded_pin(b.pin(case["publication"])):
                        raise ValueError("The physical parent was not cancelled at the pre-admission cut.")
                    activities=connection.execute("SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state").fetchall()
                    if (len(activities)!=1 or activities[0][0]!="Cancelled" or
                            json.loads(activities[0][1])["execution"]["activityExecutionId"]!=b.key(origin["nativeInvocationId"]) or
                            connection.execute("SELECT COUNT(*) FROM elsa_runtime_bookmark_state").fetchone()[0]!=0):
                        raise ValueError("The physical native invocation did not retire its original wait at cancellation.")
                    stored={b.text(json.loads(row[0])["outboxItemId"]):json.loads(row[0]) for row in connection.execute("SELECT ContentJson FROM elsa_runtime_post_commit_outbox")}
                    for item in cancelled["sources"]:
                        value=stored.get(item["outboxItemId"])
                        if value is None or value["intent"]["payload"]!=encoded or value["deliveredAt"] is not None:
                            raise ValueError("The physical pre-admission source lost its owed immutable address.")
    return {"sourceRevision":source,"actualJourneys":1,"workbenchProcesses":3,"wholeStoresVerified":4,
        "rootCancellationBeforeFirstModelAdmissionProved":True,"interpreterStarted":False,
        "pendingParentResultCrashProved":False,"etlEmbeddingProved":False,"upgradedHostOrStudioProved":False}

if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("--manifest",type=Path,required=True)
    print(json.dumps(verify(parser.parse_args().manifest.resolve(strict=True)),indent=2))
