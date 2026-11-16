#!/usr/bin/env python3
"""Verify actual cancellation of a held service fault without reviving its worker."""
import argparse
from collections import Counter
from contextlib import closing
import gzip
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sqlite3
import tempfile

_spec=importlib.util.spec_from_file_location('held_retry',Path(__file__).with_name('verify_held_service_retry.py'))
h=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(h)
v=h.v;q=h.q;require=h.require


def verify_case(root,parent,source):
    read=lambda name:json.loads((root/name).read_text());p=read('process.json');embedded=parent!='direct';products=p['products']
    require(p['kind']=='actual-model-held-service-cancellation' and p['schemaVersion']==1 and p['sourceRevision']==source and p['parent']=='BpmnModel' and
        p['embeddedParent']=={'root':'Root','sequence':'Sequence','flowchart':'Flowchart','compensationscope':'CompensationScope'}.get(parent) and
        p['technicalFaultPolicy']=='HoldForRetry' and p['maxServiceAttempts']==2 and p['resumptionSweepIntervalSeconds']==.2 and p['retryAfterSeconds']==20 and
        p['predecessorExitCode']==p['heldPredecessorExitCode']==-9 and [x['role'] for x in p['processes']]==['http-fixture']+['workbench']*4 and
        len({x['processId'] for x in p['processes']})==5 and len({x['controllerProcessId'] for x in p['processes']})==1 and
        products['sourceCommit']==source and products['metadataOnly'] and len(products['actualProductVersions'])==12 and
        all(x['productVersion'].endswith('+'+source) for x in products['actualProductVersions']), 'The held cancellation lost its real process-loss, parent, pinned policy or source provenance.')
    before=read('before.json');held=read('held.json');final=read('finished.json');checked={x:read(x+'-checked-model.json') for x in ('before','held','finished','retained')}
    b,old,after=(checked[x] for x in ('before','held','finished'));bm,hm,fm=(x['model'] for x in (b,old,after));origin=b['worker']['modelWorkOrigin']
    require(held==read('held-after-restart.json') and final==read('retained.json') and after==checked['retained'], 'The held cancellation changed after another standard process.')
    failure,=hm['payload']['failures'];retired,=fm['payload']['failures'];result,=hm['payload']['nativeResults'];incident,=held['incidents']['items']
    command=read('held-cancel-command.json');refusal=read('retired-retry-refusal.json');pin={k:p['publication'][k] for k in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')}
    for name,item in checked.items():
        model=item['model'];worker=item['worker'];start=item['startSource']
        require(item['readOnly'] and model['executionId']==p['modelExecutionId'] and model['executable']==p['modelPublication']['executable'] and
            model['materialIncarnationId']==p['modelPublication']['incarnationId'] and worker['workflowExecutionId']==p['workflowExecutionId'] and worker['modelWorkOrigin']==origin and
            worker['pinnedExecutable']==pin and worker['authority']==model['authority'] and worker['parentWorkflowExecutionId'] is None and
            origin['executable']==model['executable'] and origin['materialIncarnationId']==model['materialIncarnationId'] and origin['workerExecutable']==pin and origin['scope']==model['scope'] and
            origin['executionId']==model['executionId'] and origin['workerExecutionId']==worker['workflowExecutionId'] and start['status']==2 and
            start['outboxItemId']==origin['sourceOutboxItemId'] and start['intent']['payload']==origin and start['intent']['kind']=='Elsa.Bpmn.StartNativeWork.v1',
            'The cancelled held work lost its complete immutable origin, worker pin or authoritative start source.')
    require(bm['revision']==1 and bm['lifecycle']==0 and bm['payload']['failures']==[] and b['worker']['status']==1 and b['resultInput'] is None and b['resultSource'] is None and
        hm['revision']==2 and hm['lifecycle']==1 and hm['terminalResult'] is None and hm['payload']['state']==bm['payload']['state']==failure['semanticState'] and
        hm['payload']['work']==bm['payload']['work'] and len(hm['payload']['work'])==1 and hm['payload']['work'][0]['attemptNumber']==1 and
        failure['status']=='Open' and failure['attemptNumber']==1 and failure['maxAttempts']==2 and failure['failedAttemptId']==origin['attemptId'] and failure['invocationId']==origin['invocationId'] and
        result['origin']==origin and result['status']==4 and result['faultCode']=='etl.snapshot-read-failed' and result['completion'] is None and
        old['worker']==after['worker'] and old['worker']['status']==4, 'The held failure lost its active semantic token or actual original worker fault.')
    require(fm['revision']==3+int(embedded) and fm['lifecycle']==3 and fm['terminalResult']=={'status':'Cancelled','reason':'Retire the actual held service failure.'} and
        fm['payload']['formatVersion']==hm['payload']['formatVersion']==bm['payload']['formatVersion']==(8 if embedded else 7) and
        fm['payload']['work']==[] and fm['payload']['state']==failure['semanticState'] and fm['payload']['nativeResults']==[result] and fm['payload'].get('cancellations') is None and
        retired=={**failure,'status':'Retired','resolutionInputId':'cancel-actual-held-service'} and final['run']==held['execution']['run'] and final['native']==held['execution']['native'] and
        final['run']['status']=='faulted' and final['run']['terminalResult']['diagnostic']['code']=='etl.snapshot-read-failed' and final['model']['status']=='TerminalSettled',
        'Held cancellation revived or rewrote its failed worker, semantic state or original ETL truth.')
    h.source_and_receipt(old,result,2);h.source_and_receipt(after,result,2)
    require(command['request']=={'requestId':'cancel-actual-held-service','reason':'Retire the actual held service failure.'} and not command['accepted']['pending'] and
        command['accepted']['result']=={'status':'Accepted','code':None,'revision':3,'lifecycle':'TerminalPendingSettlement' if embedded else 'TerminalSettled'} and
        command['retiredIncident']=={**incident,'status':'Retired'} and read('retained-incidents.json')=={'items':[command['retiredIncident']],'nextContinuationToken':None} and
        refusal['request']=={'requestId':'retry-actual-retired-service','incidentId':incident['incidentId'],'invocationId':origin['invocationId'],'failedAttemptId':origin['attemptId']} and
        refusal['httpStatus']==409 and not refusal['command']['pending'] and refusal['command']['result']=={'status':'Rejected','code':'execution-terminal','revision':fm['revision'],'lifecycle':'TerminalSettled'},
        'The held cancellation lost its exact retirement command or durable terminal retry refusal.')
    wire=read('before-wire.json')
    require(all(read(name+'-wire.json')==wire for name in ('held','finished','retained')) and not wire['overflow'] and
        Counter((x['id'],x['name']) for post in wire['outputPosts'] for x in post['records'])==Counter({(1,'Ada'):1,(2,'Grace'):1}) and all(post['hasIdempotencyKey'] for post in wire['outputPosts']),
        'The cancelled held fault generated new physical HTTP effects.')
    retry,=[x['http']['fetch'] for x in before['partitions']['items'] if x['partitionOrdinal']==1]
    require(retry['phase']=='waiting' and retry['lastStatusCode']==429 and retry['attemptCount']==1 and v.instant(retry['notBefore'])>v.instant(p['cutAtUtc']) and
        next(x['http']['fetch'] for x in final['partitions']['items'] if x['partitionOrdinal']==1)==retry, 'The cancelled failed run lost its original durable HTTP retry checkpoint.')
    availability=read('snapshot-unavailability.json');content=q.digest(root/'lookup-source.csv')
    require(q.digest(root/'lookup-snapshot.csv')==content and all(availability[k]==content for k in ('sourceSha256','snapshotSha256Before','snapshotSha256WhileUnavailable','restoredSha256')) and
        availability['restoredBeforeAuthorizedCancellation'] and availability['sourceStillAvailable'] and availability['originalSnapshotReadPathMissing'] and availability['sourceRevision']==source and
        v.instant(availability['restoredAtUtc'])>v.instant(p['heldCutAtUtc']), 'The cancelled held journey changed its immutable lookup bytes or availability cut.')
    original_invocation=None
    for phase in ('before','held','finished'):
        require(set(p[phase+'Stores'])=={'elsa','etl-runtime','etl-design'},'The held cancellation lacks all coupled stores.')
        for store,snapshot in p[phase+'Stores'].items():
            file=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(file.read_bytes())
            require(q.digest(file)==snapshot['gzipSha256'] and hashlib.sha256(raw).hexdigest()==snapshot['databaseSha256'] and snapshot['observer']['readOnly'] and
                snapshot['observer']['snapshotSha256']==snapshot['databaseSha256'], 'A held-cancellation provider snapshot differs from its original bytes.')
            with tempfile.TemporaryDirectory(prefix='elsa-held-cancel-read-') as tmp:
                db=Path(tmp)/'snapshot.sqlite';db.write_bytes(raw)
                with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                    require(c.execute('PRAGMA integrity_check').fetchall()==[('ok',)],'A held-cancellation store failed integrity verification.')
                    if store=='elsa':physical_runtime(c,phase,p,checked[phase],result,command,refusal,embedded)
                    elif store=='etl-runtime':
                        invocation=h.physical_etl(c,phase,p,before,{'original':final},content,availability,include_replacement=False)
                        if original_invocation is None:original_invocation=invocation
                        require(invocation==original_invocation,'Cancellation changed the immutable original ETL invocation.')
    if embedded:verify_parent(root,p,checked)
    return {'parent':parent,'wholeStoresVerified':9,'newWorkers':0,'newEtlRuns':0,'newPhysicalHttpEffects':0}


def physical_runtime(c,phase,p,checked,result,command,refusal,embedded):
    model=checked['model'];origin=result['origin'];key=v.key
    row=c.execute('SELECT Revision,Lifecycle,ContentJson FROM elsa_runtime_model_execution WHERE ExecutionId=?',(key(model['executionId']),)).fetchone()
    require(row is not None and row[:2]==(model['revision'],model['lifecycle']) and json.loads(row[2])['payload']==model['payload'] and
        json.loads(row[2])['terminalResult']==model['terminalResult'] and json.loads(row[2])['executable']==v.encoded_tree(model['executable']) and
        c.execute('SELECT COUNT(*) FROM elsa_runtime_model_execution').fetchone()[0]==1 and
        c.execute('SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state').fetchone()[0]==1+int(embedded), 'The physical held cancellation acquired another worker or changed its committed model state.')
    row,=c.execute('SELECT SchemaVersion,Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(key(origin['workerExecutionId']),)).fetchall();worker=json.loads(row[2])
    require(row[0]=='2.0.0' and row[1]==(1 if phase=='before' else 4) and worker['modelWorkOrigin']==v.encoded_tree(origin) and worker['pinnedExecutable']==v.encoded_tree(origin['workerExecutable']),
        'The physical original worker lost its protected model origin or retained fault.')
    if phase!='before':
        row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=?',(key(origin['workerExecutionId']),)).fetchall()
        require(row[0]=='Faulted' and json.loads(row[1])['fault']['code']==key('etl.snapshot-read-failed') and json.loads(row[1])['completion'] is None,
            'Held cancellation rewrote the physical native root fault.')
    for name in ('startSource','resultSource'):
        source=checked[name]
        if source is None:continue
        row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(key(source['outboxItemId']),)).fetchall()
        require(row[0]==source['status'] and json.loads(row[1])['intent']['payload']==source['intent']['payload'] and json.loads(row[1])['deliveredAt']==source['deliveredAt'] and
            json.loads(row[1])['intent']['kind']==key(source['intent']['kind']),
            'The physical cancelled worker source differs from its exact acknowledgment proof.')
    rows=[json.loads(row[0]) for row in c.execute('SELECT ContentJson FROM elsa_runtime_model_input')]
    require(len(rows)==(1 if phase=='before' else 2 if phase=='held' else 4+int(embedded)), 'The physical held cancellation lost a receipt or accepted extra model input.')
    if phase!='before':
        receipt=checked['resultInput'];stored,=[r for r in rows if r['inputId']==key(origin['resultInputId'])]
        require(stored['result']==receipt['result'] and stored['contentFingerprint']==key(receipt['contentFingerprint']) and stored['acceptedRevision']==2 and
            stored['attemptId']==key(origin['attemptId']), 'Cancellation changed the original accepted native-result receipt.')
    if phase=='finished':
        cancel,=[r for r in rows if r['inputKind']==key('cancel')];rejected,=[r for r in rows if r['inputKind']==key('retry-service')]
        require(cancel['inputId']==key(command['request']['requestId']) and cancel['result']==command['accepted']['result'] and cancel['acceptedRevision']==3 and
            rejected['inputId']==key(refusal['request']['requestId']) and rejected['result']==refusal['command']['result'] and rejected['acceptedRevision']==model['revision'] and
            rejected['invocationId']==key(origin['invocationId']) and rejected['attemptId']==key(origin['attemptId']) and not c.execute('SELECT 1 FROM elsa_runtime_scheduler_work_item').fetchall(),
            'The physical cancellation or terminal retry refusal lost its exact durable decision.')
        kinds=[json.loads(r[0])['intent']['kind'] for r in c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox')]
        require(key('Elsa.Bpmn.CancelNativeWork.v1') not in kinds and key('Elsa.Etl.Cancel.v1') not in kinds,
            'The held cancellation invented cancellation responsibility for its already-faulted worker.')
    material,=[json.loads(r[0])['material'] for r in c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable') if json.loads(r[0]).get('material')]
    require(material['definition']['formatVersion']==3 and material['definition']['serviceBindings']==[{'bindingId':'pipeline','executable':origin['workerExecutable'],'businessErrorCode':'etl.rows-rejected',
        'faultPolicy':{'formatVersion':1,'disposition':'HoldForRetry','maxAttempts':2}}] and material['dependencies']==[{'bindingId':key('pipeline'),'executable':v.encoded_tree(origin['workerExecutable'])}],
        'Cancellation changed the pinned service policy or worker dependency.')
    incidents=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_incident_state')]
    require(len(incidents)==(0 if phase=='before' else 1) and (not incidents or incidents[0]['workflowExecutionId']==key(origin['workerExecutionId']) and
        incidents[0]['status']=='Blocking' and incidents[0]['failureType']==key('ActivityReturnedFault') and incidents[0]['resolutionOutcome']['actionKind']==key('FaultWorkflow')),
        'The original physical native fault incident was erased or reassigned by held cancellation.')


def verify_parent(root,p,checked):
    read=lambda name:json.loads((root/name).read_text());phases={x:read(x+'-checked-embedding.json') for x in ('before','held','finished','retained')}
    before=phases['before'];after=phases['finished'];encoded=before['model']['payload']['nativeParent']['invocation'];origin=h.opaque(encoded)
    pin={k:p['nativeParentPublication'][k] for k in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')}
    require(after==phases['retained'] and origin['nativeExecutionId']==p['nativeParentExecutionId'] and origin['nativeExecutable']==pin and origin['executionId']==p['modelExecutionId'] and
        origin['model']['executable']==p['modelPublication']['executable'] and origin['model']['materialIncarnationId']==p['modelPublication']['incarnationId'],
        'The cancelled native parent lost its original pinned model invocation.')
    for phase in ('before','held','finished'):
        item=phases[phase];run,=[a for a in item['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
        require(item['model']==checked[phase]['model'] and item['model']['payload']['nativeParent']['invocation']==encoded and item['nativeParent']['pinnedExecutable']==pin and
            item['nativeParent']['authority']==item['model']['authority'] and item['nativeParent']['status']==(5 if phase=='finished' else 1) and
            run['invocationId']==origin['nativeInvocationId'] and run['status']==(6 if phase=='finished' else 3) and
            not [a for a in item['activities'] if a['execution']['executableNodeId']=='after'], 'The native parent advanced or lost its actual RunBpmn cancellation/wait.')
        if phase!='finished':
            bookmark,=item['bookmarks']
            require(bookmark['bookmarkId']==origin['registrationId'] and bookmark['stimulusHash']==p['modelExecutionId'] and
                run['privateState']['value']['inlineValue']['Invocation']==encoded and run['privateState']['producedByAttemptId']==origin['nativeAttemptId'],
                'The held native parent lost its original registration or private invocation state.')
        else:
            require(not item['bookmarks'] and not item['queuedModelInputs'] and run['completion'] is None and any(r['status']==1 and r['registrationId']==origin['registrationId'] and
                r['providerId']=='elsa.bpmn.native-parent.v1' and r['payloadType']['alias']=='Elsa.Activities.Bpmn.Models.RunBpmnCompletion' for r in run['triggerDeliveries']),
                'The cancelled parent lacks its exact typed RunBpmn consumption proof.')
    settlement,=[r for r in after['inputs'] if r['inputKind']=='native-parent-settled'];cancel,=[r for r in after['inputs'] if r['inputKind']=='cancel']
    sources={s['intent']['kind']:s for s in after['sources']}
    require(set(sources)=={'Elsa.Bpmn.StartEmbedded.v1','Elsa.Bpmn.StartNativeWork.v1','Elsa.Bpmn.CancelEmbedded.v1','Elsa.Bpmn.ResumeNativeParent.v1'} and len(after['sources'])==4 and
        all(s['status']==2 for s in sources.values()) and sources['Elsa.Bpmn.StartEmbedded.v1']['intent']['payload']==encoded and sources['Elsa.Bpmn.CancelEmbedded.v1']['intent']['payload']==encoded and
        sources['Elsa.Bpmn.ResumeNativeParent.v1']['intent']['payload']=={'FormatVersion':1,'Invocation':encoded,'Result':after['model']['terminalResult']} and
        settlement['acceptedRevision']==4 and settlement['invocationId']==origin['nativeInvocationId'] and settlement['attemptId']==origin['nativeAttemptId'] and settlement['result']['status']=='Accepted' and
        v.instant(sources['Elsa.Bpmn.ResumeNativeParent.v1']['deliveredAt'])>=v.instant(settlement['acceptedAt']) and
        v.instant(sources['Elsa.Bpmn.CancelEmbedded.v1']['deliveredAt'])>=v.instant(cancel['acceptedAt']) and len(after['inputs'])==5,
        'The cancelled native parent lost exact settlement/ACK or invented another cancel input.')
    for phase in ('before','held','finished'):
        with tempfile.TemporaryDirectory(prefix='elsa-held-cancel-parent-') as tmp:
            db=Path(tmp)/'snapshot.sqlite';db.write_bytes(gzip.decompress((root/p[phase+'Stores']['elsa']['file']).read_bytes()))
            with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                item=phases[phase];row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(v.key(p['nativeParentExecutionId']),)).fetchall()
                require(row[0]==item['nativeParent']['status'] and json.loads(row[1])['pinnedExecutable']==v.encoded_tree(pin), 'The physical cancelled parent header lost its original pin or status.')
                for activity in item['activities']:
                    row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state WHERE ActivityExecutionId=?',(v.key(activity['invocationId']),)).fetchall()
                    require(row[0]==('Scheduled','Running','Waiting','Suspended','Completed','Faulted','Cancelled','Recovered','Superseded')[activity['status']],
                        'The physical native activity differs from its observed cancellation disposition.')
                material,=c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable WHERE ArtifactId=?',(v.key(pin['artifactId']),)).fetchall();material=json.loads(material[0])
                roots={'Root':'Elsa.Activities.Bpmn.Activities.RunBpmn','Sequence':'Elsa.Activities.Sequence.Activities.Sequence','Flowchart':'Elsa.Activities.Flowchart.Activities.Flowchart','CompensationScope':'Elsa.Activities.Compensation.Activities.CompensationScope'}
                require(material['rootActivity']['activityType']==v.key(roots[p['embeddedParent']]) and material['identity']==v.encoded_tree(pin) and
                    any(d['artifactId']==v.key(p['modelPublication']['executable']['artifactId']) and d['artifactHash']==v.key(p['modelPublication']['executable']['artifactHash']) for d in material['dependencies']),
                    'The actual cancelled parent root shape or model dependency closure differs.')
                for source in item['sources']:
                    row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(v.key(source['outboxItemId']),)).fetchall()
                    require(row[0]==source['status'] and json.loads(row[1])['intent']['payload']==source['intent']['payload'] and json.loads(row[1])['deliveredAt']==source['deliveredAt'],
                        'The physical cancelled-parent source differs from its exact terminal acknowledgment.')


def verify(path):
    root=path.resolve().parent;m=json.loads(path.read_text());source=m['sourceRevision'];cases=m['cases']
    require(m['schemaVersion']==1 and m['kind']=='actual-bpmn-held-service-cancellation-matrix' and re.fullmatch('[a-f0-9]{40}',source) and len(cases)==5 and
        {x['name'] for x in cases}=={'direct','root','sequence','flowchart','compensationscope'}, 'The held-cancellation matrix lacks every unique direct/native parent.')
    names={'process.json','before.json','recovered.json','held.json','held-after-restart.json','finished.json','retained.json','held-cancel-command.json','retired-retry-refusal.json','retained-incidents.json',
        'snapshot-unavailability.json','lookup-source.csv','lookup-snapshot.csv'}|{x+'-wire.json' for x in ('before','held','finished','retained')}|{x+'-checked-model.json' for x in ('before','held','finished','retained')}|{
        x+'-'+store+'.sqlite.gz' for x in ('before','held','finished') for store in ('elsa','etl-runtime','etl-design')}
    products=None;traces={};results=[]
    for case in cases:
        name=case['name'];expected=names|({x+'-checked-embedding.json' for x in ('before','held','finished','retained')} if name!='direct' else set())
        require(len(case['assets'])==len(expected) and {a['path'] for a in case['assets']}=={name+'/'+n for n in expected}, 'The held cancellation lacks its exact raw capture assets.')
        for asset in case['assets']:require(q.digest(q.evidence_file(root,asset['path']))==asset['sha256'],'An actual held-cancellation asset differs.')
        results.append(verify_case(root/name,name,source));p=json.loads((root/name/'process.json').read_text())
        if products is None:products=p['products']
        require(products==p['products'],'The held-cancellation cases used different first-party host products.')
        for phase in ('before','held','finished'):
            def normalize(value):
                if isinstance(value,dict):return {k:'<model-execution>' if k=='producingWorkHandle' and x==p['modelExecutionId'] else normalize(x) for k,x in value.items()}
                if isinstance(value,list):return [normalize(x) for x in value]
                return value
            state=normalize(json.loads((root/name/(phase+'-checked-model.json')).read_text())['model']['payload']['state'])
            require(phase not in traces or traces[phase]==state,'The direct/native-parent held-cancellation semantic histories differ.')
            traces[phase]=state
    return {'actualHeldCancellationJourneys':results,'wholeStoresVerified':45,'sourceRevision':source,'heldCancellationProved':True,'retiredFailureAndOriginalFaultPreserved':True,
        'retiredRetryRefusalProved':True,'sameSourceInterpreterTracesCompared':True,'budgetExhaustionProved':False,'timerInterruptionProved':False,'obsoleteResultControlsProved':False,
        'authorizationBeforeReplayProved':False,'finalAffectedSourceUpgradeAndStudioProved':False}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--manifest',type=Path,required=True);print(json.dumps(verify(parser.parse_args().manifest),indent=2))
