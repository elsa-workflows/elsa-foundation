#!/usr/bin/env python3
"""Read-only verification of actual native and direct BPMN ETL HTTP recovery journeys."""
import argparse
import base64
from contextlib import closing
from copy import deepcopy
from datetime import datetime, timezone
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import re
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
def instant(value):
    match=re.fullmatch(r"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:\+00:00|Z)",value)
    if match is None:raise ValueError("The actual receipt timestamp is not UTC.")
    return datetime.strptime(match[1],"%Y-%m-%dT%H:%M:%S"),(match[2] or "").ljust(7,"0")


def encoded_tree(value):
    if isinstance(value,str):return key(value)
    if isinstance(value,dict):return {name:encoded_tree(item) for name,item in value.items()}
    if isinstance(value,list):return [encoded_tree(item) for item in value]
    return value


def model_bridge(read,process,before,finished,business_error,cancelled=False,model_cancel=False,embedded=False):
    technical=process.get('technicalFault') is True
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
    expected="Cancelled" if cancelled else "Faulted" if technical or business_error and process.get('businessErrorBoundary')!='matching' else "Completed"
    native_status=5 if cancelled else 4 if technical else 3
    if (last["model"]["lifecycle"]!=3 or last["model"]["revision"]!=((3 if model_cancel else 2)+int(embedded)) or last["model"]["payload"]["work"] or last["worker"]["status"]!=native_status or
            result["origin"]!=origin or result["status"]!=native_status or last["model"]["terminalResult"]["status"]!=expected or
            before["model"]["status"]!="Running" or finished["model"]["status"]!="TerminalSettled" or finished["model"]["result"]["status"]!=expected):
        raise ValueError("The actual model did not retain the native worker's typed terminal outcome.")
    if (receipt is None or receipt["inputId"]!=origin["resultInputId"] or receipt["inputKind"]!="native-result" or receipt["executionId"]!=model_id or
            receipt["invocationId"]!=origin["invocationId"] or receipt["attemptId"]!=origin["attemptId"] or receipt["acceptedRevision"]!=(3 if model_cancel else 2) or receipt["result"]["status"]!="Accepted" or
            source is None or source["status"]!=2 or source["stimulusDeliverySourceScope"]!=origin["scope"] or source["outboxItemId"]!=result["sourceOutboxItemId"] or source["intent"]["payload"]!=result or
            source["intent"]["kind"]!=origin["resultIntentKind"] or instant(source["deliveredAt"])<instant(receipt["acceptedAt"])):
        raise ValueError("The actual result was acknowledged without its exact model consumption proof.")
    completion=result["completion"]
    if cancelled:
        if completion is not None or result["faultCode"] is not None:raise ValueError("The actual cancelled worker was relabeled as completed or faulted.")
        return checked,origin,result
    if technical:
        if (completion is not None or result['faultCode']!='etl.snapshot-read-failed' or result.get('cancellationSources') is not None or
                last['model']['terminalResult'].get('errorCode') is not None or last['model']['terminalResult']['code']!='bpmn.work.faulted'):
            raise ValueError('The actual technical worker fault was relabeled as a business error or completion.')
        return checked,origin,result
    typed=completion["result"]
    if (completion["isRedacted"] or completion["resultType"]["alias"]!="Elsa.Etl.Workflows.Runtime.Models.RunEtlResult" or
            completion["outcomeKey"]!=("BusinessError" if business_error else "Done") or typed!={"runId":process["runId"],"outcome":1 if business_error else 0,
                "acceptedRecords":3 if business_error else 4,"rejectedRecords":1 if business_error else 0,"filteredRecords":0,"diagnosticCode":"etl.rows-rejected" if business_error else None}):
        raise ValueError("The actual typed RunEtl result was lost or contradicted its ETL terminal.")
    return checked,origin,result


def physical_business_error_boundary(connection,phase,checked,origin,mode,technical=False):
    model=checked[phase]['model']
    material,=[json.loads(row[0])['material'] for row in connection.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable')
        if json.loads(row[0]).get('material') and json.loads(row[0])['material']['identity']==encoded_tree(model['executable'])]
    process={'processId':'direct-etl','elements':[{'elementId':'start','elementType':'startEvent'},
        {'elementId':'service','elementType':'serviceTask','bindingRef':'pipeline'},{'elementId':'end','elementType':'endEvent'},
        {'elementId':'error','elementType':'boundaryEvent','attachedToRef':'service','cancelActivity':True,
            'eventDefinitions':[{'type':'error','properties':{'code':'etl.rows-rejected' if mode=='matching' else 'etl.other-error'}}]},
        {'elementId':'error-end','elementType':'endEvent'}],
        'sequenceFlows':[{'flowId':'to-service','sourceRef':'start','targetRef':'service'},
            {'flowId':'to-end','sourceRef':'service','targetRef':'end'},{'flowId':'caught-end','sourceRef':'error','targetRef':'error-end'}]}
    if material['definition']!={'formatVersion':2,'process':process,'humanBindings':[],
            'serviceBindings':[{'bindingId':'pipeline','executable':origin['workerExecutable'],'businessErrorCode':'etl.rows-rejected'}]}:
        raise ValueError('The physical model lost its declared coded business-error boundary or service binding.')
    if phase=='finished':
        terminal=model['terminalResult'];diagnostics=model['payload']['state']['diagnostics']
        catches=[d for d in diagnostics if d['elementId']=='error' and d['details'].get('errorCode')=='etl.rows-rejected' and d['details'].get('match')=='exact']
        if technical:
            if terminal['status']!='Faulted' or terminal.get('errorCode') is not None or terminal.get('code')!='bpmn.work.faulted' or catches:
                raise ValueError('The actual technical fault entered a coded business-error catch.')
        elif (mode=='matching' and (terminal['status']!='Completed' or len(catches)!=1) or
                mode=='different' and (terminal['status']!='Faulted' or terminal.get('errorCode')!='etl.rows-rejected' or terminal.get('code')!='bpmn.work.faulted' or catches)):
            raise ValueError('The actual interpreter lost its exact matched or unmatched business-error disposition.')


def physical_model_bridge(connection,phase,checked,origin,result,model_cancel=False,embedded=False,parent_settlement_owed=False):
    observed=checked[phase];model=observed["model"]
    row=connection.execute("SELECT Lifecycle,Revision,ContentJson FROM elsa_runtime_model_execution WHERE ExecutionId=?",(key(model["executionId"]),)).fetchone()
    stored=json.loads(row[2]) if row else None
    if (row is None or row[:2]!=(model["lifecycle"],model["revision"]) or stored["payload"]!=model["payload"] or
            stored["terminalResult"]!=model["terminalResult"] or stored["executable"]!=encoded_tree(model["executable"]) or stored["materialIncarnationId"]!=key(model["materialIncarnationId"])):
        raise ValueError("The physical model state differs from its checked reader.")
    worker=connection.execute("SELECT SchemaVersion,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?",(key(origin["workerExecutionId"]),)).fetchone()
    if worker is None or worker[0]!="2.0.0" or json.loads(worker[1])["modelWorkOrigin"]!=encoded_tree(origin) or connection.execute("SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state").fetchone()[0]!=(2 if embedded else 1):
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
        if row is None or row[0]!=source["status"] or stored["intent"]["payload"]!=source["intent"]["payload"] or stored["deliveredAt"]!=source["deliveredAt"]:
            raise ValueError("The physical native/model delivery source differs from its consumption proof.")
    rows=connection.execute("SELECT InputId,ContentJson FROM elsa_runtime_model_input WHERE ExecutionId=?",(key(model["executionId"]),)).fetchall()
    if len(rows)!=(1 if phase=="before" else (3 if model_cancel else 2)+int(embedded)-int(parent_settlement_owed)):raise ValueError("The physical model accepted an unexpected input.")
    receipt=observed["resultInput"]
    if receipt is not None:
        stored,=[json.loads(row[1]) for row in rows if row[0]==key(receipt["inputId"])]
        if (stored["contentFingerprint"]!=key(receipt["contentFingerprint"]) or stored["inputKind"]!=key("native-result") or
                stored["invocationId"]!=key(origin["invocationId"]) or stored["attemptId"]!=key(origin["attemptId"]) or stored["result"]!=receipt["result"]):
            raise ValueError("The physical model receipt lost its exact accepted input identity.")
        activity,=(connection.execute("SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=?",(key(origin["workerExecutionId"]),)).fetchall() if embedded else connection.execute("SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state").fetchall())
        completion=json.loads(activity[1])["completion"];expected=result["completion"]
        if result["status"]==5:
            if activity[0]!="Cancelled" or completion is not None:raise ValueError("The physical native root lost its actual cancellation.")
            return
        if result['status']==4:
            state=json.loads(activity[1])
            if activity[0]!='Faulted' or completion is not None or state['fault']['code']!=key(result['faultCode']):
                raise ValueError('The physical native root lost its actual technical fault.')
            return
        if (completion["invocationId"]!=key(expected["invocationId"]) or completion["attemptId"]!=key(expected["attemptId"]) or
                completion["contractFingerprint"]!=key(expected["contractFingerprint"]) or completion["outcomeKey"]!=key(expected["outcomeKey"]) or
                completion["result"]["inlineValue"]!=expected["result"] or completion["result"]["type"]["alias"]!=key(expected["resultType"]["alias"])):
            raise ValueError("The model return differs from the authoritative physical native root completion.")


def physical_model_cancellation(connection,read,checked,origin,result):
    model=checked["finished"]["model"];receipt=checked["finished"]["resultInput"]
    cancellations=model["payload"].get("cancellations")
    if (model["payload"]["formatVersion"]!=4 or not isinstance(cancellations,list) or len(cancellations)!=1 or
            model["terminalResult"]!={"status":"Cancelled","reason":"Cancel the recovered BPMN service invocation."}):
        raise ValueError("The model cancellation lost its retained semantic decision or exact native responsibility.")
    cancellation,=cancellations
    if cancellation["origin"]!=origin or cancellation["settled"] is not True:
        raise ValueError("The model cancellation did not settle its original native invocation and attempt.")
    row=connection.execute("SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?",(key(cancellation["sourceOutboxItemId"]),)).fetchone()
    source=json.loads(row[1]) if row else None
    if (row is None or row[0]!=2 or source["intent"]["kind"]!=key("Elsa.Bpmn.CancelNativeWork.v1") or
            source["intent"]["workflowExecutionId"]!=key(model["executionId"]) or source["intent"]["payload"]!={**cancellation,"settled":False} or
            source["status"]!="Delivered" or instant(source["deliveredAt"])<instant(receipt["acceptedAt"])):
        raise ValueError("The model cancellation source was acknowledged without its exact native result consumption.")
    row=connection.execute("SELECT ContentJson FROM elsa_runtime_model_input WHERE ExecutionId=? AND InputId=?",(key(model["executionId"]),key("cancel-actual-bpmn-etl"))).fetchone()
    accepted=json.loads(row[0]) if row else None;command=read("model-cancel-command.json");response=read("model-cancel-response.json")
    if (accepted is None or accepted["inputKind"]!=key("cancel") or accepted["acceptedRevision"]!=2 or
            accepted["result"]!={"status":"Accepted","code":None,"revision":2,"lifecycle":"TerminalPendingSettlement"} or
            command["pending"] or command["executionId"]!=model["executionId"] or command["inputId"]!="cancel-actual-bpmn-etl" or
            accepted["result"]!=command["result"] or response["result"]!=command["result"] or
            instant(accepted["acceptedAt"])>instant(receipt["acceptedAt"])):
        raise ValueError("The actual BPMN cancel command lost its retained pending-settlement decision.")
    cancel_sources=[json.loads(row[0]) for row in connection.execute("SELECT ContentJson FROM elsa_runtime_post_commit_outbox")
        if json.loads(row[0])["intent"]["kind"]==key("Elsa.Etl.Cancel.v1")]
    if len(cancel_sources)!=1 or result.get("cancellationSources")!=[base64.b64decode(cancel_sources[0]["outboxItemId"]).decode("utf-16le")]:
        raise ValueError("The actual native result lost its complete downstream cancellation inventory.")
    downstream=cancel_sources[0]
    if (downstream["status"]!="Delivered" or instant(downstream["deliveredAt"])>instant(receipt["acceptedAt"])):
        raise ValueError("The model settled before actual downstream cancellation delivery.")


def pending_cancellation(read,root,process,bridge,model_cancel):
    checked,origin,result=bridge;pending=read('pending.json');observed=read('pending-checked.json');model=observed['model'];worker=observed['worker']
    stage='model-command' if model_cancel else 'native-terminal';before=read('before.json')
    if (process.get('pendingCancellationStage')!=stage or process.get('pendingPredecessorExitCode')!=-9 or process.get('sweepIntervalsSeconds')!=[.2,300,.2,.2] or
            instant(process['pendingCutAtUtc'])<=instant(process['cutAtUtc']) or observed['readOnly'] is not True or observed['origin']!=origin or
            model['executionId']!=origin['executionId'] or model['executable']!=origin['executable'] or model['scope']!=origin['scope'] or model['materialIncarnationId']!=origin['materialIncarnationId'] or
            worker['workflowExecutionId']!=origin['workerExecutionId'] or worker['pinnedExecutable']!=origin['workerExecutable'] or worker['modelWorkOrigin']!=origin or
            model['payload']['state']!=checked['before']['model']['payload']['state'] or observed['queuedModelInputs'] or
            read('pending-wire.json')!=read('before-wire.json') or pending['run']['status']!='running' or pending['run']['runId']!=process['runId'] or
            pending['run']['selection']!=process['selection'] or pending['run']['acceptedRecords']!=2 or pending['native']['incidents']):
        raise ValueError('The actual second crash lost its pending source, full invocation, live ETL run or physical effects.')
    if any(x['inputKind']=='native-result' for x in observed['inputs']):raise ValueError('A native result was already consumed before the pending-result crash.')
    start,=[s for s in observed['sources'] if s['intent']['kind']=='Elsa.Bpmn.StartNativeWork.v1']
    if start!=checked['before']['startSource']:raise ValueError('The pending crash changed its already-delivered original native start.')
    expected_worker='Running' if model_cancel else 'Cancelled'
    if worker['status']!=(1 if model_cancel else 5) or pending['native']['instance']['status']!=expected_worker or model['revision']!=(2 if model_cancel else 1):
        raise ValueError('The pending cancellation stage contradicts actual native admission or terminal truth.')
    activity,=pending['native']['activities'];original,=before['native']['activities']
    saved_retry=next(x['http']['fetch'] for x in before['partitions']['items'] if x['http']['fetch'] and x['http']['fetch']['lastStatusCode']==429)
    pending_retry=next(x['http']['fetch'] for x in pending['partitions']['items'] if x['http']['fetch'] and x['http']['fetch']['lastStatusCode']==429)
    if (activity['activityExecutionId']!=original['activityExecutionId'] or activity['status']!=('Suspended' if model_cancel else 'Cancelled') or
            pending_retry['notBefore']!=saved_retry['notBefore'] or pending_retry['attemptCount']!=1 or pending_retry['phase']!='waiting'):
        raise ValueError('The pending cancellation recreated its native invocation or changed the frozen HTTP retry.')
    sources={s['outboxItemId']:s for s in observed['sources']}
    if model_cancel:
        cancellation,=model['payload']['cancellations'];receipt,=[x for x in observed['inputs'] if x['inputKind']=='cancel']
        cancel=sources[cancellation['sourceOutboxItemId']];final_cancel=checked['finished']['model']['payload']['cancellations'][0]
        if (model['lifecycle']!=2 or pending['model']['status']!='TerminalPendingSettlement' or model['payload']['work'] or model['payload'].get('nativeResults') or
                cancellation['origin']!=origin or cancellation['settled'] or cancellation.get('notAdmitted',False) or len(observed['inputs'])!=2 or
                cancel['intent']['kind']!='Elsa.Bpmn.CancelNativeWork.v1' or cancel['intent']['payload']!=cancellation or cancel['status'] in (2,4) or cancel['deliveredAt'] is not None or
                receipt['acceptedRevision']!=2 or receipt['result']!=read('model-cancel-response.json')['result'] or receipt['inputId']!='cancel-actual-bpmn-etl' or
                final_cancel['sourceOutboxItemId']!=cancellation['sourceOutboxItemId'] or final_cancel['origin']!=origin or
                model['terminalResult']!=checked['finished']['model']['terminalResult'] or any(s['intent']['kind'] in ('Elsa.Bpmn.NativeWorkResult.v1','Elsa.Etl.Cancel.v1') for s in observed['sources'])):
            raise ValueError('The second crash did not retain the exact model cancellation before worker delivery.')
    else:
        native,=[s for s in observed['sources'] if s['intent']['kind']=='Elsa.Bpmn.NativeWorkResult.v1']
        etl,=[s for s in observed['sources'] if s['intent']['kind']=='Elsa.Etl.Cancel.v1']
        if len(result['cancellationSources'])!=1:raise ValueError('The pending native result lost its complete cancellation inventory.')
        inventory,=result['cancellationSources']
        if (model!=checked['before']['model'] or model['lifecycle']!=0 or pending['model']['status']!='Running' or len(observed['inputs'])!=1 or
                native['status'] in (2,4) or etl['status'] in (2,4) or native['deliveredAt'] is not None or etl['deliveredAt'] is not None or
                native['intent']['payload']!=result or native['outboxItemId']!=checked['finished']['resultSource']['outboxItemId'] or
                inventory!=etl['outboxItemId']):
            raise ValueError('The second crash lost actual same-checkpoint ETL cancellation and native result responsibility.')
        native_suffix=':'+native['intent']['intentId'];etl_suffix=':'+etl['intent']['intentId']
        if native['outboxItemId'][:-len(native_suffix)]!=etl['outboxItemId'][:-len(etl_suffix)]:raise ValueError('Pending ETL cancellation and native result came from different terminal checkpoints.')
    materials=observed['workerMaterial']
    if materials['identity']!=origin['workerExecutable']:raise ValueError('The pending cancellation lost its frozen native material.')
    for store,snapshot in process['pendingStores'].items():
        file=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(file.read_bytes())
        if q.digest(file)!=snapshot['gzipSha256'] or hashlib.sha256(raw).hexdigest()!=snapshot['databaseSha256'] or snapshot['observer']['snapshotSha256']!=snapshot['databaseSha256'] or snapshot['observer']['readOnly'] is not True:
            raise ValueError('An actual provider-observed pending-cancellation store differs.')
        with tempfile.TemporaryDirectory(prefix='elsa-pending-cancel-read-') as temporary:
            database=Path(temporary)/'snapshot.sqlite';database.write_bytes(raw)
            with closing(sqlite3.connect(database.as_uri()+'?mode=ro&immutable=1',uri=True)) as connection:
                if connection.execute('PRAGMA integrity_check').fetchall()!=[('ok',)]:raise ValueError('A pending-cancellation store failed integrity.')
                if store=='elsa':
                    row,=connection.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchall();actual=json.loads(row[0])
                    if actual['payload']!=model['payload'] or actual['revision']!=model['revision'] or actual['terminalResult']!=model['terminalResult'] or actual['executable']!=encoded_tree(model['executable']):raise ValueError('The physical pending model differs from its durable cancellation decision.')
                    row,=connection.execute('SELECT SchemaVersion,ContentJson FROM elsa_runtime_workflow_execution_state').fetchall();actual=json.loads(row[1])
                    if row[0]!='2.0.0' or actual['modelWorkOrigin']!=encoded_tree(origin) or actual['status']!=expected_worker or actual['pinnedExecutable']!=encoded_tree(origin['workerExecutable']):raise ValueError('The physical pending worker differs from its true terminal state or exact origin.')
                    if connection.execute('SELECT COUNT(*) FROM elsa_runtime_bookmark_state').fetchone()[0]!=int(model_cancel):raise ValueError('The physical pending cancellation lost or retained an unexpected native wait.')
                    rows=[json.loads(x[0]) for x in connection.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox')]
                    if len(rows)!=len(sources):raise ValueError('The physical pending cancellation lost a delivery obligation.')
                    for source in sources.values():
                        row,=[z for z in rows if z['outboxItemId']==key(source['outboxItemId'])]
                        if row['intent']['payload']!=source['intent']['payload'] or row['deliveredAt']!=source['deliveredAt'] or row['status']!=['Pending','Delivering','Delivered','FailedRetryable','FailedFinal'][source['status']]:raise ValueError('The physical pending source differs from its owed acknowledgment state.')
                    rows=[json.loads(x[0]) for x in connection.execute('SELECT ContentJson FROM elsa_runtime_model_input')]
                    if len(rows)!=len(observed['inputs']):raise ValueError('The physical pending model already consumed an unobserved result.')
                    for receipt in observed['inputs']:
                        row,=[z for z in rows if z['inputId']==key(receipt['inputId'])]
                        if row['result']!=receipt['result'] or row['acceptedRevision']!=receipt['acceptedRevision'] or row['contentFingerprint']!=key(receipt['contentFingerprint']):raise ValueError('The physical pending model receipt differs from its actual command.')
                    for source in sources.values():
                        if source['intent']['kind'] not in ('Elsa.Bpmn.CancelNativeWork.v1','Elsa.Bpmn.NativeWorkResult.v1','Elsa.Etl.Cancel.v1'):continue
                        suffix=':'+source['intent']['intentId'];commit=source['outboxItemId'][:-len(suffix)]
                        row=connection.execute('SELECT ContentJson FROM elsa_runtime_checkpoint_commit WHERE CommitId=?',(key(commit),)).fetchone()
                        if row is None or source['outboxItemId'] not in json.loads(row[0])['pendingPostCommitWorkIds']:raise ValueError('The physical checkpoint lost its original pending cancel/result responsibility.')
                elif store=='etl-runtime':
                    row,=connection.execute('SELECT InvocationJson,Status,AcceptedRecords,RejectedRecords FROM elsa_etl_runtime_runs').fetchall();invocation=json.loads(row[0])
                    if row[1:]!=(1,2,0) or decoded(invocation['runId'])!=process['runId'] or decoded(invocation['resultAddress']['workflowExecutionId'])!=origin['workerExecutionId'] or decoded(invocation['resultAddress']['activityExecutionId'])!=before['native']['activities'][0]['activityExecutionId']:
                        raise ValueError('The physical pending ETL run lost its original active invocation or completed sibling effects.')
                    if connection.execute('SELECT COUNT(*) FROM elsa_etl_runtime_terminals').fetchone()[0] or connection.execute('SELECT COUNT(*) FROM elsa_etl_runtime_outbox').fetchone()[0]:raise ValueError('ETL cancellation was already terminal before the pending crash.')
                    if not model_cancel and etl['intent']['payload']!=invocation:raise ValueError('The pending ETL cancel no longer targets the immutable real invocation.')



def consumed_before_ack(read,root,process,bridge,model_cancel):
    checked,origin,result=bridge;observed=read('consumed-checked.json');view=read('consumed.json');sdk=read('sdk-consume.json')
    model=observed['model'];worker=observed['worker'];receipt,=[x for x in observed['inputs'] if x['inputKind']=='native-result']
    native,=[s for s in observed['sources'] if s['intent']['kind']=='Elsa.Bpmn.NativeWorkResult.v1']
    etl,=[s for s in observed['sources'] if s['intent']['kind']=='Elsa.Etl.Cancel.v1'];start,=[s for s in observed['sources'] if s['intent']['kind']=='Elsa.Bpmn.StartNativeWork.v1']
    if (process.get('postConsumptionPreAck') is not True or observed['readOnly'] is not True or observed['origin']!=origin or
            model!=checked['finished']['model'] or worker!=checked['finished']['worker'] or observed['queuedModelInputs'] or model['lifecycle']!=3 or
            receipt!=checked['finished']['resultInput'] or receipt!=sdk['receipt'] or receipt['acceptedRevision']!=(3 if model_cancel else 2) or
            native['outboxItemId']!=checked['finished']['resultSource']['outboxItemId'] or native['intent']['payload']!=result or native['status']!=3 or native['deliveredAt'] is not None or native['deliveryFencingToken']<1 or
            start!=checked['before']['startSource'] or etl['status']!=2 or etl['deliveredAt'] is None or result['cancellationSources']!=[etl['outboxItemId']] or
            view['run']['status']!='cancelled' or view['run']['runId']!=process['runId'] or view['run']['acceptedRecords']!=2 or view['run']['outstandingRecords'] or view['run']['outstandingBytes'] or
            view['native']['instance']['status']!='Cancelled' or view['model']['status']!='TerminalSettled' or view['model']['revision']!=model['revision'] or
            read('consumed-wire.json')!=read('before-wire.json')):
        raise ValueError('The actual pre-ACK crash lacks durable exact consumption with its original result source still owed.')
    if model_cancel:
        cancellation,=model['payload']['cancellations'];cancel,=[s for s in observed['sources'] if s['intent']['kind']=='Elsa.Bpmn.CancelNativeWork.v1']
        if (not cancellation['settled'] or cancellation.get('notAdmitted',False) or cancel['outboxItemId']!=cancellation['sourceOutboxItemId'] or
                cancel['status']!=3 or cancel['deliveredAt'] is not None):raise ValueError('The consumed model cancellation lost its still-owed original acknowledgment.')
    operations=['deliver-model-cancel','drain-native-cancel'] if model_cancel else []
    operations+=['deliver-etl-cancel','queue-native-result','consume-native-result']
    if (sdk['operation']!='--consume-native-result' or sdk['origin']!=origin or sdk['model']!=model or sdk['worker']!=worker or sdk['resultSource']!=native or
            [s['operation'] for s in sdk['steps']]!=operations or len(sdk['products'])!=8 or sdk['products'][0]['sha256']!=process['dispatcherSha256'] or
            any(process['sourceRevision'] not in p['informationalVersion'] for p in sdk['products']) or
            not (instant(process['pendingCutAtUtc'])<instant(sdk['startedAtUtc'])<=instant(sdk['completedAtUtc'])<instant(process['consumedCutAtUtc']))):
        raise ValueError('The actual SDK consumption lost its production operations, exact source or pre-ACK ordering.')
    for step in sdk['steps']:
        if 'delivery' in step:
            delivery=step['delivery']
            if delivery['attemptedCount']!=1 or delivery['deliveredCount']!=int(step['operation']=='deliver-etl-cancel'):
                raise ValueError('The actual SDK fabricated acknowledgment instead of retaining native/model responsibility.')
        elif step['drain']['status']!=0:raise ValueError('The protocol-aware recovery mailbox did not accept actual consumption.')
    observed_bridge={**checked,'consumed':{'model':model,'worker':worker,'startSource':start,'resultSource':native,'resultInput':receipt}}
    for store,snapshot in process['consumedStores'].items():
        file=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(file.read_bytes())
        if q.digest(file)!=snapshot['gzipSha256'] or hashlib.sha256(raw).hexdigest()!=snapshot['databaseSha256'] or snapshot['observer']['snapshotSha256']!=snapshot['databaseSha256'] or snapshot['observer']['readOnly'] is not True:
            raise ValueError('An actual provider-observed consumed-before-ACK store differs.')
        with tempfile.TemporaryDirectory(prefix='elsa-consumed-read-') as temporary:
            database=Path(temporary)/'snapshot.sqlite';database.write_bytes(raw)
            with closing(sqlite3.connect(database.as_uri()+'?mode=ro&immutable=1',uri=True)) as connection:
                if connection.execute('PRAGMA integrity_check').fetchall()!=[('ok',)]:raise ValueError('A consumed-before-ACK store failed integrity.')
                if store=='elsa':
                    physical_model_bridge(connection,'consumed',observed_bridge,origin,result,model_cancel=model_cancel)
                    if connection.execute('SELECT COUNT(*) FROM elsa_runtime_bookmark_state').fetchone()[0]:raise ValueError('The consumed cancellation recreated a native wait.')
                    rows=[json.loads(x[0]) for x in connection.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox')]
                    if len(rows)!=len(observed['sources']):raise ValueError('The physical pre-ACK store lost a retained delivery source.')
                    for source in observed['sources']:
                        row,=[x for x in rows if x['outboxItemId']==key(source['outboxItemId'])]
                        if row['intent']['payload']!=source['intent']['payload'] or row['deliveredAt']!=source['deliveredAt'] or row['status']!=['Pending','Delivering','Delivered','FailedRetryable','FailedFinal'][source['status']]:raise ValueError('The physical pre-ACK source disagrees with actual consumption/acknowledgment state.')
                elif store=='etl-runtime':
                    row,=connection.execute('SELECT InvocationJson,Status,AcceptedRecords,RejectedRecords,OutstandingRecords,OutstandingBytes FROM elsa_etl_runtime_runs').fetchall()
                    invocation=json.loads(row[0]);terminal=connection.execute('SELECT Outcome FROM elsa_etl_runtime_terminals').fetchall()
                    if row[1:]!=(5,2,0,0,0) or terminal!=[(3,)] or etl['intent']['payload']!=invocation or decoded(invocation['runId'])!=process['runId']:
                        raise ValueError('The actual pre-ACK consumption left its original ETL invocation running or rebound cancellation.')



def embedded_etl_parent(read,root,process,bridge,parent_cancel):
    checked,worker_origin,native_result=bridge
    phases={p:read(p+'-checked-embedding.json') for p in ('before','finished','retained')}
    before=phases['before'];after=phases['finished'];model=before['model'];final=after['model'];native=before['nativeParent']
    parent=process['embeddedParent'];parent_id=process['nativeParentExecutionId'];parent_pin={k:process['nativeParentPublication'][k] for k in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')}
    faulted=process.get('technicalFault') is True or process['businessError'] and process.get('businessErrorBoundary')!='matching'
    absorbed=faulted and parent=='CompensationScope'
    def invocation(value):
        if isinstance(value,str):
            if not value.startswith('u16:'):raise ValueError('The embedded native invocation lost its exact opaque encoding.')
            return base64.b64decode(value[4:],validate=True).decode('utf-16le','surrogatepass')
        if isinstance(value,dict):return {k:invocation(v) for k,v in value.items()}
        if isinstance(value,list):return [invocation(v) for v in value]
        return value
    encoded=model['payload']['nativeParent']['invocation'];origin=invocation(encoded)
    if (parent not in ('Root','Sequence','Flowchart','CompensationScope') or process.get('nativeParentCancel') is not parent_cancel or phases['finished']!=phases['retained'] or
            model!=checked['before']['model'] or final!=checked['finished']['model'] or origin['executionId']!=model['executionId'] or origin['nativeExecutionId']!=parent_id or
            origin['nativeExecutable']!=parent_pin or native['pinnedExecutable']!=parent_pin or native['workflowExecutionId']!=parent_id or
            origin['model']['executable']!=model['executable'] or origin['model']['materialIncarnationId']!=model['materialIncarnationId'] or
            model['payload']['formatVersion']!=5 or final['payload']['formatVersion']!=(6 if parent_cancel else 5) or final['revision']!=(4 if parent_cancel else 3) or
            final['payload']['nativeParent']['invocation']!=encoded or native.get('modelWorkOrigin') is not None or origin['scope']!=model['scope']):
        raise ValueError('The actual embedded ETL journey lost its parent/model/worker pins, format or retained invocation.')
    activity,=[a for a in before['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
    current,=[a for a in after['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
    bookmark,=before['bookmarks']
    if (activity['invocationId']!=origin['nativeInvocationId'] or activity['status']!=3 or activity['privateState']['producedByAttemptId']!=origin['nativeAttemptId'] or
            activity['privateState']['value']['inlineValue']['Invocation']!=encoded or bookmark['bookmarkId']!=origin['registrationId'] or bookmark['stimulusHash']!=model['executionId'] or
            current['invocationId']!=activity['invocationId'] or current['status']!=(6 if parent_cancel else 5 if faulted else 4) or after['bookmarks'] or after['queuedModelInputs'] or
            after['nativeParent']['status']!=(5 if parent_cancel else 4 if faulted and not absorbed else 3) or model['authority']!=native['authority'] or final['authority']!=after['nativeParent']['authority']):
        raise ValueError('The real RunBpmn wait/result did not retain its actual native registration and parent terminal truth.')
    if not parent_cancel and ((not faulted and current['completion']['result']['inlineValue']!={'executionId':model['executionId'],'result':final['terminalResult']}) or
            (faulted and current['completion'] is not None) or not any(r['status']==1 and r['registrationId']==origin['registrationId'] and r['providerId']=='elsa.bpmn.native-parent.v1' and r['payloadType']['alias']=='Elsa.Activities.Bpmn.Models.RunBpmnCompletion' for r in current['triggerDeliveries'])):
        raise ValueError('The native parent lacks its exact typed RunBpmn consumption proof.')
    successors=[a for a in after['activities'] if a['execution']['executableNodeId']=='after']
    if (parent_cancel or faulted) and successors or not parent_cancel and not faulted and parent!='Root' and (len(successors)!=1 or successors[0]['status']!=4):raise ValueError('The actual native parent successor did not preserve completion/fault/cancellation behavior.')
    outer=read('finished.json')['embedding'];api_run,=[a for a in outer['activities'] if a['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
    incidents=outer['incidents']
    faulted_ids={a['invocationId'] for a in after['activities'] if a['status']==5}
    if (outer['instance']['status']!=('Cancelled' if parent_cancel else 'Faulted' if faulted and not absorbed else 'Completed') or
            api_run['status']!=('Cancelled' if parent_cancel else 'Faulted' if faulted else 'Completed') or
            (faulted and (len(incidents)!=len(faulted_ids) or {i['activityExecutionId'] for i in incidents}!=faulted_ids or
                any(i['workflowExecutionId']!=parent_id or i['status']!=('Resolved' if absorbed else 'Blocking') or
                    i['failureType']!='ActivityReturnedFault' or i['resolutionOutcome']['actionKind']!=('AbsorbFault' if absorbed else 'FaultWorkflow') for i in incidents))) or (not faulted and incidents)):
        raise ValueError('The actual native parent lost its default fault incident or terminal API truth.')
    if absorbed:
        composite,=[a for a in after['activities'] if a['execution']['activityType']=='Elsa.Activities.Compensation.Activities.CompensationScope']
        if (composite['status']!=4 or composite['completion']['outcomeKey']!='Compensated' or
                incidents[0]['isBlocking'] or incidents[0]['resolvedAt'] is None or incidents[0]['metadata'].get('runtime.faultAbsorbedBy')!=composite['invocationId']):
            raise ValueError('The actual compensation scope lost its default Compensated outcome or absorbed-fault proof.')
    inputs=after['inputs'];settlement,=[r for r in inputs if r['inputKind']=='native-parent-settled'];start,=[r for r in inputs if r['inputKind']=='start-embedded']
    if (settlement['acceptedRevision']!=final['revision'] or settlement['result']['status']!='Accepted' or settlement['invocationId']!=origin['nativeInvocationId'] or
            settlement['attemptId']!=origin['nativeAttemptId'] or start['acceptedRevision']!=1 or len(inputs)!=(4 if parent_cancel else 3)):
        raise ValueError('The real native parent lacks its exact model admission/settlement receipt.')
    sources={s['intent']['kind']:s for s in after['sources']};result=sources['Elsa.Bpmn.ResumeNativeParent.v1'];start_source=sources['Elsa.Bpmn.StartEmbedded.v1']
    expected={'Elsa.Bpmn.StartEmbedded.v1','Elsa.Bpmn.StartNativeWork.v1','Elsa.Bpmn.ResumeNativeParent.v1'}
    if parent_cancel:expected|={'Elsa.Bpmn.CancelEmbedded.v1','Elsa.Bpmn.CancelNativeWork.v1'}
    if (set(sources)!=expected or len(after['sources'])!=len(expected) or any(s['status']!=2 for s in sources.values()) or
            start_source['intent']['payload']!=encoded or result['intent']['payload']!={'FormatVersion':1,'Invocation':encoded,'Result':final['terminalResult']} or
            instant(result['deliveredAt'])<instant(settlement['acceptedAt'])):
        raise ValueError('The embedded ETL result was acknowledged without its exact native parent settlement proof.')
    if parent_cancel:
        cancellation,=final['payload']['cancellations'];native_cancel=sources['Elsa.Bpmn.CancelNativeWork.v1']
        if (not cancellation['settled'] or cancellation.get('notAdmitted',False) or cancellation['origin']!=worker_origin or cancellation['sourceOutboxItemId']!=native_cancel['outboxItemId'] or
                sources['Elsa.Bpmn.CancelEmbedded.v1']['intent']['payload']!=encoded):raise ValueError('Actual parent cancellation did not settle the original worker/ETL invocation.')
    for phase in ('before','finished'):
        snapshot=process[phase+'Stores']['elsa'];file=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(file.read_bytes())
        with tempfile.TemporaryDirectory(prefix='elsa-embedded-parent-read-') as temporary:
            db=Path(temporary)/'s.sqlite';db.write_bytes(raw)
            with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                observed=phases[phase];row=c.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(key(parent_id),)).fetchone()
                if row is None or row[0]!=(1 if phase=='before' else 5 if parent_cancel else 4 if faulted and not absorbed else 3) or json.loads(row[1])['pinnedExecutable']!=encoded_tree(parent_pin):raise ValueError('The physical RunBpmn parent lost its exact pin or terminal state.')
                for activity in observed['activities']:
                    row=c.execute('SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=? AND ActivityExecutionId=?',(key(parent_id),key(activity['invocationId']))).fetchone()
                    if row is None or row[0]!=('Scheduled','Running','Waiting','Suspended','Completed','Faulted','Cancelled','Recovered','Superseded')[activity['status']]:raise ValueError('The physical native parent differs from its actual activity status.')
                    if phase=='before' and activity['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn' and json.loads(row[1])['privateState']['value']['inlineValue']!=activity['privateState']['value']['inlineValue']:
                        raise ValueError('The physical RunBpmn wait lost its original pinned model address.')
                if c.execute('SELECT COUNT(*) FROM elsa_runtime_model_execution').fetchone()[0]!=1 or c.execute('SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state').fetchone()[0]!=2:
                    raise ValueError('The real embedded model acquired a synthetic native carrier or duplicate worker.')
                row=c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable WHERE ArtifactId=?',(key(parent_pin['artifactId']),)).fetchone()
                if row is None or not any(e['artifactId']==key(model['executable']['artifactId']) and e['artifactHash']==key(model['executable']['artifactHash']) for e in json.loads(row[0])['dependencies']):
                    raise ValueError('The actual native parent lost its retained model dependency closure.')
                native_material=json.loads(row[0]);root_types={'Root':'Elsa.Activities.Bpmn.Activities.RunBpmn','Sequence':'Elsa.Activities.Sequence.Activities.Sequence','Flowchart':'Elsa.Activities.Flowchart.Activities.Flowchart','CompensationScope':'Elsa.Activities.Compensation.Activities.CompensationScope'}
                if native_material['identity']!=encoded_tree(parent_pin) or native_material['rootActivity']['activityType']!=key(root_types[parent]):raise ValueError('The physical native parent root differs from its declared composite shape or full pin.')
                material,=[json.loads(r[0])['material'] for r in c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable') if json.loads(r[0]).get('material')]
                if material['profile']!=key('foundation-native-work-v1'):raise ValueError('The actual embedded material lost its qualified native service profile.')
                rows=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_model_input')]
                if len(rows)!=len(observed['inputs']):raise ValueError('The physical embedded ETL model receipt count differs.')
                for receipt in observed['inputs']:
                    row,=[r for r in rows if r['inputId']==key(receipt['inputId'])]
                    if row['contentFingerprint']!=key(receipt['contentFingerprint']) or row['result']!=receipt['result'] or row['acceptedRevision']!=receipt['acceptedRevision']:raise ValueError('The physical parent settlement receipt differs from its exact consumed input.')
                rows=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox')]
                for source in observed['sources']:
                    row,=[r for r in rows if r['outboxItemId']==key(source['outboxItemId'])]
                    if row['intent']['payload']!=source['intent']['payload'] or row['deliveredAt']!=source['deliveredAt'] or row['status']!=['Pending','Delivering','Delivered','FailedRetryable','FailedFinal'][source['status']]:raise ValueError('The physical embedded delivery source differs from its exact consumption/acknowledgment proof.')
                if phase=='finished' and parent_cancel:
                    etl,=[r for r in rows if r['intent']['kind']==key('Elsa.Etl.Cancel.v1')]
                    if (etl['status']!='Delivered' or etl['deliveredAt'] is None or instant(etl['deliveredAt'])>instant(checked['finished']['resultInput']['acceptedAt']) or
                            native_result['cancellationSources']!=[base64.b64decode(etl['outboxItemId']).decode('utf-16le')]):
                        raise ValueError('The actual embedded model settled before its exact downstream ETL cancellation.')
                    cancel_input,=[r for r in after['inputs'] if r['inputKind']=='cancel-embedded']
                    if cancel_input['result']['lifecycle']!='TerminalPendingSettlement' or checked['finished']['resultInput']['result']['lifecycle']!='TerminalPendingSettlement':
                        raise ValueError('The embedded cancellation lost its pending worker/parent settlement decisions.')
                if phase=='finished' and c.execute('SELECT COUNT(*) FROM elsa_runtime_scheduler_work_item').fetchone()[0]:raise ValueError('The actual embedded ETL journey left native/model work queued.')
                if phase=='finished':
                    stored=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_incident_state WHERE WorkflowExecutionId=?',(key(parent_id),))]
                    if faulted:
                        if len(stored)!=len(incidents):raise ValueError('The physical parent lost its default fault incident inventory.')
                        for observed_incident in incidents:
                            incident,=[i for i in stored if i['incidentId']==key(observed_incident['incidentId'])]
                            if (incident['workflowExecutionId']!=key(parent_id) or incident['activityExecutionId']!=key(observed_incident['activityExecutionId']) or
                                    incident['status']!=observed_incident['status'] or incident['resolvedAt']!=observed_incident['resolvedAt'] or
                                    incident['failureType']!=key('ActivityReturnedFault') or incident['resolutionOutcome']['actionKind']!=key(observed_incident['resolutionOutcome']['actionKind'])):
                                raise ValueError('The physical parent lost its exact default fault incident.')
                        if absorbed:
                            root,=[a for a in observed['activities'] if a['execution']['activityType']=='Elsa.Activities.Compensation.Activities.CompensationScope']
                            row,=c.execute('SELECT ContentJson FROM elsa_runtime_activity_execution_state WHERE ActivityExecutionId=?',(key(root['invocationId']),)).fetchall()
                            if json.loads(row[0])['completion']['outcomeKey']!=key('Compensated'):raise ValueError('The physical compensation scope lost its default Compensated outcome.')
                    elif stored:raise ValueError('The physical completed/cancelled parent acquired an incident.')



def direct_embedded_trace_comparison(root,manifest):
    """Compare actual success traces without equating separately published executable pins."""
    counterpart=manifest['directCounterpart']
    if counterpart['manifest']!='direct-counterpart/manifest.json':raise ValueError('The actual direct counterpart changed its bound evidence path.')
    file=q.evidence_file(root,counterpart['manifest'])
    if q.digest(file)!=counterpart['sha256']:raise ValueError('The actual direct counterpart manifest differs.')
    result=verify(file)
    if result['sourceRevision']!=manifest['sourceRevision'] or not result['directSuccessCounterpartVerified']:
        raise ValueError('The actual direct counterpart changed its source or qualification.')

    def observation(directory):
        process=json.loads((directory/'process.json').read_text())
        states=[]
        for phase in ('before','finished'):
            model=json.loads((directory/(phase+'-checked-model.json')).read_text())['model']
            state=deepcopy(model['payload']['state'])
            # Only this self-referential execution handle differs between isolated real runs.
            # Preserve every other field, diagnostic, token/flow identity and ordering.
            for token in state['tokens']:
                if token['producingWorkHandle']==model['executionId']:token['producingWorkHandle']='$model'
            states.append(state)
        products=process['products']['actualProductVersions']
        if (len({p['name'] for p in products})!=12 or any(re.fullmatch('[0-9a-f]{64}',p['sha256']) is None for p in products)):
            raise ValueError('The actual direct/embedded comparison lacks its distinct captured product hashes.')
        hashes={p['name']:(p['productVersion'],p['sha256']) for p in products}
        snapshot=process['beforeStores']['elsa'];raw=gzip.decompress(q.evidence_file(directory,snapshot['file']).read_bytes())
        with tempfile.TemporaryDirectory(prefix='elsa-direct-embedded-trace-') as temporary:
            database=Path(temporary)/'snapshot.sqlite';database.write_bytes(raw)
            with closing(sqlite3.connect(database.as_uri()+'?mode=ro&immutable=1',uri=True)) as connection:
                materials=[json.loads(r[0]).get('material') for r in connection.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable')]
                material,=[m for m in materials if m is not None and m['identity']==encoded_tree(process['modelPublication']['executable'])]
        definition=material['definition']
        # The process is identical. Each capture retains its own independently checked worker pin.
        bindings=[{k:v for k,v in b.items() if k!='executable'} for b in definition['serviceBindings']]
        contract={k:material[k] for k in ('formatVersion','protocol','profile','inputContract','outputs','runtimeRequirements')}
        contract.update(definitionFormat=definition['formatVersion'],process=definition['process'],humanBindings=definition['humanBindings'],serviceContracts=bindings)
        return states,hashes,contract

    direct=observation(file.parent/'capture')
    compared=[]
    for case in manifest['cases']:
        if case['cancel']:continue
        observed=observation(root/case['name']/'capture')
        if observed[0]!=direct[0]:raise ValueError('The actual embedded interpreter state differs from the direct success trace.')
        if observed[1]!=direct[1]:raise ValueError('The actual embedded product hashes differ from the direct success build.')
        if observed[2]!=direct[2]:raise ValueError('The actual embedded process or binding contract differs from the direct success definition.')
        compared.append(case['parent'])
    return result,compared


def pending_embedded_parent_return(read,root,process,bridge):
    checked,origin,native_result=bridge
    pending=read('parent-pending-checked-model.json');embedding=read('parent-pending-checked-embedding.json');sdk=read('sdk-consume.json')
    model=pending['model'];last=checked['finished'];after=read('finished-checked-embedding.json')
    original=read('before-checked-embedding.json');parent_id=process['nativeParentExecutionId']
    sources={s['intent']['kind']:s for s in embedding['sources']};final_sources={s['intent']['kind']:s for s in after['sources']}
    parent=sources['Elsa.Bpmn.ResumeNativeParent.v1'];final_parent=final_sources['Elsa.Bpmn.ResumeNativeParent.v1']
    if (process['pendingParentResultCrash'] is not True or process['parentPendingPredecessorExitCode']!=-9 or process['sweepIntervalsSeconds']!=[.2,300,.2,.2] or
            model['lifecycle']!=2 or model['revision']!=3 or model['payload']['formatVersion']!=6 or model['payload']!=last['model']['payload'] or
            model['terminalResult']!=last['model']['terminalResult'] or model['authority']!=last['model']['authority'] or model['executable']!=last['model']['executable'] or
            model['materialIncarnationId']!=last['model']['materialIncarnationId'] or model['scope']!=last['model']['scope'] or model['executionId']!=origin['executionId'] or
            pending['worker']['status']!=5 or pending['worker']['modelWorkOrigin']!=origin or embedding['model']!=model or
            pending['resultInput']!=last['resultInput'] or pending['resultInput']['acceptedRevision']!=3 or pending['resultInput']['result']['lifecycle']!='TerminalPendingSettlement' or
            embedding['queuedModelInputs'] or any(not c['settled'] for c in model['payload']['cancellations'])):
        raise ValueError('The actual parent-pending cut lost its terminal model, exact worker consumption or settled downstream responsibility.')
    if (sdk['operation']!='--consume-native-result' or sdk['embeddedParentExecutionId']!=parent_id or sdk['model']!=model or sdk['worker']!=pending['worker'] or
            sdk['receipt']!=pending['resultInput'] or sdk['resultSource']!=pending['resultSource'] or sdk['origin']!=origin or
            len(sdk['products'])!=8 or len({p['name'] for p in sdk['products']})!=8 or any(not p['informationalVersion'].endswith('+'+process['sourceRevision']) for p in sdk['products'])):
        raise ValueError('The actual pending-parent SDK receipt differs from the original source-matched production consumption.')
    operations=['deliver-embedded-cancel','consume-embedded-cancel','deliver-model-cancel','drain-native-cancel','deliver-etl-cancel','queue-native-result','consume-native-result']
    if [s['operation'] for s in sdk['steps']]!=operations:raise ValueError('The actual pending-parent SDK did not retain its complete delivery/consumption chain.')
    for step in sdk['steps']:
        if 'delivery' in step:
            d=step['delivery']
            if d['attemptedCount']!=1 or d['deliveredCount']!=int(step['operation']=='deliver-etl-cancel'):
                raise ValueError('The pending-parent SDK acknowledged a source instead of retaining actual consumption responsibility.')
        elif step['drain']['status']!=0:raise ValueError('The pending-parent SDK did not use an accepted real recovery mailbox.')
    if (pending['resultSource']['status']!=3 or pending['resultSource']['deliveryFencingToken']<=0 or pending['resultSource']['deliveredAt'] is not None or
            pending['resultSource']['intent']!=last['resultSource']['intent'] or pending['resultSource']['outboxItemId']!=last['resultSource']['outboxItemId'] or
            parent['status']!=0 or parent['deliveredAt'] is not None or parent['outboxItemId']!=final_parent['outboxItemId'] or parent['intent']!=final_parent['intent'] or
            any(r['inputKind']=='native-parent-settled' for r in embedding['inputs']) or
            sorted(embedding['inputs'],key=lambda r:r['inputId'])!=sorted([r for r in after['inputs'] if r['inputKind']!='native-parent-settled'],key=lambda r:r['inputId'])):
        raise ValueError('The actual pending-parent cut lacks original unacknowledged result sources and an absent parent-settlement receipt.')
    if (embedding['nativeParent']['workflowExecutionId']!=parent_id or embedding['nativeParent']['status']!=5 or embedding['nativeParent']['pinnedExecutable']!=original['nativeParent']['pinnedExecutable'] or
            embedding['bookmarks'] or any(a['execution']['executableNodeId']=='after' for a in embedding['activities']) or
            len([a for a in embedding['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn' and a['status']==6])!=1 or
            read('parent-pending-wire.json')!=read('before-wire.json') or read('parent-pending.json')['run']['status']!='cancelled'):
        raise ValueError('The actual pending-parent cut lost its cancelled native parent or original HTTP effects.')
    if set(process['parentPendingStores'])!={'elsa','etl-runtime','etl-design'}:raise ValueError('The actual parent-pending cut lacks all three coupled stores.')
    for kind in ('Elsa.Bpmn.CancelEmbedded.v1','Elsa.Bpmn.CancelNativeWork.v1'):
        source=sources[kind];final_source=final_sources[kind]
        if source['status']!=3 or source['deliveryFencingToken']<=0 or source['deliveredAt'] is not None or source['outboxItemId']!=final_source['outboxItemId'] or source['intent']!=final_source['intent']:
            raise ValueError('The actual pending embedded cancellation lost its original fenced source acknowledgment responsibility.')
    observations={**checked,'parentPending':pending}
    for store,snapshot in process['parentPendingStores'].items():
        file=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(file.read_bytes())
        if q.digest(file)!=snapshot['gzipSha256'] or hashlib.sha256(raw).hexdigest()!=snapshot['databaseSha256'] or snapshot['observer']['snapshotSha256']!=snapshot['databaseSha256'] or snapshot['observer']['readOnly'] is not True:
            raise ValueError('An actual parent-pending coupled store differs from its read-only provider backup.')
        with tempfile.TemporaryDirectory(prefix='elsa-parent-pending-read-') as temporary:
            database=Path(temporary)/'snapshot.sqlite';database.write_bytes(raw)
            with closing(sqlite3.connect(database.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                if c.execute('PRAGMA integrity_check').fetchall()!=[('ok',)]:raise ValueError('An actual parent-pending store failed integrity.')
                if store=='elsa':
                    physical_model_bridge(c,'parentPending',observations,origin,native_result,model_cancel=True,embedded=True,parent_settlement_owed=True)
                    row=c.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(key(parent_id),)).fetchone()
                    if row is None or row[0]!=5 or json.loads(row[1])['pinnedExecutable']!=encoded_tree(embedding['nativeParent']['pinnedExecutable']):raise ValueError('The physical pending native parent lost its cancellation or pin.')
                    rows=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_model_input')]
                    if len(rows)!=3 or any(r['inputKind']==key('native-parent-settled') for r in rows):raise ValueError('The physical pending model already accepted native-parent settlement.')
                    for receipt in embedding['inputs']:
                        row,=[r for r in rows if r['inputId']==key(receipt['inputId'])]
                        if row['contentFingerprint']!=key(receipt['contentFingerprint']) or row['result']!=receipt['result'] or row['acceptedRevision']!=receipt['acceptedRevision']:raise ValueError('The physical pending model changed an exact accepted receipt.')
                    rows=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox')]
                    for source in embedding['sources']:
                        row,=[r for r in rows if r['outboxItemId']==key(source['outboxItemId'])]
                        if row['intent']['payload']!=source['intent']['payload'] or row['deliveredAt']!=source['deliveredAt'] or row['status']!=['Pending','Delivering','Delivered','FailedRetryable','FailedFinal'][source['status']]:raise ValueError('The physical pending native-parent source differs from its actual ACK state.')
                    etl,=[r for r in rows if r['intent']['kind']==key('Elsa.Etl.Cancel.v1')]
                    if etl['status']!='Delivered' or instant(etl['deliveredAt'])>instant(pending['resultInput']['acceptedAt']) or native_result['cancellationSources']!=[base64.b64decode(etl['outboxItemId']).decode('utf-16le')]:raise ValueError('The physical parent-pending model consumed before its actual ETL cancellation settled.')
                elif store=='etl-runtime':
                    row,=c.execute('SELECT InvocationJson,Status,AcceptedRecords,RejectedRecords,OutstandingRecords,OutstandingBytes FROM elsa_etl_runtime_runs').fetchall()
                    invocation=json.loads(row[0])
                    if row[1:]!=(5,2,0,0,0) or c.execute('SELECT Outcome FROM elsa_etl_runtime_terminals').fetchall()!=[(3,)] or decoded(invocation['runId'])!=process['runId'] or decoded(invocation['resultAddress']['workflowExecutionId'])!=origin['workerExecutionId']:
                        raise ValueError('The actual parent-pending ETL store lost cancellation, accounting or its original native result address.')



def successful_embedded_parent_return(read,root,process,bridge):
    checked,origin,native_result=bridge;stage=process['successfulParentResultCrashStage'];settled=stage=='settled';consumed=stage!='pending'
    pending=read('parent-pending-checked-model.json');embedding=read('parent-pending-checked-embedding.json');sdk=read('sdk-consume.json')
    before=read('before-checked-embedding.json');after=read('finished-checked-embedding.json');model=pending['model'];last=checked['finished']
    parent_id=process['nativeParentExecutionId'];sources={s['intent']['kind']:s for s in embedding['sources']};final_sources={s['intent']['kind']:s for s in after['sources']}
    parent=sources['Elsa.Bpmn.ResumeNativeParent.v1'];final_parent=final_sources['Elsa.Bpmn.ResumeNativeParent.v1']
    if (stage not in ('pending','consumed','settled') or process['parentPendingPredecessorExitCode']!=-9 or process['sweepIntervalsSeconds']!=[.2,300,.2,.2] or
            model['lifecycle']!=(3 if settled else 2) or model['revision']!=(3 if settled else 2) or model['payload']['formatVersion']!=5 or
            model['payload']!=last['model']['payload'] or model['terminalResult']!=last['model']['terminalResult'] or model['terminalResult']['status']!='Completed' or
            model['authority']!=last['model']['authority'] or model['executable']!=last['model']['executable'] or model['materialIncarnationId']!=last['model']['materialIncarnationId'] or
            model['scope']!=last['model']['scope'] or model['executionId']!=origin['executionId'] or pending['worker']['status']!=3 or pending['worker']['modelWorkOrigin']!=origin or
            embedding['model']!=model or pending['resultInput']!=last['resultInput'] or pending['resultInput']['acceptedRevision']!=2 or pending['resultInput']['result']['lifecycle']!='TerminalPendingSettlement'):
        raise ValueError('The actual successful parent cut lost its terminal result, exact original native consumption or selected model settlement stage.')
    native_consumed=sdk['nativeResultConsumedModel']
    if (sdk['operation']!='--consume-native-success' or sdk['parentReturnStage']!=stage or sdk['embeddedParentExecutionId']!=parent_id or sdk['model']!=model or sdk['worker']!=pending['worker'] or
            sdk['receipt']!=pending['resultInput'] or sdk['resultSource']!=pending['resultSource'] or sdk['origin']!=origin or
            native_consumed['revision']!=2 or native_consumed['lifecycle']!=2 or native_consumed['payload']!=model['payload'] or native_consumed['terminalResult']!=model['terminalResult'] or
            len(sdk['products'])!=14 or len({p['name'] for p in sdk['products']})!=14 or any(not p['informationalVersion'].endswith('+'+process['sourceRevision']) for p in sdk['products'])):
        raise ValueError('The actual successful parent SDK receipt differs from the source-matched native/model consumption.')
    operations=[s['operation'] for s in sdk['steps']]
    if operations[:2]!=['queue-native-result','consume-native-result'] or any(o not in ('queue-native-result','consume-native-result','deliver-parent-result','consume-parent-result','queue-parent-settlement','consume-parent-settlement') for o in operations) or len(set(operations))!=len(operations):
        raise ValueError('The actual successful parent SDK changed its original delivery/consumption path.')
    if (('deliver-parent-result' in operations)!=consumed or ('consume-parent-settlement' in operations)!=settled or 'queue-parent-settlement' in operations and not settled):raise ValueError('The SDK delivery path differs from its selected actual parent stage.')
    for step in sdk['steps']:
        if 'delivery' in step:
            if step['delivery']['attemptedCount']!=1 or step['delivery']['deliveredCount']!=0:raise ValueError('The successful parent SDK acknowledged a source before the selected cut.')
        elif step['drain']['status']!=0:raise ValueError('The successful parent SDK did not use an accepted production recovery mailbox.')
    if (pending['resultSource']['status']!=3 or pending['resultSource']['deliveryFencingToken']<=0 or pending['resultSource']['deliveredAt'] is not None or
            pending['resultSource']['intent']!=last['resultSource']['intent'] or pending['resultSource']['outboxItemId']!=last['resultSource']['outboxItemId'] or
            parent['status']!=(3 if consumed else 0) or consumed and parent['deliveryFencingToken']<=0 or parent['deliveredAt'] is not None or
            parent['outboxItemId']!=final_parent['outboxItemId'] or parent['intent']!=final_parent['intent'] or sdk['parentResultSource']!=parent):
        raise ValueError('The actual successful parent cut lost its original unacknowledged source or positive delivery fence.')
    expected_inputs=[r for r in after['inputs'] if settled or r['inputKind']!='native-parent-settled']
    if sorted(embedding['inputs'],key=lambda r:r['inputId'])!=sorted(expected_inputs,key=lambda r:r['inputId']):raise ValueError('The actual successful parent cut changed original receipts or its exact settlement boundary.')
    settlements=[r for r in embedding['inputs'] if r['inputKind']=='native-parent-settled']
    if len(settlements)!=int(settled) or (sdk['parentSettlementReceipt'] is not None)!=settled:raise ValueError('The actual successful parent cut claimed the wrong settlement receipt.')
    invocation=model['payload']['nativeParent']['invocation']
    def command(value):
        if (value['inputKind']!='native-parent-settled' or value['executable']!=model['executable'] or value['materialIncarnationId']!=model['materialIncarnationId'] or
                value['authority']!=model['authority'] or value['tenantId']!=model['tenantId'] or value['payload']!={'invocation':invocation,'sourceOutboxItemId':parent['outboxItemId']}):
            raise ValueError('The actual queued parent settlement differs from its original source, pins or captured authority.')
    if settled:
        if sdk['parentSettlementReceipt']!=settlements[0] or sdk['parentSettlementCommand']['inputId']!=settlements[0]['inputId']:raise ValueError('The actual source-ACK cut lost its accepted exact parent settlement input.')
        command(sdk['parentSettlementCommand'])
    queued=embedding['queuedModelInputs']
    if (queued and (not consumed or settled)) or len(queued)>1:raise ValueError('The successful parent cut retained unexpected model progression work.')
    for work in queued:
        if work['commandKind']!=21 or work['workflowExecutionId']!=model['executionId'] or work['commandId']!=work['payload']['inputId']:raise ValueError('The successful parent cut queued an unrelated model command.')
        command(work['payload'])
    current,=[a for a in embedding['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
    old,=[a for a in before['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
    if (embedding['nativeParent']['workflowExecutionId']!=parent_id or embedding['nativeParent']['pinnedExecutable']!=before['nativeParent']['pinnedExecutable'] or
            embedding['nativeParent']['status'] not in ((1,3) if consumed else (1,)) or current['invocationId']!=old['invocationId'] or current['status']!=(4 if consumed else 3)):
        raise ValueError('The successful parent cut lost its actual native parent invocation, pin or consumption state.')
    if sdk['parentActivity']!=current:raise ValueError('The SDK parent activity differs from its actual checked typed consumption.')
    successors=[a for a in embedding['activities'] if a['execution']['executableNodeId']=='after']
    if consumed:
        if embedding['bookmarks'] or current['completion']['result']['inlineValue']!={'executionId':model['executionId'],'result':model['terminalResult']} or not any(r['status']==1 and r['registrationId']==before['bookmarks'][0]['bookmarkId'] and r['providerId']=='elsa.bpmn.native-parent.v1' and r['payloadType']['alias']=='Elsa.Activities.Bpmn.Models.RunBpmnCompletion' for r in current['triggerDeliveries']):raise ValueError('The successful parent cut lacks actual typed RunBpmn consumption.')
        if len(successors)>1 or any(a['status'] not in (0,1,2,3,4) for a in successors) or embedding['nativeParent']['status']==3 and process['embeddedParent']!='Root' and (len(successors)!=1 or successors[0]['status']!=4):raise ValueError('The successful native parent lost its original pending/completed successor responsibility.')
    elif embedding['bookmarks']!=before['bookmarks'] or successors:raise ValueError('The pending successful parent already consumed its original wait or successor.')
    if read('parent-pending-wire.json')!=read('finished-wire.json') or read('parent-pending.json')['run']['status']!='succeeded':raise ValueError('HTTP effects changed after the actual successful-parent crash cut.')
    if set(process['parentPendingStores'])!={'elsa','etl-runtime','etl-design'}:raise ValueError('The successful parent cut lacks all three actual coupled stores.')
    observations={**checked,'parentPending':pending}
    for store,snapshot in process['parentPendingStores'].items():
        file=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(file.read_bytes())
        if q.digest(file)!=snapshot['gzipSha256'] or hashlib.sha256(raw).hexdigest()!=snapshot['databaseSha256'] or snapshot['observer']['snapshotSha256']!=snapshot['databaseSha256'] or snapshot['observer']['readOnly'] is not True:raise ValueError('An actual successful-parent coupled store differs from its provider backup.')
        with tempfile.TemporaryDirectory(prefix='elsa-success-parent-read-') as temporary:
            database=Path(temporary)/'snapshot.sqlite';database.write_bytes(raw)
            with closing(sqlite3.connect(database.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                if c.execute('PRAGMA integrity_check').fetchall()!=[('ok',)]:raise ValueError('An actual successful-parent store failed integrity.')
                if store=='elsa':
                    physical_model_bridge(c,'parentPending',observations,origin,native_result,embedded=True,parent_settlement_owed=not settled)
                    row=c.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(key(parent_id),)).fetchone()
                    if row is None or row[0]!=embedding['nativeParent']['status'] or json.loads(row[1])['pinnedExecutable']!=encoded_tree(embedding['nativeParent']['pinnedExecutable']):raise ValueError('The physical successful parent lost its actual pin or consumption state.')
                    for activity in embedding['activities']:
                        row=c.execute('SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=? AND ActivityExecutionId=?',(key(parent_id),key(activity['invocationId']))).fetchone()
                        if row is None or row[0]!=('Scheduled','Running','Waiting','Suspended','Completed','Faulted','Cancelled','Recovered','Superseded')[activity['status']]:raise ValueError('The physical successful parent differs from its actual activity state.')
                        value=json.loads(row[1])
                        if activity['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn':
                            if consumed and (value['completion']['result']['inlineValue']!=activity['completion']['result']['inlineValue'] or not any(r['status']=='Consumed' and r['registrationId']==key(before['bookmarks'][0]['bookmarkId']) and r['providerId']==key('elsa.bpmn.native-parent.v1') and r['payloadType']['alias']==key('Elsa.Activities.Bpmn.Models.RunBpmnCompletion') for r in value['triggerDeliveries'])):raise ValueError('The physical successful parent lost its exact typed consumption.')
                            if not consumed and value['privateState']['value']['inlineValue']!=activity['privateState']['value']['inlineValue']:raise ValueError('The physical pending successful parent lost its original pinned wait.')
                    rows=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_model_input')]
                    if len(rows)!=len(expected_inputs):raise ValueError('The physical successful parent model has an unexpected settlement receipt.')
                    for receipt in embedding['inputs']:
                        row,=[r for r in rows if r['inputId']==key(receipt['inputId'])]
                        if row['contentFingerprint']!=key(receipt['contentFingerprint']) or row['result']!=receipt['result'] or row['acceptedRevision']!=receipt['acceptedRevision']:raise ValueError('The physical successful parent changed an exact accepted input.')
                    rows=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox')]
                    for source in embedding['sources']:
                        row,=[r for r in rows if r['outboxItemId']==key(source['outboxItemId'])]
                        if row['intent']['payload']!=source['intent']['payload'] or row['deliveredAt']!=source['deliveredAt'] or row['deliveryFencingToken']!=source['deliveryFencingToken'] or row['deliveryAttemptCount']!=source['deliveryAttemptCount'] or row['status']!=['Pending','Delivering','Delivered','FailedRetryable','FailedFinal'][source['status']]:raise ValueError('The physical successful parent source disagrees with actual acknowledgment truth.')
                    native_source,=[r for r in rows if r['outboxItemId']==key(pending['resultSource']['outboxItemId'])]
                    if native_source['deliveryFencingToken']!=pending['resultSource']['deliveryFencingToken'] or native_source['deliveryAttemptCount']!=pending['resultSource']['deliveryAttemptCount']:raise ValueError('The physical native result lost its original positive delivery fence.')
                    stored_queue=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_scheduler_work_item')]
                    actual_queue=[r for r in stored_queue if r['workflowExecutionId']==key(model['executionId'])]
                    if len(actual_queue)!=len(queued):raise ValueError('The physical successful parent model queue differs from its exact inspected settlement work.')
                    for work in queued:
                        row,=[r for r in actual_queue if r['workItemId']==key(work['workItemId'])]
                        if row['payload']!=work['payload'] or row['commandId']!=key(work['commandId']):raise ValueError('The physical successful parent model queue changed its original settlement command.')
                elif store=='etl-runtime':
                    row,=c.execute('SELECT InvocationJson,Status,AcceptedRecords,RejectedRecords,OutstandingRecords,OutstandingBytes FROM elsa_etl_runtime_runs').fetchall();invocation=json.loads(row[0])
                    if row[1:]!=(2,4,0,0,0) or c.execute('SELECT Outcome FROM elsa_etl_runtime_terminals').fetchall()!=[(0,)] or decoded(invocation['runId'])!=process['runId'] or decoded(invocation['resultAddress']['workflowExecutionId'])!=origin['workerExecutionId']:raise ValueError('The successful-parent ETL store lost its real terminal, output accounting or original result address.')



def verify(path):
    manifest=json.loads(path.read_text());root=path.parent
    if manifest.get('kind')=='actual-bpmn-etl-technical-fault-matrix':
        cases=manifest['cases'];parents={'Direct','Root','Sequence','Flowchart','CompensationScope'}
        if manifest.get('schemaVersion')!=1 or len(cases)!=5 or len({c['name'] for c in cases})!=5 or {c['parent'] for c in cases}!=parents:
            raise ValueError('The technical-fault matrix lacks every unique direct/native parent.')
        products=None;traces={}
        for case in cases:
            if case['manifest']!=case['name']+'/manifest.json':raise ValueError('A technical-fault case changed its evidence path.')
            file=q.evidence_file(root,case['manifest'])
            if q.digest(file)!=case['sha256']:raise ValueError('A technical-fault case manifest differs.')
            result=verify(file)
            if result['sourceRevision']!=manifest['sourceRevision'] or result.get('embeddedParent','Direct')!=case['parent'] or not result['technicalFinalFaultProved']:
                raise ValueError('A technical-fault case changed its executing source or actual parent.')
            capture=json.loads(file.read_text())['case'];process=json.loads((file.parent/capture/'process.json').read_text())
            if products is None:products=process['products']
            elif process['products']!=products:raise ValueError('The technical-fault cases differ in captured first-party host products.')
            for phase in ('before','finished'):
                model=json.loads((file.parent/capture/(phase+'-checked-model.json')).read_text())['model'];state=deepcopy(model['payload']['state'])
                for token in state['tokens']:
                    if token.get('producingWorkHandle')==model['executionId']:token['producingWorkHandle']='<model-execution>'
                if phase in traces and traces[phase]!=state:raise ValueError('The same-source direct/embedded technical-fault interpreter traces differ.')
                traces[phase]=state
        return {'sourceRevision':manifest['sourceRevision'],'actualTechnicalFinalFaultJourneys':5,'wholeStoresVerified':30,'actualWorkbenchProcesses':15,'actualHttpProcesses':5,
            'technicalFinalFaultProved':True,'technicalFaultRefusesCodedBusinessCatch':True,'sameSourceInterpreterTechnicalFaultTracesCompared':True,
            'modelHeldFaultRetryProved':False,'finalFaultRetryRefusalProved':False,'nativeCompositeFaultStatusDecisionResolved':False}
    if manifest.get('kind')=='actual-bpmn-etl-business-error-matrix':
        cases=manifest['cases'];parents=('Direct','Root','Sequence','Flowchart','CompensationScope');modes=('matching','different')
        if (manifest.get('schemaVersion')!=1 or len(cases)!=10 or len({c['name'] for c in cases})!=10 or
                {(c['parent'],c['boundary']) for c in cases}!={(p,m) for p in parents for m in modes}):
            raise ValueError('The business-error matrix lacks every unique direct/native-parent and matching-code case.')
        traces={};products=None
        for case in cases:
            if case['manifest']!=case['name']+'/manifest.json':raise ValueError('A business-error case changed its bound evidence path.')
            file=q.evidence_file(root,case['manifest'])
            if q.digest(file)!=case['sha256']:raise ValueError('A business-error case manifest differs.')
            result=verify(file)
            if (result['sourceRevision']!=manifest['sourceRevision'] or result['businessErrorBoundary']!=case['boundary'] or
                    result.get('embeddedParent','Direct')!=case['parent']):raise ValueError('A business-error case changed its source, native parent or actual boundary code.')
            capture=json.loads(file.read_text())['case']
            process=json.loads((file.parent/capture/'process.json').read_text())
            if products is None:products=process['products']
            elif process['products']!=products:raise ValueError('The business-error journeys differ in captured first-party host products.')
            for phase in ('before','finished'):
                model=json.loads((file.parent/capture/(phase+'-checked-model.json')).read_text())['model']
                state=deepcopy(model['payload']['state'])
                for token in state['tokens']:
                    if token.get('producingWorkHandle')==model['executionId']:token['producingWorkHandle']='<model-execution>'
                key_=(case['boundary'],phase)
                if key_ in traces and traces[key_]!=state:raise ValueError('The same-source direct/embedded business-error interpreter traces differ.')
                traces[key_]=state
        return {'sourceRevision':manifest['sourceRevision'],'actualMatchedAndUnmatchedBusinessErrorJourneys':10,'wholeStoresVerified':60,
            'actualWorkbenchProcesses':30,'actualHttpProcesses':10,'directAndAllNativeParentBusinessErrorDispositionProved':True,
            'sameSourceInterpreterBusinessErrorTracesCompared':True,'modelHeldFaultRetryProved':False,'nativeCompositeFaultStatusDecisionResolved':False}
    if manifest.get('kind') in ('actual-bpmn-pending-cancellation-matrix','actual-bpmn-consumed-before-ack-matrix'):
        consumed_matrix=manifest['kind']=='actual-bpmn-consumed-before-ack-matrix'
        if manifest.get('schemaVersion')!=1 or len(manifest['cases'])!=2 or {c['stage'] for c in manifest['cases']}!={'model-command','native-terminal'}:
            raise ValueError('The pending cancellation matrix lacks both actual process cuts.')
        for case in manifest['cases']:
            if case['manifest']!=case['stage']+'/manifest.json':raise ValueError('A pending cancellation case changed its bound evidence path.')
            file=q.evidence_file(root,case['manifest'])
            if q.digest(file)!=case['sha256']:raise ValueError('A pending cancellation case manifest differs.')
            result=verify(file)
            if result['sourceRevision']!=manifest['sourceRevision'] or result['pendingCancellationStage']!=case['stage'] or not result['postConsumptionPreAckCrashProved' if consumed_matrix else 'pendingCancellationCrashProved']:
                raise ValueError('A pending cancellation case changed its source or actual stage.')
        return {'sourceRevision':manifest['sourceRevision'],'actualPendingCancellationStages':['model-command','native-terminal'],'pendingCancellationCrashProved':not consumed_matrix,
            'wholeStoresVerified':24 if consumed_matrix else 18,'actualWorkbenchProcesses':8,'actualHttpProcesses':2,'actualSdkProcesses':2 if consumed_matrix else 0,'postConsumptionPreAckCrashProved':consumed_matrix,
            'runBpmnEtlIntegrationProved':False,'modelHeldFaultRetryProved':False}
    if manifest.get('kind')=='actual-native-runbpmn-etl-success-parent-matrix':
        cases=manifest['cases'];parents=('Root','Sequence','Flowchart','CompensationScope');stages=('pending','consumed','settled')
        if manifest.get('schemaVersion')!=1 or len(cases)!=12 or {(c['parent'],c['stage']) for c in cases}!={(p,s) for p in parents for s in stages} or len({c['name'] for c in cases})!=12:
            raise ValueError('The successful parent matrix lacks every unique actual parent and consumption/settlement/ACK stage.')
        for case in cases:
            if case['manifest']!=case['name']+'/manifest.json':raise ValueError('A successful parent case changed its bound evidence path.')
            file=q.evidence_file(root,case['manifest'])
            if q.digest(file)!=case['sha256']:raise ValueError('A successful parent case manifest differs.')
            result=verify(file)
            if result['sourceRevision']!=manifest['sourceRevision'] or result['embeddedParent']!=case['parent'] or result['successfulParentResultCrashStage']!=case['stage']:
                raise ValueError('A successful parent case changed its source, actual parent or selected crash stage.')
        return {'sourceRevision':manifest['sourceRevision'],'actualSuccessfulNativeParentReturnJourneys':[{'parent':c['parent'],'stage':c['stage']} for c in cases],
            'wholeStoresVerified':108,'actualWorkbenchProcesses':48,'actualSdkProcesses':12,'actualHttpProcesses':12,
            'runBpmnEtlIntegrationProved':True,'successfulParentResultCrashProved':True,'allNativeParentSuccessConsumptionSettlementAndPreAckCrashProved':True,'modelHeldFaultRetryProved':False}
    if manifest.get('kind')=='actual-native-runbpmn-etl-pending-parent-matrix':
        cases=manifest['cases']
        if manifest.get('schemaVersion')!=1 or len(cases)!=4 or {c['parent'] for c in cases}!={'Root','Sequence','Flowchart','CompensationScope'} or len({c['name'] for c in cases})!=4:
            raise ValueError('The pending-parent matrix lacks all unique native parent shapes.')
        for case in cases:
            if case['manifest']!=case['name']+'/manifest.json':raise ValueError('A pending-parent case changed its bound evidence path.')
            file=q.evidence_file(root,case['manifest'])
            if q.digest(file)!=case['sha256']:raise ValueError('A pending-parent case manifest differs.')
            result=verify(file)
            if result['sourceRevision']!=manifest['sourceRevision'] or result['embeddedParent']!=case['parent'] or not result['nativeParentToPipelineCancellationProved'] or not result['pendingParentResultCrashProved']:
                raise ValueError('A pending-parent case changed its source, native parent or actual crash cut.')
        return {'sourceRevision':manifest['sourceRevision'],'actualCancelledNativeParentShapes':[c['parent'] for c in cases],
            'wholeStoresVerified':9*len(cases),'actualWorkbenchProcesses':16,'actualSdkProcesses':4,'actualHttpProcesses':4,
            'runBpmnEtlIntegrationProved':True,'pendingParentResultCrashProved':True,'allNativeParentCancellationPendingReturnProved':True,
            'successParentResultCrashProved':False,'modelHeldFaultRetryProved':False}
    if manifest.get('kind')=='actual-native-runbpmn-etl-matrix':
        if manifest.get('schemaVersion')!=1 or len(manifest['cases'])!=8 or {(c['parent'],c['cancel']) for c in manifest['cases']}!={(p,c) for p in ('Root','Sequence','Flowchart','CompensationScope') for c in (False,True)} or len({c['name'] for c in manifest['cases']})!=8:raise ValueError('The embedded ETL matrix lacks all unique parent/completion/cancellation cases.')
        results=[]
        for case in manifest['cases']:
            if case['manifest']!=case['name']+'/manifest.json':raise ValueError('An embedded ETL case changed its bound evidence path.')
            file=q.evidence_file(root,case['manifest'])
            if q.digest(file)!=case['sha256']:raise ValueError('An actual embedded ETL case manifest differs.')
            result=verify(file)
            if result['sourceRevision']!=manifest['sourceRevision'] or result['embeddedParent']!=case['parent'] or result['nativeParentToPipelineCancellationProved'] is not case['cancel'] or not result['runBpmnEtlIntegrationProved']:
                raise ValueError('An embedded ETL case changed its source, actual parent or cancellation.')
            results.append({'parent':case['parent'],'cancel':case['cancel']})
        direct,compared=direct_embedded_trace_comparison(root,manifest)
        return {'sourceRevision':manifest['sourceRevision'],'actualRunBpmnEtlJourneys':results,'wholeStoresVerified':6*len(results)+direct['wholeStoresVerified'],'runBpmnEtlIntegrationProved':True,
            'actualDirectSuccessCounterpartVerified':True,'sameSourceInterpreterSuccessTraceComparedParents':compared,
            'allNativeParentCompletionAndCancellationProved':{(r['parent'],r['cancel']) for r in results}=={(p,c) for p in ('Root','Sequence','Flowchart','CompensationScope') for c in (False,True)},
            'pendingParentResultCrashProved':False,'modelHeldFaultRetryProved':False}
    business=manifest.get('kind') in ('actual-direct-bpmn-native-etl-business-error','actual-native-runbpmn-etl-business-error')
    technical=manifest.get('kind') in ('actual-direct-bpmn-native-etl-technical-fault','actual-native-runbpmn-etl-technical-fault')
    boundary=manifest.get('businessErrorBoundary')
    if ((business or technical) and boundary not in ('matching','different')) or (not business and not technical and boundary is not None) or technical and boundary!='matching':raise ValueError('Unknown actual business-error boundary qualification.')
    embedded=manifest.get('kind') in ('actual-native-runbpmn-etl-http-recovery','actual-native-runbpmn-etl-parent-cancellation','actual-native-runbpmn-etl-business-error','actual-native-runbpmn-etl-technical-fault')
    counterpart=manifest.get('kind')=='actual-direct-bpmn-native-etl-success-counterpart'
    parent_cancel=manifest.get('kind')=='actual-native-runbpmn-etl-parent-cancellation'
    success_parent=manifest.get('successfulParentResultCrashStage')
    if success_parent is not None and (success_parent not in ('pending','consumed','settled') or not embedded or parent_cancel):raise ValueError('Unknown actual successful parent-result stage.')
    parent_pending=manifest.get('pendingParentResultCrash') is True
    if parent_pending and not parent_cancel:raise ValueError('A pending-parent result crash requires actual embedded parent cancellation.')
    model_cancel=manifest.get("kind")=="actual-direct-bpmn-native-etl-model-cancellation"
    pipeline_cancel=manifest.get("kind")=="actual-direct-bpmn-native-etl-pipeline-cancellation"
    worker_cancel=manifest.get("kind")=="actual-direct-bpmn-native-etl-worker-cancellation"
    cancelling=pipeline_cancel or worker_cancel or model_cancel or parent_cancel
    direct=embedded or counterpart or cancelling or business or technical or manifest.get("kind")=="actual-direct-bpmn-native-etl-recovery"
    if manifest.get("schemaVersion")!=1 or manifest.get("kind") not in ("actual-native-etl-recovery-reference","actual-direct-bpmn-native-etl-recovery","actual-direct-bpmn-native-etl-success-counterpart","actual-direct-bpmn-native-etl-pipeline-cancellation","actual-direct-bpmn-native-etl-worker-cancellation","actual-direct-bpmn-native-etl-model-cancellation","actual-native-runbpmn-etl-http-recovery","actual-native-runbpmn-etl-parent-cancellation","actual-direct-bpmn-native-etl-business-error","actual-native-runbpmn-etl-business-error","actual-direct-bpmn-native-etl-technical-fault","actual-native-runbpmn-etl-technical-fault"):raise ValueError("Unknown ETL recovery reference evidence.")
    source=manifest["sourceRevision"];assets=manifest["assets"]
    stage=manifest.get('pendingCancellationStage');pending_crash=stage is not None;post_ack=manifest.get('postConsumptionPreAck') is True
    if post_ack and not pending_crash:raise ValueError('A consumed-before-ACK case requires its actual pending cancellation source.')
    if pending_crash and stage!=('model-command' if model_cancel else 'native-terminal' if worker_cancel else None):raise ValueError('Unknown pending cancellation stage.')
    if len(source)!=40 or any(c not in "0123456789abcdef" for c in source):raise ValueError("The ETL reference lacks an exact source.")
    expected_cases={manifest["case"]:("BpmnModel",business)} if embedded or counterpart or business or technical else {"model-cancel":("BpmnModel",False)} if model_cancel else {"native-cancel":("BpmnModel",False)} if worker_cancel else {"direct-cancel":("BpmnModel",False)} if pipeline_cancel else {"direct-success":("BpmnModel",False),"direct-business":("BpmnModel",True)} if direct else {"sequence":("Sequence",False),"flowchart":("Flowchart",False),"worker-business":("RunEtl",True)}
    names={"before.json","recovered.json","finished.json","retained.json","before-wire.json","finished-wire.json","retained-wire.json","safe-errors.json","process.json"}
    names|={phase+"-"+store+".sqlite.gz" for phase in ("before","finished") for store in ("elsa","etl-runtime","etl-design")}
    if direct:names|={phase+"-checked-model.json" for phase in ("before","finished","retained")}
    if pipeline_cancel:names.add("cancel-response.json")
    if worker_cancel:names.add("native-cancel-response.json")
    if model_cancel:names|={"model-cancel-response.json","model-cancel-command.json"}
    if embedded:names|={p+'-checked-embedding.json' for p in ('before','finished','retained')}
    if technical:names|={'lookup-source.csv','lookup-snapshot.csv','snapshot-unavailability.json'}
    if parent_cancel:names.add('native-parent-cancel-response.json')
    if parent_pending or success_parent is not None:names|={'sdk-consume.json','parent-pending.json','parent-pending-wire.json','parent-pending-checked-model.json','parent-pending-checked-embedding.json'}|{'parent-pending-'+s+'.sqlite.gz' for s in ('elsa','etl-runtime','etl-design')}
    if pending_crash:names|={'pending.json','pending-checked.json','pending-wire.json'}|{'pending-'+s+'.sqlite.gz' for s in ('elsa','etl-runtime','etl-design')}
    if post_ack:names|={'sdk-consume.json','consumed.json','consumed-checked.json','consumed-wire.json'}|{'consumed-'+s+'.sqlite.gz' for s in ('elsa','etl-runtime','etl-design')}
    declared={asset["path"] for asset in assets}
    if len(declared)!=len(assets) or declared!={case+"/"+name for case in expected_cases for name in names}:raise ValueError("The ETL recovery bundle lacks its exact actual assets.")
    for asset in assets:
        if q.digest(q.evidence_file(root,asset["path"]))!=asset["sha256"]:raise ValueError("An actual ETL recovery asset differs.")
    results=[]
    for case,(parent,business_error) in expected_cases.items():
        def read(name):return json.loads(q.evidence_file(root,case+"/"+name).read_text())
        process=read("process.json");products=process["products"]
        if process.get('businessErrorBoundary')!=boundary:raise ValueError('The actual business-error boundary differs from its declared case.')
        if process.get('technicalFault',False) is not technical or technical and (process.get('technicalFaultPolicy')!='FinalFault' or process.get('technicalFaultSource')!='retained-lookup-snapshot-unavailable'):raise ValueError('The actual technical-fault classification or default policy differs.')
        if (business or technical) and process.get('resumptionSweepIntervalSeconds')!=.2:raise ValueError('The actual error recovery lost its declared production resumption configuration.')
        if (process["sourceRevision"]!=source or process["parent"]!=parent or process["businessError"] is not business_error or process["predecessorExitCode"]!=-9 or
                [item["role"] for item in process["processes"]]!=(["http-fixture","workbench","workbench","consumption-sdk","workbench","workbench"] if post_ack or parent_pending or success_parent is not None else ["http-fixture"]+["workbench"]*(4 if pending_crash else 3)) or len({item["processId"] for item in process["processes"]})!=(6 if post_ack or parent_pending or success_parent is not None else 5 if pending_crash else 4)):raise ValueError("The actual ETL reference lacks its distinct process-loss/restart provenance.")
        if products["sourceCommit"]!=source or products["metadataOnly"] is not True or len(products["actualProductVersions"])!=(12 if direct else 6) or any(not item["productVersion"].endswith("+"+source) for item in products["actualProductVersions"]):raise ValueError("The actual Workbench and ETL products differ from their declared source.")
        before=read("before.json");finished=read("finished.json");recovered=read("recovered.json")
        before_wire=read("before-wire.json");wire=read("finished-wire.json")
        if technical:
            availability=read('snapshot-unavailability.json');source_file=q.evidence_file(root,case+'/lookup-source.csv');snapshot_file=q.evidence_file(root,case+'/lookup-snapshot.csv')
            content=q.digest(source_file)
            if (availability['kind']!='actual-owned-lookup-snapshot-unavailability' or availability['schemaVersion']!=1 or availability['sourceRevision']!=source or
                    q.digest(snapshot_file)!=content or availability['sourceSha256']!=content or availability['snapshotSha256Before']!=content or
                    availability['snapshotSha256WhileUnavailable']!=content or availability['restoredSha256']!=content or
                    re.fullmatch('[a-f0-9]{64}\\.csv',availability['snapshotKey']) is None or availability['unavailableKey']!=availability['snapshotKey']+'.unavailable' or
                    availability['predecessorExitCode']!=-9 or not availability['sourceStillAvailable'] or not availability['originalSnapshotReadPathMissing'] or
                    not availability['restoredAfterOwnedHostsStopped'] or instant(availability['inducedAtUtc'])<=instant(process['cutAtUtc']) or
                    instant(availability['restoredAtUtc'])<=instant(finished['run']['terminalResult']['settledAt'])):
                raise ValueError('The actual technical fault lacks its unchanged, temporarily unavailable retained lookup snapshot.')
        bridge=model_bridge(read,process,before,finished,business_error,cancelling,model_cancel or parent_cancel,embedded) if direct else None
        if embedded:embedded_etl_parent(read,q.evidence_file(root,case+'/process.json').parent,process,bridge,parent_cancel)
        if success_parent is not None:successful_embedded_parent_return(read,q.evidence_file(root,case+'/process.json').parent,process,bridge)
        if parent_pending:pending_embedded_parent_return(read,q.evidence_file(root,case+'/process.json').parent,process,bridge)
        if pending_crash:pending_cancellation(read,q.evidence_file(root,case+'/process.json').parent,process,bridge,model_cancel)
        if post_ack:consumed_before_ack(read,q.evidence_file(root,case+'/process.json').parent,process,bridge,model_cancel)
        if finished!=read("retained.json") or wire!=read("retained-wire.json"):raise ValueError("The terminal native/ETL views or physical effects changed after another process.")
        identity=process["workflowExecutionId"];run_id=process["runId"];selection=process["selection"]
        old={node(item):item for item in before["native"]["activities"]};current={node(item):item for item in finished["native"]["activities"]}
        if before["native"]["instance"]["status"]!="Running" or before["run"]["status"]!="running" or "after" in old or old["etl"]["status"]!="Suspended":raise ValueError("The actual predecessor was not held at the real RunEtl wait.")
        terminal_status="Cancelled" if cancelling else "Faulted" if technical else "Completed"
        if current["etl"]["activityExecutionId"]!=old["etl"]["activityExecutionId"] or len(current)!=(1 if parent in ("RunEtl","BpmnModel") else 3) or any(item["status"]!=terminal_status for item in current.values()) or current["etl"]["outcomeNames"]!=([] if cancelling or technical else ["BusinessError" if business_error else "Done"]):raise ValueError("The actual native continuation changed its invocation or typed outcome.")
        if finished["native"]["instance"]["status"]!=terminal_status or not technical and finished["native"]["incidents"]:raise ValueError("The actual native parent did not settle without incidents.")
        if technical:
            incidents=finished['native']['incidents']
            if (len(incidents)!=1 or incidents[0]['activityExecutionId']!=current['etl']['activityExecutionId'] or incidents[0]['workflowExecutionId']!=process['workflowExecutionId'] or
                    incidents[0]['failureType']!='ActivityReturnedFault' or incidents[0]['status']!='Blocking' or incidents[0]['resolutionOutcome']['actionKind']!='FaultWorkflow'):
                raise ValueError('The actual RunEtl technical fault lost its default native incident.')
        pin={name:before["native"]["instance"][name] for name in ("artifactId","artifactHash","artifactVersion","definitionId","definitionVersionId")}
        for view in (before,recovered,finished):
            if (view["native"]["instance"]["workflowExecutionId"]!=identity or view["run"]["runId"]!=run_id or view["run"]["selection"]!=selection or
                    any(view["native"]["instance"][name]!=value for name,value in pin.items())):raise ValueError("An actual ETL/native identity or full pin changed on recovery.")
        retry=next((item["http"]["fetch"] for item in before["partitions"]["items"] if item["http"]["fetch"] and item["http"]["fetch"]["lastStatusCode"]==429),None)
        if retry is None or retry["phase"]!="waiting" or retry["attemptCount"]!=1 or instant(retry["notBefore"])<=instant(process["cutAtUtc"]):raise ValueError("The actual crash cut lacks its original future 429 retry.")
        if cancelling:
            response=read("native-parent-cancel-response.json" if parent_cancel else "model-cancel-response.json" if model_cancel else "native-cancel-response.json" if worker_cancel else "cancel-response.json")
            accepted_request=(response["inputId"]=="cancel-actual-bpmn-etl" and response["result"]["status"]=="Accepted" and response["result"]["lifecycle"]=="TerminalPendingSettlement") if model_cancel else response["submissionDisposition"]=="Accepted" if worker_cancel or parent_cancel else response["cancellationAccepted"] and response["terminalResult"]["outcome"]=="cancelled"
            if (process.get("nativeParentCancel" if parent_cancel else "modelCancel" if model_cancel else "nativeCancel" if worker_cancel else "pipelineCancel") is not True or process.get("retryAfterSeconds")!=60 or recovered["run"]["status"]!="running" or
                    not accepted_request or
                    instant(process["observedCancelledAfterRetryDeadlineAtUtc"])<=instant(retry["notBefore"])):
                raise ValueError("The actual pipeline cancellation lacks its accepted request and post-deadline observation.")
        if not any(item["http"]["exhausted"] and item["owedBatch"] is None for item in before["partitions"]["items"]):raise ValueError("The sibling had not completed before the actual crash cut.")
        accepted,rejected=(2,0) if cancelling or technical else (3,1) if business_error else (4,0);outcome="cancelled" if cancelling else "faulted" if technical else "businessError" if business_error else "succeeded"
        result=finished["run"];rows=finished["partitions"]["items"]
        if (result["formatVersion"]!=2 or result["status"]!=outcome or result["terminalResult"]["outcome"]!=outcome or result["acceptedRecords"]!=accepted or result["rejectedRecords"]!=rejected or result["filteredRecords"]!=0 or
                result["outstandingRecords"]!=0 or result["outstandingBytes"]!=0 or len(rows)!=2 or any(item["owedBatch"] is not None for item in rows) or
                not cancelling and not technical and any(not item["http"]["exhausted"] or item["http"]["pageSequence"]!=1 for item in rows)):raise ValueError("The actual ETL terminal or partition accounting differs.")
        if technical:
            failed=[r['http']['fetch'] for r in rows if r['partitionOrdinal']==1]
            if (result['terminalResult']['diagnostic']['code']!='etl.snapshot-read-failed' or len(failed)!=1 or failed[0]!=retry):
                raise ValueError('The actual snapshot fault lost its original waiting HTTP retry state.')
        if business_error and result["terminalResult"]["diagnostic"]["code"]!="etl.rows-rejected":raise ValueError("The actual ETL business-error key differs.")
        if wire["overflow"] or before_wire["overflow"]:raise ValueError("The physical HTTP observations exceeded their bounds.")
        earlier=[item for item in before_wire["requests"] if item.get("shard")=="shard-b"];later=[item for item in wire["requests"] if item.get("shard")=="shard-b"]
        if cancelling:
            if len(earlier)!=1 or later!=earlier or later[0]["status"]!=429:raise ValueError("The physically cancelled HTTP shard resumed its saved retry.")
        elif technical and (len(earlier)!=1 or wire!=before_wire or instant(process['observedFaultedAfterRetryDeadlineAtUtc'])<=instant(retry['notBefore'])):raise ValueError('The actual faulted HTTP wait revived at its original retry deadline.')
        elif not technical and (len(earlier)!=1 or len(later)!=3 or [item["status"] for item in later]!=[429,200,200] or later[0]!=earlier[0] or later[1]["receivedAtMs"]-later[0]["receivedAtMs"]<20000):raise ValueError("The physical source retried early or changed its saved page sequence.")
        records=[record for post in wire["outputPosts"] for record in post["records"]];expected_records={1:"Ada",2:"Grace",3:"Linus"}
        if cancelling or technical:expected_records.pop(3)
        elif not business_error:expected_records[4]="Margaret"
        if {item["id"]:item["name"] for item in records}!=expected_records or any(not post["hasIdempotencyKey"] for post in wire["outputPosts"]):raise ValueError("The physical HTTP receiver lacks the actual mapped effects.")
        errors=read("safe-errors.json")
        if errors!=([{"recordOrdinal":1,"code":"etl.invalid-value","field":"id"}] if business_error else []):raise ValueError("The actual safe-error file output differs.")
        invocation=None;native_bookmark=None;native_consumption=[];native_cancel_sources=[]
        for phase,stores in (("before",process["beforeStores"]),("finished",process["finishedStores"])):
            for store,snapshot in stores.items():
                file=q.evidence_file(root,case+"/"+snapshot["file"]);raw=gzip.decompress(file.read_bytes())
                if q.digest(file)!=snapshot["gzipSha256"] or hashlib.sha256(raw).hexdigest()!=snapshot["databaseSha256"] or snapshot["observer"]["snapshotSha256"]!=snapshot["databaseSha256"] or snapshot["observer"]["readOnly"] is not True:raise ValueError("A provider-observed ETL snapshot differs from its actual bytes.")
                with tempfile.TemporaryDirectory(prefix="elsa-etl-verify-") as temporary:
                    database=Path(temporary)/"snapshot.sqlite";database.write_bytes(raw)
                    with closing(sqlite3.connect(database.as_uri()+"?mode=ro&immutable=1",uri=True)) as connection:
                        if connection.execute("PRAGMA integrity_check").fetchall()!=[("ok",)]:raise ValueError("An actual ETL snapshot failed integrity verification.")
                        if store=="elsa":
                            if post_ack and phase=='finished' and connection.execute('SELECT COUNT(*) FROM elsa_runtime_scheduler_work_item').fetchone()[0]:
                                raise ValueError('The recovered pre-ACK journey retained native/model scheduler work.')
                            if bridge is not None:physical_model_bridge(connection,phase,*bridge,model_cancel=model_cancel or parent_cancel,embedded=embedded)
                            if business or technical:physical_business_error_boundary(connection,phase,bridge[0],bridge[1],boundary,technical)
                            if model_cancel and phase=="finished":physical_model_cancellation(connection,read,*bridge)
                            if technical and phase=='finished':
                                expected_incidents=finished['native']['incidents']+(finished['embedding']['incidents'] if embedded else [])
                                incident_rows=[json.loads(r[0]) for r in connection.execute('SELECT ContentJson FROM elsa_runtime_incident_state')]
                                if len(incident_rows)!=len(expected_incidents) or {r['incidentId'] for r in incident_rows}!={key(i['incidentId']) for i in expected_incidents}:
                                    raise ValueError('The physical technical-fault incident inventory differs from its actual native owners.')
                                for i in expected_incidents:
                                    row,=[r for r in incident_rows if r['incidentId']==key(i['incidentId'])]
                                    if (row['workflowExecutionId']!=key(i['workflowExecutionId']) or row['activityExecutionId']!=key(i['activityExecutionId']) or row['status']!=i['status'] or
                                            row['failureType']!=key('ActivityReturnedFault') or row['resolutionOutcome']['actionKind']!=key(i['resolutionOutcome']['actionKind'])):
                                        raise ValueError('The physical technical-fault incident lost its exact native disposition.')
                            row=connection.execute("SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?",(key(identity),)).fetchone()
                            if row is None or row[0]!=(1 if phase=="before" else 5 if cancelling else 4 if technical else 3) or json.loads(row[1])["pinnedExecutable"]!={name:key(value) for name,value in pin.items()}:raise ValueError("The physical native ETL workflow lost its exact pin or state.")
                            bookmarks=connection.execute("SELECT BookmarkId FROM elsa_runtime_bookmark_state WHERE WorkflowExecutionId=?",(identity,)).fetchall()
                            if len(bookmarks)!=(1 if phase=="before" else 0):raise ValueError("The physical RunEtl wait was recreated or not consumed.")
                            if phase=="before":native_bookmark=bookmarks[0][0]
                            elif pipeline_cancel:
                                activity,=connection.execute("SELECT ContentJson FROM elsa_runtime_activity_execution_state").fetchall()
                                native_consumption=json.loads(activity[0]).get("triggerDeliveries") or []
                            if phase=="finished" and (worker_cancel or model_cancel or parent_cancel):
                                native_cancel_sources=[json.loads(row[0]) for row in connection.execute("SELECT ContentJson FROM elsa_runtime_post_commit_outbox")
                                    if json.loads(row[0])["intent"]["kind"]==key("Elsa.Etl.Cancel.v1")]
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
                            if phase=="finished" and (row[1]!=(5 if cancelling else 4 if technical else 3 if business_error else 2) or row[2:]!=(accepted,rejected,0,0) or terminal!=[(3 if cancelling else 2 if technical else 1 if business_error else 0,)] or len(outbox)!=1 or outbox[0][0]<1 or outbox[0][1] is None or outbox[0][2] is None):raise ValueError("The physical ETL terminal or owed native result did not settle.")
                            if phase=="finished" and (worker_cancel or model_cancel or parent_cancel) and outbox[0][1]!=2:
                                raise ValueError("The native-origin ETL result lacks its actual proven-parent-terminal disposition.")
        if (native_bookmark!=decoded(invocation["resultAddress"]["bookmarkId"]) or native_bookmark!=decoded(invocation["resultAddress"]["registrationId"]) or
                decoded(invocation["resultAddress"]["nodeId"])!=old["etl"]["executableNodeId"]):raise ValueError("The ETL result address did not retain its actual native bookmark.")
        if pipeline_cancel and not any(receipt["status"]=="Consumed" and receipt["registrationId"]==key(decoded(invocation["resultAddress"]["registrationId"])) and
                receipt["providerId"]==key(decoded(invocation["resultAddress"]["providerId"])) and receipt["payloadType"]["alias"]==key("Elsa.Etl.Workflows.Runtime.Models.RunEtlCompletion")
                for receipt in native_consumption):raise ValueError("The cancelled native root lacks its exact typed ETL consumption receipt.")
        if worker_cancel or model_cancel or parent_cancel:
            if len(native_cancel_sources)!=1:raise ValueError("The native cancellation checkpoint lacks its actual owed ETL cancel source.")
            cancel=native_cancel_sources[0];result_source=bridge[0]["finished"]["resultSource"]
            cancel_id=base64.b64decode(cancel["outboxItemId"]).decode("utf-16le");intent_id=base64.b64decode(cancel["intent"]["intentId"]).decode("utf-16le")
            result_suffix=":"+result_source["intent"]["intentId"];cancel_suffix=":"+intent_id
            if (cancel["status"]!="Delivered" or cancel["deliveredAt"] is None or cancel["intent"]["payload"]!=invocation or
                    cancel["intent"]["workflowExecutionId"]!=key(identity) or not cancel_id.endswith(cancel_suffix) or
                    not result_source["outboxItemId"].endswith(result_suffix) or
                    cancel_id[:-len(cancel_suffix)]!=result_source["outboxItemId"][:-len(result_suffix)]):
                raise ValueError("The native cancellation lost its exact ETL address or same-checkpoint result responsibility.")
        results.append({"case":case,"acceptedRecords":accepted,"rejectedRecords":rejected,"physicalOutputRecords":len(records),"businessErrorPreserved":business_error})
    result={"actualReferenceJourneys":results,"wholeStoresVerified":len(expected_cases)*(12 if post_ack else 9 if pending_crash or parent_pending or success_parent is not None else 6),"sourceRevision":source,"directBpmnClosedWorkerIntegrationProved":direct and not embedded,
        "runBpmnEtlIntegrationProved":embedded,"modelHeldFaultRetryProved":False}
    result.update(pendingCancellationCrashProved=pending_crash and not post_ack,pendingCancellationStage=stage,postConsumptionPreAckCrashProved=post_ack)
    if embedded:result.update(embeddedParent=process['embeddedParent'],nativeParentToPipelineCancellationProved=parent_cancel,pendingParentResultCrashProved=parent_pending)
    if success_parent is not None:result['successfulParentResultCrashStage']=success_parent
    if counterpart:result['directSuccessCounterpartVerified']=True
    if business:result['businessErrorBoundary']=boundary
    if technical:result.update(technicalFinalFaultProved=True,finalFaultRetryRefusalProved=False)
    if not direct:result["directOrRunBpmnEtlIntegrationProved"]=False
    if cancelling:result.update(pipelineOriginatedCancellationProved=pipeline_cancel,nativeWorkerOriginatedCancellationProved=worker_cancel,modelToWorkerCancellationProved=model_cancel,modelCancellationBeforeAdmissionProved=False)
    return result


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("--manifest",type=Path,required=True)
    print(json.dumps(verify(parser.parse_args().manifest.resolve(strict=True)),indent=2))
