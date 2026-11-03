#!/usr/bin/env python3
"""Read-only proof of both actual native-admission/model-cancellation commit orders."""
import argparse,gzip,hashlib,importlib.util,json,sqlite3,tempfile
from contextlib import closing
from pathlib import Path
_spec=importlib.util.spec_from_file_location('retirement',Path(__file__).with_name('verify_native_start_retirement.py'))
r=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(r)
v=r.v;q=r.q
START='Elsa.Bpmn.StartNativeWork.v1';CANCEL='Elsa.Bpmn.CancelNativeWork.v1';RESULT='Elsa.Bpmn.NativeWorkResult.v1'


def verify(path):
    root=path.parent;m=json.loads(path.read_text());source=m['sourceRevision']
    if m.get('kind')=='actual-bpmn-native-admission-cancellation-race-matrix':
        if m.get('schemaVersion')!=1 or len(m['cases'])!=2 or {c['order'] for c in m['cases']}!={'cancel-first','admit-first'}:
            raise ValueError('The admission race matrix lacks both actual commit orders.')
        results=[]
        for case in m['cases']:
            if case['manifest']!=case['order']+'/manifest.json':raise ValueError('A race case changed its bound evidence path.')
            f=q.evidence_file(root,case['manifest'])
            if q.digest(f)!=case['sha256']:raise ValueError('An actual race case manifest differs.')
            result=verify(f)
            if result['sourceRevision']!=source or result['raceOrder']!=case['order']:raise ValueError('A race case changed its exact source or order.')
            results.append(result)
        return {'sourceRevision':source,'actualCommitOrders':[z['raceOrder'] for z in results], 'admissionRaceProved':True,
            'wholeStoresVerified':18,'actualWorkbenchProcesses':6,'actualSdkProcesses':6,'nativeWorkflowsAdmitted':1,'etlRunsAdmitted':0,
            'pendingResultCrashProved':False,'runBpmnEtlIntegrationProved':False,'modelHeldFaultRetryProved':False}
    if m.get('schemaVersion')!=1 or m.get('kind')!='actual-bpmn-native-admission-cancellation-race' or len(source)!=40:
        raise ValueError('Unknown actual admission race evidence.')
    names={'process.json','sdk-dispatch.json','cancel-response.json'}|{'race-'+n+'.json' for n in ('proposal','release','competitor','gated','competing-checked')}
    names|={p+'-'+s+'.sqlite.gz' for p in ('before','pending','finished') for s in ('elsa','etl-runtime','etl-design')}
    names|={p+suffix+'.json' for p in ('before','pending','finished','retained') for suffix in ('','-checked')}
    names|={p+'-wire.json' for p in ('before','finished','retained')}
    if len(m['assets'])!=len(names) or {a['path'] for a in m['assets']}!=names:raise ValueError('The race lacks its complete actual observations.')
    for asset in m['assets']:
        if q.digest(q.evidence_file(root,asset['path']))!=asset['sha256']:raise ValueError('An actual race asset differs.')
    def read(name):return json.loads(q.evidence_file(root,name+'.json').read_text())
    process=read('process');order=process['raceOrder'];admitted=order=='admit-first';children=process['processes']
    roles=['http-fixture','workbench','native-sdk','gated-sdk','competing-sdk','workbench','workbench']
    if (order not in ('cancel-first','admit-first') or process['sourceRevision']!=source or process['products']['sourceCommit']!=source or
            process['predecessorExitCode']!=-9 or process['sweepIntervalsSeconds']!=[300,.2,.2] or process['queuedNativeStage']!='checkpoint' or
            [c['role'] for c in children]!=roles or len({c['processId'] for c in children})!=7 or len({c['controllerProcessId'] for c in children})!=1):
        raise ValueError('The race lost its exact source and actual process-loss/restart provenance.')
    checked={p:read(p+'-checked') for p in ('before','pending','finished','retained')};views={p:read(p) for p in checked}
    before=checked['before'];pending=checked['pending'];finished=checked['finished'];origin=process['origin'];sdk=read('sdk-dispatch')
    gated=read('race-gated');competitor=read('race-competitor');observed=read('race-proposal');release=read('race-release');between=read('race-competing-checked')
    proposal=observed['proposal'];work,=before['queuedNativeWork'];commit='commit:'+work['workItemId']
    if (sdk['operation']!='enqueue-model-owned-native-start' or sdk['drainPolicy']!='OneWorkItem' or sdk['modelUnchanged'] is not True or sdk['nativeWorkerAdmitted'] is not False or
            sdk['revision']!=1 or sdk['executionId']!=origin['executionId'] or sdk['origin']!=origin or sdk['queuedWork']!=[work] or sdk['expectedRetirementCommitId']!=commit or
            len(sdk['products'])!=4 or sdk['products'][0]['sha256']!=process['dispatcherSha256'] or any(source not in p['informationalVersion'] for p in sdk['products'])):
        raise ValueError('The actual SDK lost the original unadmitted checkpoint or exact source.')
    queued=r.fields(work['payload'])
    if work['commandKind']!=11 or queued['modelworkorigin']!=r.fields(origin) or queued['pinnedexecutable']!=r.fields(origin['workerExecutable']) or queued['checkpointname']!='WorkflowStarted':
        raise ValueError('The real native checkpoint changed the exact invocation or pin.')
    intents=queued['postcommitintents']
    if len(intents)!=1 or intents[0]['kind']!='Elsa.Workflows.Runtime.EnqueueSchedulerWork' or intents[0]['payload']['commandkind']!=7:
        raise ValueError('The queued native admission lost its genuine root-schedule responsibility.')
    if (observed['processId']!=children[3]['processId'] or release['processId']!=observed['processId'] or observed['observedModel']!=before['model'] or
            release['proposalUnchanged'] is not True or release['proposalFingerprint']!=observed['proposalFingerprint'] or len(release['proposalFingerprint'])!=64 or
            not (v.instant(gated['startedAtUtc'])<=v.instant(observed['observedAtUtc'])<v.instant(competitor['startedAtUtc'])<=v.instant(competitor['completedAtUtc'])<
                v.instant(process['releaseRequestedAtUtc'])<=v.instant(release['releasedAtUtc'])<=v.instant(gated['completedAtUtc']))):
        raise ValueError('The unchanged proposal was not observed before the competitor committed and released afterward.')
    for client in (gated,competitor):
        if (client['origin']!=origin or len(client['products'])!=5 or client['products'][0]['sha256']!=process['dispatcherSha256'] or
                any(source not in p['informationalVersion'] for p in client['products'])):raise ValueError('A real race writer changed its source or invocation.')
    if not admitted:
        if (observed['operation']!='--drain-checkpoint' or gated['operation']!='--drain-checkpoint' or competitor['operation']!='--cancel-model' or
                proposal.get('expectedModelExecution')!=before['model'] or proposal.get('modelState') is not None or proposal['commitId']!=commit or
                proposal['checkpoint']['name']!='WorkflowStarted' or proposal['stateChanges']['workflowExecution']['state']['modelWorkOrigin']!=origin or
                between['model']!=pending['model'] or between['worker'] is not None or between['nativeMarkers'] or
                gated['worker'] is not None or gated['model']!=pending['model'] or gated['queuedNativeWork']!=pending['queuedNativeWork']):
            raise ValueError('The native proposal lacks its actual revision-1 admission guard or lost to a different model decision.')
        outcome=gated['outcome'];item,=outcome['items']
        if (outcome['drainedCount']!=0 or outcome['stoppedOnDeferral'] is not True or outcome['stoppedOnFault'] or item['status']!=3 or item['workItemId']!=work['workItemId'] or
                item['error']!='The authorizing execution changed or is unavailable; cross-model admission remains owed.' or
                pending['queuedNativeWork'][0]['payload']!=work['payload']):
            raise ValueError('The real provider did not refuse stale native admission and retain its exact queued work.')
    else:
        model_state=proposal.get('modelState',{})
        if (observed['operation']!='--cancel-model' or gated['operation']!='--cancel-model' or competitor['operation']!='--drain-checkpoint' or
                model_state.get('expectedRevision')!=1 or model_state.get('state')!=pending['model'] or model_state.get('input') not in pending['inputs'] or
                between['model']!=before['model'] or between['worker']!=pending['worker'] or competitor['worker']!=pending['worker'] or
                competitor['model']!=before['model'] or gated['model']!=pending['model'] or gated['worker']!=pending['worker'] or
                competitor['outcome']!={'status':'TotalCycleBudgetReached','workflowExecutionId':origin['workerExecutionId'],'maxCycles':2,'cycles':2}):
            raise ValueError('Native admission did not commit while the unchanged cancellation proposal still observed revision 1.')
        if not any(json.loads(x['contentJson'])['commitId']==commit for x in between['nativeMarkers']):
            raise ValueError('The actual native admission marker was absent before cancellation released.')
    if checked['finished']!=checked['retained'] or views['finished']!=views['retained']:raise ValueError('The race changed after its third real host.')
    interpreter=before['model']['payload']['state'];material=before['workerMaterial'];model_pin=process['modelPublication']['executable']
    worker_pin={k:process['publication'][k] for k in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')}
    for phase,c in checked.items():
        model=c['model'];view=views[phase];settled=phase in ('finished','retained');revision=1 if phase=='before' else 3 if settled else 2
        if (c['readOnly'] is not True or c['origin']!=origin or c['workerMaterial']!=material or material['identity']!=worker_pin or origin['workerExecutable']!=worker_pin or
                origin['executable']!=model_pin or model['executable']!=model_pin or model['executionId']!=origin['executionId'] or model['scope']!=origin['scope'] or
                model['materialIncarnationId']!=origin['materialIncarnationId'] or model['materialIncarnationId']!=process['modelPublication']['incarnationId'] or
                model['revision']!=revision or view['model']['revision']!=revision or model['payload']['state']!=interpreter or view['runs']!={'items':[],'nextCursor':None}):
            raise ValueError('A race phase changed its exact pin, invocation, interpreter history or admitted ETL.')
        actual_worker=admitted and phase!='before'
        if (c['worker'] is not None)!=actual_worker or len(view['native'])!=int(actual_worker):raise ValueError('Actual native admission contradicts its commit order.')
        if actual_worker:
            worker=c['worker'];native,=view['native']
            if (worker['workflowExecutionId']!=origin['workerExecutionId'] or worker['pinnedExecutable']!=worker_pin or worker['modelWorkOrigin']!=origin or
                    worker['status']!=(5 if settled else 1) or native['status']!=('Cancelled' if settled else 'Running') or native['incidentCount'] or native['blockingIncidentCount']):
                raise ValueError('The admitted native worker lost its origin, truthful cancellation or fault-free admission.')
        if phase=='before':
            if model['lifecycle']!=0 or model['terminalResult'] is not None or len(c['inputs'])!=1 or len(c['sources'])!=1 or c['nativeMarkers']:
                raise ValueError('The actual race did not begin with one owed live invocation.')
            continue
        cancellation,=model['payload']['cancellations']
        if (model['lifecycle']!=(3 if settled else 2) or model['payload']['work'] or model['terminalResult']!={'status':'Cancelled','reason':'Retire the original native start before admission.'} or
                cancellation['origin']!=origin or cancellation['settled'] is not settled or cancellation.get('notAdmitted',False) is not (settled and not admitted) or
                len(c['inputs'])!=(3 if settled else 2)):
            raise ValueError('The pending/settled race decision fabricated retirement or lost cancellation responsibility.')
        start,=[s for s in c['sources'] if s['intent']['kind']==START];cancel,=[s for s in c['sources'] if s['intent']['kind']==CANCEL]
        original_cancel={k:z for k,z in {**cancellation,'settled':False}.items() if k!='notAdmitted'}
        if start['outboxItemId']!=origin['sourceOutboxItemId'] or start['intent']['payload']!=origin or cancel['outboxItemId']!=cancellation['sourceOutboxItemId'] or cancel['intent']['payload']!=original_cancel:
            raise ValueError('The original start/cancel source changed during the race.')
        if not settled:
            if any(s['status'] in (2,4) for s in (start,cancel)) or model['payload'].get('nativeResults'):
                raise ValueError('The actual race acknowledged pending work before recovery.')
        else:
            if c['queuedNativeWork'] or c['queuedModelInputs'] or any(s['status']!=2 for s in c['sources']):raise ValueError('The actual race left recovery responsibilities owed.')
            receipt,=[x for x in c['inputs'] if x['inputKind']==('native-result' if admitted else 'native-start-retired')]
            if (receipt['acceptedRevision']!=3 or receipt['invocationId']!=origin['invocationId'] or receipt['attemptId']!=origin['attemptId'] or
                    receipt['result']['status']!='Accepted' or receipt['result']['lifecycle']!='TerminalSettled' or v.instant(cancel['deliveredAt'])<v.instant(receipt['acceptedAt'])):
                raise ValueError('The cancellation source lacks its exact retained settlement receipt.')
            if admitted:
                result,=model['payload']['nativeResults'];source_item,=[s for s in c['sources'] if s['intent']['kind']==RESULT]
                if (result['origin']!=origin or result['status']!=5 or result['sourceOutboxItemId']!=source_item['outboxItemId'] or
                        result['cancellationSources'] or source_item['intent']['payload']!=result or receipt['inputId']!=origin['resultInputId'] or
                        v.instant(source_item['deliveredAt'])<v.instant(receipt['acceptedAt'])):raise ValueError('The admitted race fabricated or lost its actual native terminal result.')
            else:
                if (model['payload'].get('nativeResults') or receipt['inputId']!='native-start-retired:'+r.identity(origin['sourceOutboxItemId'],cancellation['sourceOutboxItemId']) or
                        v.instant(start['deliveredAt'])<v.instant(receipt['acceptedAt']) or v.instant(cancel['deliveredAt'])<v.instant(start['deliveredAt'])):
                    raise ValueError('The unadmitted race fabricated native truth or acknowledged before exact retirement.')
                markers=c['nativeMarkers']
                if len(markers)!=1 or json.loads(markers[0]['contentJson'])['commitId']!=commit or json.loads(markers[0]['pendingPostCommitWorkIdsJson']):
                    raise ValueError('The race lost its original rootless retirement marker.')
    response=read('cancel-response');cancel_receipt,=[x for x in pending['inputs'] if x['inputKind']=='cancel']
    if response['pending'] or response['result']!=cancel_receipt['result'] or cancel_receipt['acceptedRevision']!=2:raise ValueError('The original cancellation response differs from its actual receipt.')
    wires=[read(p+'-wire') for p in ('before','finished','retained')]
    if any(z!=wires[0] for z in wires) or wires[0]!={'requests':[],'outputPosts':[],'overflow':False}:raise ValueError('The admission race produced an unexpected HTTP effect.')
    materials=None
    for phase in ('before','pending','finished'):
        for store,snapshot in process[phase+'Stores'].items():
            file=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(file.read_bytes())
            if q.digest(file)!=snapshot['gzipSha256'] or hashlib.sha256(raw).hexdigest()!=snapshot['databaseSha256'] or snapshot['observer']['snapshotSha256']!=snapshot['databaseSha256'] or snapshot['observer']['readOnly'] is not True:
                raise ValueError('An actual provider-observed race store differs.')
            with tempfile.TemporaryDirectory(prefix='elsa-race-read-') as temporary:
                db=Path(temporary)/'s.sqlite';db.write_bytes(raw)
                with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                    if c.execute('PRAGMA integrity_check').fetchall()!=[('ok',)]:raise ValueError('An actual race store failed integrity.')
                    if store=='etl-runtime' and c.execute('SELECT COUNT(*) FROM elsa_etl_runtime_runs').fetchone()[0]:raise ValueError('The physical race admitted an ETL run.')
                    if store!='elsa':continue
                    observed_state=checked[phase];current=observed_state['model']
                    row,=c.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchall();physical=json.loads(row[0])
                    if physical['payload']!=current['payload'] or physical['revision']!=current['revision'] or physical['terminalResult']!=current['terminalResult'] or physical['executable']!=v.encoded_tree(model_pin):raise ValueError('The physical race model differs from its inspected decision.')
                    rows=[json.loads(x[0]) for x in c.execute('SELECT ContentJson FROM elsa_runtime_model_input')]
                    if len(rows)!=len(observed_state['inputs']):raise ValueError('The physical race lost a model input receipt.')
                    for receipt in observed_state['inputs']:
                        row,=[z for z in rows if z['inputId']==v.key(receipt['inputId'])]
                        if row['result']!=receipt['result'] or row['acceptedRevision']!=receipt['acceptedRevision'] or row['contentFingerprint']!=v.key(receipt['contentFingerprint']):raise ValueError('A physical race receipt differs from its exact consumption proof.')
                    rows=[json.loads(x[0]) for x in c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox')]
                    if len(rows)!=len(observed_state['sources']):raise ValueError('The physical race lost a durable delivery obligation.')
                    for delivery in observed_state['sources']:
                        row,=[z for z in rows if z['outboxItemId']==v.key(delivery['outboxItemId'])]
                        if row['intent']['payload']!=delivery['intent']['payload'] or row['deliveredAt']!=delivery['deliveredAt'] or row['status']!=['Pending','Delivering','Delivered','FailedRetryable','FailedFinal'][delivery['status']]:raise ValueError('A physical race delivery differs from its inspected acknowledgment.')
                    rows=[json.loads(x[0]) for x in c.execute('SELECT ContentJson FROM elsa_runtime_workflow_execution_state')]
                    worker=observed_state['worker']
                    if c.execute('SELECT COUNT(*) FROM elsa_runtime_incident_state').fetchone()[0]:raise ValueError('The actual race produced a physical native incident.')
                    if len(rows)!=int(worker is not None):raise ValueError('The physical native admission contradicts the actual commit order.')
                    if worker is not None:
                        row,=rows
                        if row['workflowExecutionId']!=v.key(origin['workerExecutionId']) or row['pinnedExecutable']!=v.encoded_tree(worker_pin) or r.fields(row['modelWorkOrigin'])!=r.fields(v.encoded_tree(origin)) or row['status']!=('Cancelled' if phase=='finished' else 'Running'):raise ValueError('The physical native worker lost its exact pinned model origin or terminal truth.')
                    elif c.execute('SELECT COUNT(*) FROM elsa_runtime_activity_execution_state').fetchone()[0] or c.execute('SELECT COUNT(*) FROM elsa_runtime_bookmark_state').fetchone()[0]:raise ValueError('The unadmitted race created physical native activity/bookmark state.')
                    queue_rows=c.execute('SELECT SchemaVersion,ContentJson FROM elsa_runtime_scheduler_work_item').fetchall()
                    if len(queue_rows)!=len(observed_state['queuedNativeWork']):raise ValueError('The physical race queue differs from its retained work.')
                    for version,content in queue_rows:
                        row=json.loads(content);item,=[z for z in observed_state['queuedNativeWork'] if v.key(z['workItemId'])==row['workItemId']]
                        expected_version='2.0.0' if admitted and phase=='pending' else '3.0.0'
                        if version!=expected_version or row['payload']!=item['payload']:raise ValueError('The physical race changed the protected queued native command.')
                    if admitted and phase=='pending' and (len(queue_rows)!=1 or observed_state['queuedNativeWork'][0]['commandKind']!=12):raise ValueError('The accepted native admission did not stop before root invocation.')
                    markers=c.execute('SELECT CommitId,Fingerprint,ContentJson FROM elsa_runtime_checkpoint_commit WHERE WorkflowExecutionId=?',(v.key(origin['workerExecutionId']),)).fetchall()
                    if sorted(markers)!=sorted((z['commitId'],z['fingerprint'],z['contentJson']) for z in observed_state['nativeMarkers']):raise ValueError('The physical race lost an original native checkpoint marker.')
                    actual=c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable ORDER BY ContentJson').fetchall()
                    if materials is None:materials=actual
                    if actual!=materials:raise ValueError('The race changed its frozen executable material.')
    return {'sourceRevision':source,'raceOrder':order,'admissionRaceProved':True,'wholeStoresVerified':9,'actualWorkbenchProcesses':3,'actualSdkProcesses':3,'nativeWorkflowsAdmitted':int(admitted),'etlRunsAdmitted':0,'pendingResultCrashProved':False,'runBpmnEtlIntegrationProved':False,'modelHeldFaultRetryProved':False}


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--manifest',required=True,type=Path)
    print(json.dumps(verify(p.parse_args().manifest.resolve(strict=True)),indent=2))
