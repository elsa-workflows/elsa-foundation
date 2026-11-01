#!/usr/bin/env python3
"""Read-only checks for actual competing held retry/cancel submissions and recovery."""
import argparse,gzip,hashlib,importlib.util,json,re,sqlite3,tempfile
from contextlib import closing
from pathlib import Path
_spec=importlib.util.spec_from_file_location('held_race_retry',Path(__file__).with_name('verify_held_service_retry.py'))
h=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(h)
v=h.v;q=h.q;require=h.require
PARENTS=('direct','root','sequence','flowchart','compensationscope')
ORDERS=('retry-first','cancel-first')
RETRY='retry-competing-held-service';CANCEL='cancel-competing-held-service'

def verify_case(root,parent,order,source):
    read=lambda n:json.loads((root/n).read_text());p=read('process.json');embedded=parent!='direct';retry_first=order=='retry-first'
    require(p['kind']=='actual-held-retry-cancel-submission-race' and p['schemaVersion']==1 and p['sourceRevision']==source and p['order']==order and
        p['parent']=='BpmnModel' and p['embeddedParent']=={'root':'Root','sequence':'Sequence','flowchart':'Flowchart','compensationscope':'CompensationScope'}.get(parent) and
        p['technicalFaultPolicy']=='HoldForRetry' and p['maxServiceAttempts']==2 and p['retryAfterSeconds']==20 and p['resumptionSweepIntervalSeconds']==.2 and
        p['predecessorExitCode']==p['heldPredecessorExitCode']==-9 and
        [x['role'] for x in p['processes']]==['http-fixture','workbench','workbench','gated-held-sdk','competing-held-sdk','workbench','workbench'] and
        len({x['processId'] for x in p['processes']})==7 and len({x['controllerProcessId'] for x in p['processes']})==1 and all(x['started'] for x in p['processes']),
        'The concurrent held journey lost its source, order, real process-loss cuts or child identities.')
    products=p['products'];require(products['sourceCommit']==source and products['metadataOnly'] and len(products['actualProductVersions'])==12 and
        all(x['productVersion'].endswith('+'+source) for x in products['actualProductVersions']),'The held race products differ from their executing source.')
    before=read('before.json');held=read('held.json');final=read('finished.json');checked={phase:read(phase+'-checked-model.json') for phase in ('before','held','finished','retained')}
    bm,hm,fm=(checked[x]['model'] for x in ('before','held','finished'));full=read('finished-checked-inputs.json');raced=read('raced-checked-model.json')
    require(final==read('retained.json') and checked['finished']==checked['retained'] and full==read('retained-checked-inputs.json') and full['model']==fm and
        raced['model']==hm==read('race-competing-checked.json')['model'],'A concurrent decision or original failed attempt changed across process recovery.')
    first=checked['before']['worker']['modelWorkOrigin'];failure,=hm['payload']['failures'];old,=hm['payload']['nativeResults'];resolved,=fm['payload']['failures']
    require(bm['revision']==1 and bm['lifecycle']==0 and bm['payload']['failures']==[] and hm['revision']==2 and hm['lifecycle']==1 and hm['terminalResult'] is None and
        bm['payload']['work']==hm['payload']['work'] and len(hm['payload']['work'])==1 and hm['payload']['work'][0]['attemptNumber']==1 and
        bm['payload']['state']==hm['payload']['state']==failure['semanticState']==fm['payload']['state'] and failure['status']=='Open' and failure['attemptNumber']==1 and failure['maxAttempts']==2 and
        old['origin']==first and old['status']==4 and old['faultCode']=='etl.snapshot-read-failed' and old['completion'] is None and
        fm['payload']['nativeResults']==[old] and not fm['payload']['work'] and fm['revision']==(5 if retry_first else 3)+int(embedded) and fm['lifecycle']==3 and
        fm['terminalResult']=={'status':'Cancelled','reason':'Cancel the actual competing held service.'} and
        bm['payload']['formatVersion']==hm['payload']['formatVersion']==(8 if embedded else 7) and fm['payload']['formatVersion']==((10 if embedded else 9) if retry_first else (8 if embedded else 7)),
        'The actual held race lost its original semantic state, bounded policy, physical fault or terminal cancellation.')
    for phase,item in checked.items():
        model=item['model'];worker=item['worker'];start=item['startSource']
        require(item['readOnly'] and model['executionId']==p['modelExecutionId'] and model['executable']==p['modelPublication']['executable'] and
            model['materialIncarnationId']==p['modelPublication']['incarnationId'] and worker['modelWorkOrigin']==first and worker['pinnedExecutable']==first['workerExecutable'] and
            first['workerExecutable']=={k:p['publication'][k] for k in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')} and
            first['executable']==model['executable'] and first['materialIncarnationId']==model['materialIncarnationId'] and first['scope']==model['scope'] and
            first['workerExecutionId']==p['workflowExecutionId']==worker['workflowExecutionId'] and worker['authority']==model['authority'] and worker['parentWorkflowExecutionId'] is None and
            start['status']==2 and start['intent']['payload']==first and start['outboxItemId']==first['sourceOutboxItemId'],
            'The concurrent held race lost its original exact source, pin, authority or physical origin.')
        if phase!='before':require(worker==checked['held']['worker'] and worker['status']==4,'A concurrent command rewrote the original failed native worker.');h.source_and_receipt(item,old,2)
    proposal=read('race-proposal.json');release=read('race-release.json');a=read('race-gated.json');b=read('race-competitor.json');lease=proposal['ownership'];input=proposal['proposal']['modelState']['input']
    ids=[RETRY,CANCEL] if retry_first else [CANCEL,RETRY];operations=['--retry-held-model','--cancel-held-model'] if retry_first else ['--cancel-held-model','--retry-held-model']
    require(proposal['operation']==operations[0] and proposal['observedModel']==hm and proposal['proposal']['modelState']['expectedRevision']==2 and
        proposal['proposal']['modelState']['state']['revision']==3 and input['inputId']==ids[0] and input['result']['status']=='Accepted' and
        proposal['proposal']['modelState']['state']['payload']['state']==failure['semanticState'] and lease['workflowExecutionId']==p['modelExecutionId'] and
        lease['ownerId'].endswith(':'+str(proposal['processId'])) and proposal['processId']==p['processes'][3]['processId'] and
        release['processId']==proposal['processId'] and release['proposalUnchanged'] and release['proposalFingerprint']==proposal['proposalFingerprint'] and
        re.fullmatch('[a-f0-9]{64}',release['proposalFingerprint']) and v.instant(lease['acquiredAt'])<=v.instant(proposal['observedAtUtc'])<v.instant(b['startedAtUtc'])<
        v.instant(b['completedAtUtc'])<=v.instant(release['releasedAtUtc'])<v.instant(a['completedAtUtc'])<v.instant(lease['expiresAt']),
        'The held race did not overlap an unchanged actual proposal with a separate writer before its original lease expiry.')
    inventory={x['name'].removesuffix('.dll'):x for x in products['actualProductVersions']}
    for item,operation,id in ((a,operations[0],ids[0]),(b,operations[1],ids[1])):
        require(item['operation']==operation and item['before']==item['model']==hm and item['outcome']=={'executionId':p['modelExecutionId'],'inputId':id,'replay':False,'pending':True,'result':None} and
            len(item['products'])==5 and all(x['informationalVersion'].endswith('+'+source) for x in item['products']) and
            all(x['sha256']==inventory[x['name']]['sha256'] for x in item['products'] if x['name'] in inventory),
            'A concurrent actual SDK command changed revision two or lost durable pending admission/source identity.')
    queue=raced['queuedModelInputs'];require([x['commandId'] for x in queue]==ids and all(x['commandKind']==21 and x['workflowExecutionId']==p['modelExecutionId'] for x in queue),
        'The protected FIFO lost or reordered the actual two competing inputs.')
    retry_cmd=next(x['payload'] for x in queue if x['commandId']==RETRY);cancel_cmd=next(x['payload'] for x in queue if x['commandId']==CANCEL)
    require(retry_cmd['inputKind']=='retry-service' and retry_cmd['payload']=={'incidentId':failure['incidentId'],'invocationId':first['invocationId'],'failedAttemptId':first['attemptId']} and
        cancel_cmd['inputKind']=='cancel' and cancel_cmd['payload']=={'reason':'Cancel the actual competing held service.'} and
        all(x['executable']==hm['executable'] and x['materialIncarnationId']==hm['materialIncarnationId'] and x['authority']==hm['authority'] and x['tenantId']==hm['tenantId'] for x in (retry_cmd,cancel_cmd)),
        'The concurrent retained commands lost their exact failed attempt, content, pin or authority.')
    require(final['retry']['result']=={'status':'Accepted' if retry_first else 'Rejected','code':None if retry_first else 'execution-terminal','revision':3,
        'lifecycle':'Running' if retry_first else 'TerminalPendingSettlement' if embedded else 'TerminalSettled'} and
        final['cancel']['result']=={'status':'Accepted','code':None,'revision':4 if retry_first else 3,'lifecycle':'TerminalPendingSettlement' if retry_first or embedded else 'TerminalSettled'} and
        not final['retry']['pending'] and not final['cancel']['pending'], 'The competing FIFO decisions lost their exact accepted/rejected results.')
    receipts={i['inputKind']:i for i in full['inputs']};require(receipts['cancel']['inputId']==CANCEL and receipts['retry-service']['inputId']==RETRY and
        receipts['cancel']['result']==final['cancel']['result'] and receipts['retry-service']['result']==final['retry']['result'] and
        v.instant(receipts['retry-service']['acceptedAt'])>v.instant(release['releasedAtUtc']) and v.instant(receipts['cancel']['acceptedAt'])>v.instant(release['releasedAtUtc']) and
        (v.instant(receipts['retry-service']['acceptedAt'])<v.instant(receipts['cancel']['acceptedAt']) if retry_first else v.instant(receipts['cancel']['acceptedAt'])<v.instant(receipts['retry-service']['acceptedAt'])) and
        not full['queuedModelInputs'] and all(s['status']==2 for s in full['sources']), 'Cold recovery lost original command order or left acknowledgment/input responsibility.')
    retirement=read('finished-checked-retirement.json');second=None
    if retry_first:
        cancel,=fm['payload']['cancellations'];second=cancel['origin'];retired=receipts['native-start-retired']
        require(cancel['settled'] and cancel['notAdmitted'] and second['invocationId']==first['invocationId'] and second['attemptId']!=first['attemptId'] and
            second['workerExecutionId']!=first['workerExecutionId'] and second['workerExecutable']==first['workerExecutable'] and
            proposal['proposal']['modelState']['state']['payload']['work'][0]['nativeWork']==second and
            resolved=={**failure,'status':'Retried','retryAttemptId':second['attemptId'],'resolutionInputId':RETRY} and
            retired['acceptedRevision']==5 and retired['invocationId']==second['invocationId'] and retired['attemptId']==second['attemptId'] and
            retirement['origin']==second and retirement['worker'] is None and not retirement['queuedNativeWork'] and not retirement['nativeMarkers'],
            'The retry-first cancellation lost its bounded replacement or positive exact non-admission retirement.')
        starts=[s for s in full['sources'] if s['intent']['kind']=='Elsa.Bpmn.StartNativeWork.v1'];cancels=[s for s in full['sources'] if s['intent']['kind']=='Elsa.Bpmn.CancelNativeWork.v1']
        require(len(starts)==2 and len(cancels)==1 and any(s['intent']['payload']==second and s['outboxItemId']==second['sourceOutboxItemId'] and
            v.instant(s['deliveredAt'])>=v.instant(retired['acceptedAt']) for s in starts) and cancels[0]['intent']['payload']=={'origin':second,'sourceOutboxItemId':cancel['sourceOutboxItemId'],'settled':False} and
            cancels[0]['outboxItemId']==cancel['sourceOutboxItemId'] and v.instant(cancels[0]['deliveredAt'])>=v.instant(retired['acceptedAt']),
            'The retry-first start/cancel was acknowledged without its exact non-admission retirement receipt.')
    else:
        require(resolved=={**failure,'status':'Retired','resolutionInputId':CANCEL} and not fm['payload'].get('cancellations') and
            len([s for s in full['sources'] if s['intent']['kind']=='Elsa.Bpmn.StartNativeWork.v1'])==1 and
            not any(s['intent']['kind']=='Elsa.Bpmn.CancelNativeWork.v1' for s in full['sources']),
            'The cancel-first failure reopened or invented a new physical attempt.')
    require(final['execution']['native']==held['execution']['native'] and final['execution']['run']==held['execution']['run'] and final['execution']['run']['status']=='faulted' and
        final['execution']['run']['terminalResult']['diagnostic']['code']=='etl.snapshot-read-failed' and
        all(read(x+'-wire.json')==read('before-wire.json') for x in ('held','finished','retained')) and
        len([r for post in read('before-wire.json')['outputPosts'] for r in post['records']])==2,
        'The concurrent held commands changed actual failed ETL/native truth or produced new HTTP effects.')
    availability=read('snapshot-unavailability.json');content=q.digest(root/'lookup-source.csv');require(q.digest(root/'lookup-snapshot.csv')==content and
        all(availability[k]==content for k in ('sourceSha256','snapshotSha256Before','snapshotSha256WhileUnavailable','restoredSha256')) and
        availability['restoredBeforeAuthorizedRetry'] and v.instant(availability['restoredAtUtc'])>v.instant(p['heldCutAtUtc']),
        'The actual concurrent commands recovered a changed immutable lookup.')
    original=None
    for phase in ('before','held','raced','finished'):
        require(set(p[phase+'Stores'])=={'elsa','etl-runtime','etl-design'},'The held race lacks a coupled whole store.')
        for store,snapshot in p[phase+'Stores'].items():
            file=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(file.read_bytes());require(q.digest(file)==snapshot['gzipSha256'] and hashlib.sha256(raw).hexdigest()==snapshot['databaseSha256'] and
                snapshot['observer']['readOnly'] and snapshot['observer']['snapshotSha256']==snapshot['databaseSha256'],'A held race store differs from its actual provider backup.')
            with tempfile.TemporaryDirectory(prefix='elsa-held-race-read-') as temp:
                db=Path(temp)/'db';db.write_bytes(raw)
                with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                    require(c.execute('PRAGMA integrity_check').fetchall()==[('ok',)],'A held race whole store failed integrity verification.')
                    if store=='elsa':physical(c,phase,p,checked['held'] if phase=='raced' else checked[phase],raced,full,proposal,second)
                    elif store=='etl-runtime':
                        invocation=h.physical_etl(c,phase,p,before,{'original':final['execution']},content,availability,include_replacement=False)
                        if original is None:original=invocation
                        require(original==invocation,'The concurrent commands changed the immutable original ETL invocation.')
    if embedded:verify_parent(root,p,checked,full)
    return {'parent':parent,'order':order,'wholeStoresVerified':12,'newAdmittedWorkers':0,'newEtlRuns':0}

def physical(c,phase,p,checked,raced,full,proposal,second):
    key=v.key;model=checked['model'];origin=checked['worker']['modelWorkOrigin'];row,=c.execute('SELECT Revision,Lifecycle,ContentJson FROM elsa_runtime_model_execution').fetchall();body=json.loads(row[2])
    require(row[:2]==(model['revision'],model['lifecycle']) and body['payload']==model['payload'] and body['executable']==v.encoded_tree(model['executable']) and
        body['materialIncarnationId']==key(model['materialIncarnationId']) and body['terminalResult']==model['terminalResult'], 'The physical concurrent model differs from its checked state and immutable pin.')
    require(c.execute('SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state').fetchone()[0]==1+int(p['embeddedParent'] is not None), 'A concurrent retry admitted an extra physical native worker.')
    row,=c.execute('SELECT SchemaVersion,Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(key(origin['workerExecutionId']),)).fetchall();native=json.loads(row[2])
    require(row[:2]==('2.0.0',1 if phase=='before' else 4) and native['modelWorkOrigin']==v.encoded_tree(origin) and native['pinnedExecutable']==v.encoded_tree(origin['workerExecutable']),
        'The physical original worker lost its fault, origin or pin.')
    if phase!='before':
        row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=?',(key(origin['workerExecutionId']),)).fetchall()
        require(row[0]=='Faulted' and json.loads(row[1])['fault']['code']==key('etl.snapshot-read-failed') and json.loads(row[1])['completion'] is None,
            'The physical original native activity fault was relabelled.')
    material,=[json.loads(r[0])['material'] for r in c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable') if json.loads(r[0]).get('material')]
    require(material['definition']['formatVersion']==3 and material['definition']['serviceBindings']==[{'bindingId':'pipeline','executable':origin['workerExecutable'],'businessErrorCode':'etl.rows-rejected',
        'faultPolicy':{'formatVersion':1,'disposition':'HoldForRetry','maxAttempts':2}}] and material['dependencies']==[{'bindingId':key('pipeline'),'executable':v.encoded_tree(origin['workerExecutable'])}],
        'The physical held race changed its pinned attempt policy or worker dependency.')
    rows=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_model_input')];expected=full['inputs'] if phase=='finished' else [r for r in full['inputs'] if r['acceptedRevision']<=(1 if phase=='before' else 2)]
    require(len(rows)==len(expected),'The physical held race lost or invented an input receipt.')
    for receipt in expected:
        row,=[r for r in rows if r['inputId']==key(receipt['inputId'])];require(row['acceptedRevision']==receipt['acceptedRevision'] and row['result']==receipt['result'] and
            row['contentFingerprint']==key(receipt['contentFingerprint']) and row['acceptedAt']==receipt['acceptedAt'] and row['invocationId']==(key(receipt['invocationId']) if receipt['invocationId'] else None) and
            row['attemptId']==(key(receipt['attemptId']) if receipt['attemptId'] else None), 'The physical held command or original result receipt differs from its exact checked decision.')
    if phase=='raced':
        queue=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_scheduler_work_item ORDER BY WorkOrderKey')]
        require(len(queue)==2 and [(r['commandId'],r['payload']) for r in queue]==[(key(r['commandId']),r['payload']) for r in raced['queuedModelInputs']],
            'The physical FIFO differs from the two actual concurrently retained commands.')
        ownership,=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_execution_liveness_state WHERE WorkflowExecutionId=?',(key(model['executionId']),))]
        require(ownership['executionLease'] is None and int(v.decoded('u16:'+ownership['metadata'][key('runtime.ownership.fencingToken')]))>proposal['ownership']['fencingToken'],
            'The actual competitor did not supersede and release the original proposal ownership fence.')
    elif phase=='finished':require(not c.execute('SELECT 1 FROM elsa_runtime_scheduler_work_item').fetchall(),'The settled race still has physical queued work.')
    sources=[checked['startSource'],checked['resultSource']]+(full['sources'] if phase=='finished' else [])
    for source in sources:
        if source is None:continue
        row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(key(source['outboxItemId']),)).fetchall();body=json.loads(row[1])
        require(row[0]==source['status'] and body['intent']['payload']==source['intent']['payload'] and body['deliveredAt']==source['deliveredAt'], 'A physical held race source lost its original payload or exact ACK.')
    if second is not None and phase=='finished':require(not c.execute('SELECT 1 FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(key(second['workerExecutionId']),)).fetchall(),
        'The positively retired retry unexpectedly admitted a physical native worker.')

def verify_parent(root,p,checked,full):
    read=lambda n:json.loads((root/n).read_text());before=read('before-checked-embedding.json');held=read('held-checked-embedding.json');after=read('finished-checked-embedding.json')
    encoded=before['model']['payload']['nativeParent']['invocation'];origin=h.opaque(encoded);pin={k:p['nativeParentPublication'][k] for k in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')}
    require(after==read('retained-checked-embedding.json') and before['model']==checked['before']['model'] and held['model']==checked['held']['model'] and after['model']==full['model'] and
        origin['nativeExecutionId']==p['nativeParentExecutionId'] and origin['nativeExecutable']==pin and after['nativeParent']['pinnedExecutable']==pin and
        after['nativeParent']['status']==5 and not after['bookmarks'] and not after['queuedModelInputs'] and
        not any(a['execution']['executableNodeId']=='after' for a in after['activities']),'The actual native parent advanced or lost its original cancellation/settlement.')
    run,=[a for a in after['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
    original,=[a for a in held['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
    require(run['invocationId']==original['invocationId']==origin['nativeInvocationId'] and original['status']==3 and run['status']==6 and run['completion'] is None and
        any(x['status']==1 and x['registrationId']==origin['registrationId'] and x['providerId']=='elsa.bpmn.native-parent.v1' and
            x['payloadType']['alias']=='Elsa.Activities.Bpmn.Models.RunBpmnCompletion' for x in run['triggerDeliveries']), 'The cancelled native parent lacks original typed RunBpmn consumption.')
    settlement,=[x for x in full['inputs'] if x['inputKind']=='native-parent-settled'];returned,=[s for s in full['sources'] if s['intent']['kind']=='Elsa.Bpmn.ResumeNativeParent.v1']
    require(settlement['acceptedRevision']==full['model']['revision'] and settlement['invocationId']==origin['nativeInvocationId'] and settlement['attemptId']==origin['nativeAttemptId'] and
        returned['intent']['payload']=={'FormatVersion':1,'Invocation':encoded,'Result':full['model']['terminalResult']} and returned['status']==2 and
        v.instant(returned['deliveredAt'])>=v.instant(settlement['acceptedAt']), 'The held race parent return was acknowledged without exact model settlement.')
    for phase,item in (('before',before),('held',held),('finished',after)):
        with tempfile.TemporaryDirectory(prefix='elsa-held-race-parent-') as temp:
            db=Path(temp)/'db';db.write_bytes(gzip.decompress((root/p[phase+'Stores']['elsa']['file']).read_bytes()))
            with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(v.key(p['nativeParentExecutionId']),)).fetchall()
                require(row[0]==item['nativeParent']['status'] and json.loads(row[1])['pinnedExecutable']==v.encoded_tree(pin),'The physical native parent lost its original pin or actual status.')

def verify(path):
    root=path.resolve().parent;m=json.loads(path.read_text());source=m['sourceRevision'];cases=m['cases']
    require(m['schemaVersion']==1 and m['kind']=='actual-held-retry-cancel-submission-matrix' and re.fullmatch('[a-f0-9]{40}',source) and len(cases)==10 and
        {(c['parent'],c['order']) for c in cases}=={(p,o) for p in PARENTS for o in ORDERS}, 'The held retry/cancel matrix lacks both unique orders for every parent.')
    names={'process.json','before.json','recovered.json','held.json','finished.json','retained.json','snapshot-unavailability.json','lookup-source.csv','lookup-snapshot.csv',
        'race-proposal.json','race-competitor.json','race-gated.json','race-release.json','race-competing-checked.json','raced-checked-model.json','finished-checked-inputs.json','retained-checked-inputs.json','finished-checked-retirement.json'}|{
        phase+'-wire.json' for phase in ('before','held','finished','retained')}|{phase+'-checked-model.json' for phase in ('before','held','finished','retained')}|{
        phase+'-'+store+'.sqlite.gz' for phase in ('before','held','raced','finished') for store in ('elsa','etl-runtime','etl-design')}
    results=[];products=None
    for case in cases:
        name=case['order']+'-'+case['parent'];require(case['name']==name,'A held-race case name aliases another input order.')
        expected=names|({phase+'-checked-embedding.json' for phase in ('before','held','finished','retained')} if case['parent']!='direct' else set())
        assets=case['assets'];require(len(assets)==len(expected) and {a['path'] for a in assets}=={name+'/'+n for n in expected},'A held race omits an authoritative raw asset.')
        for asset in assets:require(q.digest(q.evidence_file(root,asset['path']))==asset['sha256'],'A held race asset differs from its immutable hash.')
        current=json.loads((root/name/'process.json').read_text())['products'];require(products is None or products==current,'The held race matrix mixed executable products.');products=current
        results.append(verify_case(root/name,case['parent'],case['order'],source))
    return {'sourceRevision':source,'actualConcurrentSubmissionJourneys':results,'wholeStoresVerified':sum(r['wholeStoresVerified'] for r in results),
        'bothFifoDecisionOrdersProved':True,'twoSimultaneousProviderProposalsManufactured':False,'finalUpgradeAndStudioProved':False}

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--manifest',required=True,type=Path);args=parser.parse_args();print(json.dumps(verify(args.manifest),indent=2))
