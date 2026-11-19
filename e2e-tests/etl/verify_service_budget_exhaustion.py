#!/usr/bin/env python3
"""Read-only proof of actual held-service budget exhaustion and terminal retry refusal."""
import argparse
from collections import Counter
from contextlib import closing
import gzip,hashlib,importlib.util,json,re,sqlite3,tempfile
from pathlib import Path
_spec=importlib.util.spec_from_file_location('held_cancel',Path(__file__).with_name('verify_held_service_cancellation.py'))
hc=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(hc)
h=hc.h;v=h.v;q=h.q;require=h.require


def verify_case(root,parent,source):
    read=lambda name:json.loads((root/name).read_text());p=read('process.json');embedded=parent!='direct';products=p['products']
    require(p['kind']=='actual-model-service-budget-exhaustion' and p['schemaVersion']==1 and p['sourceRevision']==source and p['parent']=='BpmnModel' and
        p['embeddedParent']=={'root':'Root','sequence':'Sequence','flowchart':'Flowchart','compensationscope':'CompensationScope'}.get(parent) and
        p['technicalFaultPolicy']=='HoldForRetry' and p['maxServiceAttempts']==2 and p['resumptionSweepIntervalSeconds']==.2 and p['retryAfterSeconds']==20 and
        p['predecessorExitCode']==p['heldPredecessorExitCode']==-9 and [x['role'] for x in p['processes']]==['http-fixture']+['workbench']*4 and len({x['processId'] for x in p['processes']})==5 and
        products['sourceCommit']==source and products['metadataOnly'] and len(products['actualProductVersions'])==12 and all(x['productVersion'].endswith('+'+source) for x in products['actualProductVersions']),
        'The exhausted budget lost its pinned policy, exact source or two real process-loss cuts.')
    before=read('before.json');held=read('held.json');final=read('finished.json');checked={phase:read(phase+'-checked-model.json') for phase in ('before','held','finished','retained')}
    b,old,f=(checked[x] for x in ('before','held','finished'));replacement=read('finished-checked-replacement.json');origin=b['worker']['modelWorkOrigin'];second=replacement['worker']['modelWorkOrigin']
    bm,hm,fm=(x['model'] for x in (b,old,f));failure,=hm['payload']['failures'];first_result,=hm['payload']['nativeResults'];first_final,exhausted=fm['payload']['failures'];results=fm['payload']['nativeResults']
    require(final==read('retained.json') and held==read('held-after-restart.json') and f==checked['retained'] and replacement==read('retained-checked-replacement.json'),
        'Repair or restart changed a final exhausted attempt or its original fault evidence.')
    pin={k:p['publication'][k] for k in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')}
    for item,worker_origin in [(x,origin) for x in checked.values()]+[(replacement,second)]:
        model=item['model'];worker=item['worker'];start=item['startSource']
        require(item['readOnly'] and model['executionId']==p['modelExecutionId'] and model['executable']==p['modelPublication']['executable'] and
            model['materialIncarnationId']==p['modelPublication']['incarnationId'] and worker['modelWorkOrigin']==worker_origin and worker['pinnedExecutable']==pin and
            worker['workflowExecutionId']==worker_origin['workerExecutionId'] and worker['parentWorkflowExecutionId'] is None and worker['authority']==model['authority'] and
            worker_origin['scope']==model['scope'] and worker_origin['executionId']==model['executionId'] and worker_origin['executable']==model['executable'] and
            worker_origin['materialIncarnationId']==model['materialIncarnationId'] and worker_origin['workerExecutable']==pin and
            start['status']==2 and start['outboxItemId']==worker_origin['sourceOutboxItemId'] and start['intent']['kind']=='Elsa.Bpmn.StartNativeWork.v1' and start['intent']['payload']==worker_origin,
            'A budgeted worker lost its immutable full origin, authority, pin or committed admission source.')
    require(bm['revision']==1 and bm['lifecycle']==0 and bm['payload']['failures']==[] and b['worker']['status']==1 and b['resultInput'] is None and b['resultSource'] is None and
        hm['revision']==2 and hm['lifecycle']==1 and hm['terminalResult'] is None and hm['payload']['state']==bm['payload']['state']==failure['semanticState'] and
        hm['payload']['work']==bm['payload']['work'] and len(hm['payload']['work'])==1 and hm['payload']['work'][0]['attemptNumber']==1 and
        failure['status']=='Open' and failure['attemptNumber']==1 and failure['maxAttempts']==2 and failure['failedAttemptId']==origin['attemptId'] and failure['invocationId']==origin['invocationId'] and
        first_result['origin']==origin and first_result['status']==4 and first_result['faultCode']=='etl.snapshot-read-failed' and first_result['completion'] is None and
        old['worker']==f['worker'] and old['worker']['status']==replacement['worker']['status']==4,
        'The first held failure lost its exact active semantic token or immutable native fault.')
    command=read('retry-command.json');request=command['request'];refusal=read('exhausted-retry-refusal.json');second_result=results[1]
    require(origin['workerExecutionId']==p['workflowExecutionId'] and second['workerExecutionId']==p['replacementWorkerExecutionId'] and p['runId']!=p['replacementRunId'] and
        second['invocationId']==origin['invocationId'] and second['attemptId']!=origin['attemptId'] and second['workerExecutionId']!=origin['workerExecutionId'] and
        request=={'requestId':'retry-actual-held-service','incidentId':failure['incidentId'],'invocationId':origin['invocationId'],'failedAttemptId':origin['attemptId']} and
        not command['accepted']['pending'] and command['accepted']['result']=={'status':'Accepted','code':None,'revision':3,'lifecycle':'Running'} and
        command['duplicate']['replay'] and not command['duplicate']['pending'] and command['duplicate']['result']==command['accepted']['result'] and command['conflictingContentHttpStatus']==409 and
        first_final=={**failure,'status':'Retried','retryAttemptId':second['attemptId'],'resolutionInputId':request['requestId']},
        'Budgeted retry lost its single new physical attempt, logical invocation or exact replay/conflict receipt.')
    require(fm['revision']==4+int(embedded) and fm['lifecycle']==3 and fm['terminalResult']['status']=='Faulted' and fm['terminalResult']['code']=='bpmn.work.faulted' and fm['terminalResult']['errorCode'] is None and
        fm['payload']['formatVersion']==hm['payload']['formatVersion']==bm['payload']['formatVersion']==(8 if embedded else 7) and fm['payload']['work']==[] and len(results)==2 and results[0]==first_result and
        second_result['origin']==second and second_result['status']==4 and second_result['completion'] is None and second_result['faultCode']=='etl.binding-read-failed' and
        exhausted['status']=='Final' and exhausted['attemptNumber']==exhausted['maxAttempts']==2 and exhausted['semanticState']==failure['semanticState'] and
        exhausted['invocationId']==origin['invocationId'] and exhausted['failedAttemptId']==second['attemptId'] and exhausted['resultInputId']==second['resultInputId'] and
        exhausted['resolutionInputId']==second['resultInputId'] and exhausted['retryAttemptId'] is None and
        final['original']['run']==held['execution']['run'] and final['original']['native']==held['execution']['native'] and
        final['replacement']['run']['status']=='faulted' and final['replacement']['run']['terminalResult']['diagnostic']['code']=='etl.binding-read-failed' and
        final['replacement']['run']['acceptedRecords']==final['replacement']['run']['rejectedRecords']==0 and final['replacement']['partitions']['items']==[],
        'The second technical fault did not exhaust its immutable budget or preserve both original failed attempts.')
    h.source_and_receipt(old,first_result,2);h.source_and_receipt(f,first_result,2);h.source_and_receipt(replacement,second_result,4)
    final_incident=refusal['incident'];actual_incident,=[i for i in final['incidents']['items'] if i['workerExecutionId']==second['workerExecutionId']]
    require(len(final['incidents']['items'])==2 and actual_incident==final_incident and final_incident['status']=='Final' and final_incident['attemptNumber']==final_incident['maxAttempts']==2 and
        final_incident['incidentId']==exhausted['incidentId'] and final_incident['faultCode']=='etl.binding-read-failed' and
        refusal['request']=={'requestId':'retry-actual-exhausted-service','incidentId':exhausted['incidentId'],'invocationId':origin['invocationId'],'failedAttemptId':second['attemptId']} and
        refusal['httpStatus']==409 and not refusal['command']['pending'] and refusal['command']['result']=={'status':'Rejected','code':'execution-terminal','revision':fm['revision'],'lifecycle':'TerminalSettled'},
        'The exhausted final incident lost its exact terminal retry refusal.')
    wire=read('before-wire.json');require(all(read(x+'-wire.json')==wire for x in ('held','finished','retained')) and not wire['overflow'] and
        Counter((r['id'],r['name']) for post in wire['outputPosts'] for r in post['records'])==Counter({(1,'Ada'):1,(2,'Grace'):1}) and all(post['hasIdempotencyKey'] for post in wire['outputPosts']),
        'An exhausted worker revived its old HTTP wait or generated additional physical effects.')
    availability=read('snapshot-unavailability.json');input_availability=read('retry-input-unavailability.json');content=q.digest(root/'lookup-source.csv')
    require(q.digest(root/'lookup-snapshot.csv')==content and all(availability[k]==content for k in ('sourceSha256','snapshotSha256Before','snapshotSha256WhileUnavailable','restoredSha256')) and
        availability['restoredBeforeAuthorizedRetry'] and availability['sourceStillAvailable'] and availability['originalSnapshotReadPathMissing'] and
        input_availability['kind']=='actual-owned-bound-lookup-input-unavailability' and input_availability['sourceRevision']==source and input_availability['originalBoundReadPathMissing'] and
        input_availability['originalRetainedSnapshotStillAvailable'] and input_availability['restoredAfterFailedHostStopped'] and
        all(input_availability[k]==content for k in ('sourceSha256Before','sourceSha256WhileUnavailable','restoredSha256')) and
        v.instant(input_availability['inducedAtUtc'])>v.instant(p['heldCutAtUtc']) and v.instant(input_availability['inducedAtUtc'])<v.instant(replacement['worker']['createdAt']) and
        v.instant(input_availability['restoredAtUtc'])>v.instant(replacement['worker']['completedAt']),
        'Budget exhaustion changed immutable lookup bytes or invented its real bound-input unavailability.')
    invocations={}
    for phase in ('before','held','finished'):
        require(set(p[phase+'Stores'])=={'elsa','etl-runtime','etl-design'},'The budget proof lacks all coupled whole stores.')
        for store,snapshot in p[phase+'Stores'].items():
            file=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(file.read_bytes());require(q.digest(file)==snapshot['gzipSha256'] and hashlib.sha256(raw).hexdigest()==snapshot['databaseSha256'] and
                snapshot['observer']['readOnly'] and snapshot['observer']['snapshotSha256']==snapshot['databaseSha256'],'A budget-exhaustion provider snapshot differs from its actual bytes.')
            with tempfile.TemporaryDirectory(prefix='elsa-budget-read-') as tmp:
                db=Path(tmp)/'snapshot.sqlite';db.write_bytes(raw)
                with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                    require(c.execute('PRAGMA integrity_check').fetchall()==[('ok',)],'A budget-exhaustion store failed integrity verification.')
                    if store=='elsa':
                        if phase!='finished':hc.physical_runtime(c,phase,p,checked[phase],first_result,None,None,embedded)
                        else:physical_final_runtime(c,p,f,replacement,results,command,refusal,final,embedded)
                    elif store=='etl-runtime':
                        rows=physical_etl(c,phase,p,before,final,content,availability)
                        for run,inv in rows.items():
                            require(run not in invocations or invocations[run]==inv,'An immutable failed ETL invocation changed between actual stores.')
                            invocations[run]=inv
    if embedded:verify_parent(root,p,checked,final)
    return {'parent':parent,'wholeStoresVerified':9,'physicalAttempts':2,'furtherRetryRefused':True,'additionalPhysicalHttpEffects':0}


def physical_final_runtime(c,p,first,second,results,command,refusal,final,embedded):
    key=v.key;model=first['model'];row,=c.execute('SELECT Revision,Lifecycle,ContentJson FROM elsa_runtime_model_execution WHERE ExecutionId=?',(key(p['modelExecutionId']),)).fetchall()
    require(row[:2]==(model['revision'],3) and json.loads(row[2])['payload']==model['payload'] and json.loads(row[2])['terminalResult']==model['terminalResult'] and
        json.loads(row[2])['executable']==v.encoded_tree(model['executable']) and c.execute('SELECT COUNT(*) FROM elsa_runtime_model_execution').fetchone()[0]==1 and
        c.execute('SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state').fetchone()[0]==2+int(embedded), 'The physical exhausted model lost its terminal state or exact two-worker inventory.')
    receipts=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_model_input')]
    require(len(receipts)==5+int(embedded),'The exhausted model accepted an extra physical retry or lost a retained input receipt.')
    for observed,result in zip((first,second),results):
        origin=result['origin'];worker=observed['worker'];row,=c.execute('SELECT SchemaVersion,Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(key(origin['workerExecutionId']),)).fetchall()
        require(row[0]=='2.0.0' and row[1]==4 and json.loads(row[2])['modelWorkOrigin']==v.encoded_tree(origin) and json.loads(row[2])['pinnedExecutable']==v.encoded_tree(origin['workerExecutable']),
            'An exhausted physical worker lost its protected origin, complete pin or actual fault status.')
        activity,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=?',(key(worker['workflowExecutionId']),)).fetchall();root=json.loads(activity[1])
        require(activity[0]=='Faulted' and root['completion'] is None and root['fault']['code']==key(result['faultCode']), 'A physical worker fault differs from its original authoritative result.')
        receipt=observed['resultInput'];stored,=[r for r in receipts if r['inputId']==key(receipt['inputId'])]
        require(stored['result']==receipt['result'] and stored['contentFingerprint']==key(receipt['contentFingerprint']) and stored['acceptedRevision']==receipt['acceptedRevision'] and
            stored['invocationId']==key(origin['invocationId']) and stored['attemptId']==key(origin['attemptId']), 'The physical old/new result receipt lost its exact failed attempt.')
        for name in ('startSource','resultSource'):
            source=observed[name];row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(key(source['outboxItemId']),)).fetchall();stored=json.loads(row[1])
            require(row[0]==2 and stored['intent']['kind']==key(source['intent']['kind']) and stored['intent']['payload']==source['intent']['payload'] and stored['deliveredAt']==source['deliveredAt'],
                'A physical budgeted worker source differs from its exact consumption/ACK proof.')
    accepted,=[r for r in receipts if r['inputId']==key(command['request']['requestId'])];rejected,=[r for r in receipts if r['inputId']==key(refusal['request']['requestId'])]
    require(accepted['inputKind']==key('retry-service') and accepted['acceptedRevision']==3 and accepted['result']==command['accepted']['result'] and
        accepted['invocationId']==key(results[0]['origin']['invocationId']) and accepted['attemptId']==key(results[0]['origin']['attemptId']) and
        rejected['inputKind']==key('retry-service') and rejected['acceptedRevision']==model['revision'] and rejected['result']==refusal['command']['result'] and
        rejected['invocationId']==key(results[1]['origin']['invocationId']) and rejected['attemptId']==key(results[1]['origin']['attemptId']) and
        not c.execute('SELECT 1 FROM elsa_runtime_scheduler_work_item').fetchall(), 'The physical exhausted retry did not retain its accepted/rejected decisions or left progression queued.')
    material,=[json.loads(r[0])['material'] for r in c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable') if json.loads(r[0]).get('material')]
    process=json.loads((q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/technical-fault-native-recovery/legacy-model-v2.json').read_text())['definition']['process']
    require(material['definition']=={'formatVersion':3,'process':process,'humanBindings':[],'serviceBindings':[{'bindingId':'pipeline','executable':results[0]['origin']['workerExecutable'],
        'businessErrorCode':'etl.rows-rejected','faultPolicy':{'formatVersion':1,'disposition':'HoldForRetry','maxAttempts':2}}]} and
        material['dependencies']==[{'bindingId':key('pipeline'),'executable':v.encoded_tree(results[0]['origin']['workerExecutable'])}],
        'The physical exhausted service changed its pinned budget, business-error boundary or full native dependency.')
    incidents=final['original']['native']['incidents']+final['replacement']['native']['incidents']+(final['original']['embedding']['incidents'] if embedded else [])
    stored=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_incident_state')]
    require(len(stored)==len(incidents) and {r['incidentId'] for r in stored}=={key(i['incidentId']) for i in incidents},'Budget exhaustion lost the original physical native fault incident inventory.')
    for incident in incidents:
        row,=[r for r in stored if r['incidentId']==key(incident['incidentId'])]
        require(row['workflowExecutionId']==key(incident['workflowExecutionId']) and row['activityExecutionId']==key(incident['activityExecutionId']) and row['status']==incident['status'] and
            row['failureType']==key('ActivityReturnedFault') and row['resolutionOutcome']['actionKind']==key(incident['resolutionOutcome']['actionKind']),
            'A physical exhausted-worker/native-parent incident lost its exact owner or fault disposition.')


def physical_etl(c,phase,p,before,final,content,availability):
    c.row_factory=sqlite3.Row;rows=c.execute('SELECT * FROM elsa_etl_runtime_runs').fetchall();require(len(rows)==(2 if phase=='finished' else 1),'Budget exhaustion created a third ETL run or lost a failed invocation.')
    invocations={}
    for row in rows:
        invocation=json.loads(row['InvocationJson']);run=v.decoded(invocation['runId']);original=run==p['runId'];invocations[run]=invocation
        require(original or phase=='finished' and run==p['replacementRunId'],'The budgeted ETL run is not one of its two actual attempts.')
        view=before if phase=='before' else final['original'] if original else final['replacement'];activity,=view['native']['activities'];address=invocation['resultAddress'];pin=p['publication']
        require(v.decoded(address['workflowExecutionId'])==view['native']['instance']['workflowExecutionId'] and v.decoded(address['activityExecutionId'])==activity['activityExecutionId'] and
            v.decoded(address['nodeId'])==activity['executableNodeId'] and v.decoded(address['executableId'])==pin['artifactId'] and v.decoded(address['executableHash'])==pin['artifactHash'] and
            address['registrationId']==address['bookmarkId'] and v.decoded(address['providerId'])=='elsa.etl.completion.v1', 'A budgeted ETL invocation lost its actual native result address or worker pin.')
        version=p['etlPublication']['version'];artifact=invocation['artifact'];connector=c.execute('SELECT BindingsJson,PreparedJson FROM elsa_etl_connector_runs WHERE RunKey=?',(row['RunKey'],)).fetchone()
        binding=json.loads(connector[0])['lookup']
        require(artifact['formatVersion']==2 and artifact['definitionVersion']==version['definitionVersion'] and v.decoded(artifact['definitionId'])==version['definitionId'] and
            v.decoded(artifact['contentHash'])==version['contentHash'] and binding['kind']=='csv' and v.decoded(binding['revision']['revision'])=='lookup-v1' and
            v.decoded(binding['revision']['expectedSha256'])==content, 'A budgeted ETL run changed its immutable artifact or original lookup binding.')
        if original:
            prepared=json.loads(connector[1])['lookup'];require(v.decoded(prepared['contentHash'])==content and v.decoded(prepared['key'])==availability['snapshotKey'] and prepared['byteLength']==34,
                'The original failed ETL run lost its prepared immutable lookup checkpoint.')
        else:
            require(connector[1] is None and not c.execute('SELECT 1 FROM elsa_etl_connector_partitions WHERE RunKey=?',(row['RunKey'],)).fetchall(),
                'The genuine replacement preparation fault incorrectly created runnable ETL partitions.')
        terminals=c.execute('SELECT Outcome,ResultJson FROM elsa_etl_runtime_terminals WHERE RunKey=?',(row['RunKey'],)).fetchall()
        outbox=c.execute('SELECT Attempts,Disposition,AcknowledgedAtUtcTicks FROM elsa_etl_runtime_outbox WHERE RunKey=?',(row['RunKey'],)).fetchall()
        if phase=='before':require(row['Status']==1 and not terminals and not outbox,'The original real HTTP wait already carried a terminal result.')
        else:
            require(row['Status']==4 and row['AcceptedRecords']==(2 if original else 0) and row['RejectedRecords']==row['OutstandingRecords']==row['OutstandingBytes']==0 and
                len(terminals)==len(outbox)==1 and terminals[0][0]==2 and outbox[0][0]>=1 and outbox[0][1]==2 and outbox[0][2] is not None,
                'An original/replacement ETL fault lost its terminal accounting or acknowledged native delivery.')
            terminal=json.loads(terminals[0][1]);require(terminal['outcome']=='faulted' and v.decoded(terminal['diagnostic']['code'])==('etl.snapshot-read-failed' if original else 'etl.binding-read-failed'),
                'An actual budgeted ETL technical fault was relabeled as another classifier or outcome.')
    return invocations


def verify_parent(root,p,checked,final):
    read=lambda name:json.loads((root/name).read_text());phases={x:read(x+'-checked-embedding.json') for x in ('before','held','finished','retained')}
    before=phases['before'];after=phases['finished'];encoded=before['model']['payload']['nativeParent']['invocation'];origin=h.opaque(encoded);absorbed=p['embeddedParent']=='CompensationScope'
    pin={k:p['nativeParentPublication'][k] for k in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')}
    roots={'Root':'Elsa.Activities.Bpmn.Activities.RunBpmn','Sequence':'Elsa.Activities.Sequence.Activities.Sequence','Flowchart':'Elsa.Activities.Flowchart.Activities.Flowchart','CompensationScope':'Elsa.Activities.Compensation.Activities.CompensationScope'}
    require(after==phases['retained'] and origin['nativeExecutionId']==p['nativeParentExecutionId'] and origin['nativeExecutable']==pin and origin['executionId']==p['modelExecutionId'] and
        origin['model']['executable']==p['modelPublication']['executable'] and origin['model']['materialIncarnationId']==p['modelPublication']['incarnationId'],
        'The exhausted native parent lost its original full model invocation or immutable pins.')
    for phase in ('before','held','finished'):
        item=phases[phase];run,=[a for a in item['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
        require(item['model']==checked[phase]['model'] and item['model']['payload']['nativeParent']['invocation']==encoded and item['nativeParent']['pinnedExecutable']==pin and
            item['nativeParent']['authority']==item['model']['authority'] and item['nativeParent']['status']==((3 if absorbed else 4) if phase=='finished' else 1) and
            run['invocationId']==origin['nativeInvocationId'] and run['status']==(5 if phase=='finished' else 3) and
            not [a for a in item['activities'] if a['execution']['executableNodeId']=='after'], 'The native parent lost its real wait or advanced after exhausted service fault.')
        if phase!='finished':
            bookmark,=item['bookmarks'];require(bookmark['bookmarkId']==origin['registrationId'] and bookmark['stimulusHash']==p['modelExecutionId'] and
                run['privateState']['value']['inlineValue']['Invocation']==encoded and run['privateState']['producedByAttemptId']==origin['nativeAttemptId'],
                'The held native parent lost its actual registration or original private invocation.')
        else:
            require(not item['bookmarks'] and not item['queuedModelInputs'] and run['completion'] is None and run['fault']['code']=='bpmn.model-faulted' and
                any(r['status']==1 and r['registrationId']==origin['registrationId'] and r['providerId']=='elsa.bpmn.native-parent.v1' and r['payloadType']['alias']=='Elsa.Activities.Bpmn.Models.RunBpmnCompletion' for r in run['triggerDeliveries']),
                'The exhausted native parent lacks its actual typed fault-result consumption.')
    root_activity,=[a for a in after['activities'] if a['execution']['activityType']==roots[p['embeddedParent']]]
    require(root_activity['status']=={'Root':5,'Sequence':1,'Flowchart':5,'CompensationScope':4}[p['embeddedParent']], 'The native root fault disposition was normalized instead of preserved.')
    incidents=final['original']['embedding']['incidents'];faulted={a['invocationId'] for a in after['activities'] if a['status']==5}
    require(len(incidents)==len(faulted) and {i['activityExecutionId'] for i in incidents}==faulted and all(i['status']==('Resolved' if absorbed else 'Blocking') and
        i['resolutionOutcome']['actionKind']==('AbsorbFault' if absorbed else 'FaultWorkflow') for i in incidents), 'The native parent lost its default fault/absorbed-fault incident disposition.')
    if absorbed:require(root_activity['completion']['outcomeKey']=='Compensated' and all(not i['isBlocking'] and i['resolvedAt'] is not None and
        i['metadata'].get('runtime.faultAbsorbedBy')==root_activity['invocationId'] for i in incidents),'The compensation scope lost its original Compensated/AbsorbFault truth.')
    settlement,=[r for r in after['inputs'] if r['inputKind']=='native-parent-settled'];source,=[s for s in after['sources'] if s['intent']['kind']=='Elsa.Bpmn.ResumeNativeParent.v1']
    require(settlement['acceptedRevision']==5 and settlement['result']['status']=='Accepted' and settlement['invocationId']==origin['nativeInvocationId'] and settlement['attemptId']==origin['nativeAttemptId'] and
        len(after['inputs'])==6 and Counter(s['intent']['kind'] for s in after['sources'])==Counter({'Elsa.Bpmn.StartEmbedded.v1':1,'Elsa.Bpmn.StartNativeWork.v1':2,'Elsa.Bpmn.ResumeNativeParent.v1':1}) and
        all(s['status']==2 for s in after['sources']) and source['intent']['payload']=={'FormatVersion':1,'Invocation':encoded,'Result':after['model']['terminalResult']} and
        v.instant(source['deliveredAt'])>=v.instant(settlement['acceptedAt']), 'The exhausted parent source was acknowledged without its exact native consumption/model settlement.')
    for phase in ('before','held','finished'):
        with tempfile.TemporaryDirectory(prefix='elsa-budget-parent-read-') as tmp:
            db=Path(tmp)/'snapshot.sqlite';db.write_bytes(gzip.decompress((root/p[phase+'Stores']['elsa']['file']).read_bytes()))
            with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                item=phases[phase];row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(v.key(p['nativeParentExecutionId']),)).fetchall()
                require(row[0]==item['nativeParent']['status'] and json.loads(row[1])['pinnedExecutable']==v.encoded_tree(pin),'The physical exhausted parent lost its original pin or header status.')
                for activity in item['activities']:
                    row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state WHERE ActivityExecutionId=?',(v.key(activity['invocationId']),)).fetchall();stored=json.loads(row[1])
                    require(row[0]==('Scheduled','Running','Waiting','Suspended','Completed','Faulted','Cancelled','Recovered','Superseded')[activity['status']] and
                        (stored['fault']['code']==v.key(activity['fault']['code']) if activity['fault'] is not None else stored['fault'] is None),
                        'The physical native parent activity lost its actual fault disposition.')
                    if phase=='finished' and absorbed and activity['execution']['activityType']==roots['CompensationScope']:
                        require(stored['completion']['outcomeKey']==v.key('Compensated'),'The physical compensation scope lost its retained Compensated result.')
                material,=c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable WHERE ArtifactId=?',(v.key(pin['artifactId']),)).fetchall();material=json.loads(material[0])
                require(material['rootActivity']['activityType']==v.key(roots[p['embeddedParent']]) and material['identity']==v.encoded_tree(pin) and
                    any(d['artifactId']==v.key(p['modelPublication']['executable']['artifactId']) and d['artifactHash']==v.key(p['modelPublication']['executable']['artifactHash']) for d in material['dependencies']),
                    'The real exhausted parent root shape or immutable model dependency closure differs.')
                for source in item['sources']:
                    row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(v.key(source['outboxItemId']),)).fetchall()
                    require(row[0]==source['status'] and json.loads(row[1])['intent']['payload']==source['intent']['payload'] and json.loads(row[1])['deliveredAt']==source['deliveredAt'],
                        'The physical exhausted parent source differs from its exact settlement/ACK proof.')
                for receipt in item['inputs']:
                    row,=c.execute('SELECT ContentJson FROM elsa_runtime_model_input WHERE InputId=?',(v.key(receipt['inputId']),)).fetchall();stored=json.loads(row[0])
                    require(stored['result']==receipt['result'] and stored['contentFingerprint']==v.key(receipt['contentFingerprint']),'The physical exhausted parent settlement receipt differs from its retained input.')


def verify(path):
    root=path.resolve().parent;m=json.loads(path.read_text());source=m['sourceRevision'];cases=m['cases']
    require(m['schemaVersion']==1 and m['kind']=='actual-bpmn-service-budget-exhaustion-matrix' and re.fullmatch('[a-f0-9]{40}',source) and len(cases)==5 and
        {c['name'] for c in cases}=={'direct','root','sequence','flowchart','compensationscope'},'The budget matrix lacks every unique direct/native parent.')
    names={'process.json','before.json','recovered.json','held.json','held-after-restart.json','finished.json','retained.json','retry-command.json','exhausted-retry-refusal.json',
        'snapshot-unavailability.json','retry-input-unavailability.json','lookup-source.csv','lookup-snapshot.csv'}|{x+'-wire.json' for x in ('before','held','finished','retained')}|{
        x+'-checked-model.json' for x in ('before','held','finished','retained')}|{x+'-checked-replacement.json' for x in ('finished','retained')}|{
        x+'-'+s+'.sqlite.gz' for x in ('before','held','finished') for s in ('elsa','etl-runtime','etl-design')}
    products=None;traces={};results=[]
    for case in cases:
        name=case['name'];expected=names|({x+'-checked-embedding.json' for x in ('before','held','finished','retained')} if name!='direct' else set())
        require(len(case['assets'])==len(expected) and {a['path'] for a in case['assets']}=={name+'/'+n for n in expected},'The budget proof lacks its exact raw assets.')
        for asset in case['assets']:require(q.digest(q.evidence_file(root,asset['path']))==asset['sha256'],'An actual budget-exhaustion asset differs.')
        results.append(verify_case(root/name,name,source));p=json.loads((root/name/'process.json').read_text())
        if products is None:products=p['products']
        require(products==p['products'],'The budget cases differ in captured first-party product versions/hashes.')
        for phase in ('before','held','finished'):
            def normalize(value):
                if isinstance(value,dict):return {k:'<model-execution>' if k=='producingWorkHandle' and x==p['modelExecutionId'] else normalize(x) for k,x in value.items()}
                if isinstance(value,list):return [normalize(x) for x in value]
                return value
            state=normalize(json.loads((root/name/(phase+'-checked-model.json')).read_text())['model']['payload']['state'])
            require(phase not in traces or traces[phase]==state,'The direct/native-parent exhausted interpreter histories differ.')
            traces[phase]=state
    return {'actualBudgetExhaustionJourneys':results,'wholeStoresVerified':45,'sourceRevision':source,'budgetExhaustionProved':True,'bothFailedAttemptsPreserved':True,
        'furtherRetryRefusedWithoutNewAttempt':True,'sameSourceInterpreterTracesCompared':True,'nativeCompositeFaultStatusDecisionResolved':False,
        'timerInterruptionProved':False,'competingRetryCancelCommitOrdersProved':False,'obsoleteResultControlsProved':False,'authorizationBeforeReplayProved':False,'finalAffectedSourceUpgradeAndStudioProved':False}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--manifest',type=Path,required=True);print(json.dumps(verify(parser.parse_args().manifest),indent=2))
