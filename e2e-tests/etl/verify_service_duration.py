#!/usr/bin/env python3
"""Read-only verification of actual ETL/native worker duration-interruption captures."""
import argparse,base64,gzip,hashlib,importlib.util,json,re,sqlite3,tempfile
from datetime import datetime
from contextlib import closing
from pathlib import Path
# Copied into e2e-tests/etl beside the existing held-retry verifier after capture completes.
_spec=importlib.util.spec_from_file_location('service_duration_held',Path(__file__).with_name('verify_held_service_retry.py'))
h=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(h)
v=h.v;q=h.q

def require(c,m):
    if not c:raise ValueError(m)

def ticks(value):
    d,f=v.instant(value);delta=d-datetime(1,1,1)
    return (delta.days*86400+delta.seconds)*10_000_000+int(f)

def verify_case(root,parent,order,source):
    read=lambda n:json.loads((root/n).read_text());p=read('process.json');embedded=parent!='direct';held=order=='held-interruption';first=order=='timer-first'
    before=read('before.json');middle=read('middle.json');final=read('finished.json');checked={phase:read(phase+'-checked-model.json') for phase in ('before','middle','finished','retained')}
    timers={phase:read(phase+'-checked-timers.json') for phase in ('before','middle','finished','retained')};model=timers['before']['model'];fm=timers['finished']['model'];origin=checked['before']['worker']['modelWorkOrigin'];timer=p['timer']
    require(p['schemaVersion']==1 and p['kind']=='actual-bpmn-etl-duration-race' and p['sourceRevision']==source and p['order']==order and p['parent']=='BpmnModel' and
        p['embeddedParent']=={'root':'Root','sequence':'Sequence','flowchart':'Flowchart','compensationscope':'CompensationScope'}.get(parent) and
        p['durationSeconds']==(10 if first else 90 if held else 60) and p['retryAfterSeconds']==(60 if first else 20) and p['resumptionSweepIntervalSeconds']==.2 and
        p['predecessorExitCode']==p['middleExitCode']==-9 and [x['role'] for x in p['processes']]==['http-fixture']+['workbench']*4 and
        len({x['processId'] for x in p['processes']})==5 and len({x['controllerProcessId'] for x in p['processes']})==1,
        'The actual service/duration source, timing or two process-loss cuts differ.')
    products=p['products'];require(products['sourceCommit']==source and len(products['actualProductVersions'])==12 and
        all(x['productVersion'].endswith('+'+source) for x in products['actualProductVersions']), 'The service duration products differ from their source.')
    require(checked['finished']==checked['retained'] and timers['finished']==timers['retained'] and final==read('retained.json') and
        read('finished-wire.json')==read('retained-wire.json') and read('middle-after-restart.json')==middle,
        'A service/timer winner or held failure changed in another process.')
    require(model['revision']==1 and model['payload']['formatVersion']==(12 if embedded else 11) and model['payload']['failures']==[] and
        len(model['payload']['work'])==2 and next(w['timer'] for w in model['payload']['work'] if w.get('timer'))==timer and
        next(w['nativeWork'] for w in model['payload']['work'] if w.get('nativeWork'))==origin and
        timer['executable']==model['executable']==p['modelPublication']['executable'] and timer['materialIncarnationId']==model['materialIncarnationId'] and
        timer['scope']==model['scope'] and timer['executionId']==model['executionId']==p['modelExecutionId'] and
        fm['payload']['work']==[] and fm['terminalResult']=={'status':'Completed','outcome':'Done'} and fm['lifecycle']==3 and
        fm['revision']==(3 if first or held else 2)+int(embedded), 'The timed model lost its original exact work or actual terminal settlement.')
    elapsed,=[r for r in timers['finished']['inputs'] if r['inputKind']=='timer-elapsed'];native=checked['finished']['resultInput'];result,=fm['payload']['nativeResults']
    require(elapsed['invocationId']==timer['invocationId'] and elapsed['attemptId']==timer['attemptId'] and v.instant(elapsed['acceptedAt'])>=v.instant(timer['dueAt']) and
        elapsed['result']['status']==('Rejected' if order=='worker-first' else 'Accepted') and elapsed['acceptedRevision']==(2 if first else 3 if held else fm['revision']) and
        (elapsed['result']['code']=='execution-terminal' if order=='worker-first' else elapsed['result']['code'] is None) and
        native['acceptedRevision']==(3 if first else 2) and result['origin']==origin and len(fm['payload']['nativeResults'])==1,
        'The actual worker/timer acceptance order or exact physical-attempt receipt differs.')
    h.source_and_receipt(checked['finished'],result,3 if first else 2)
    end='end' if order=='worker-first' else 'timeout-end';other='timeout-end' if end=='end' else 'end'
    require(any(d['elementId']==end for d in fm['payload']['state']['diagnostics']) and not any(d['elementId']==other for d in fm['payload']['state']['diagnostics']),
        'The real service duration model advanced down the losing route.')
    expected_native='Cancelled' if first else 'Faulted' if held else 'Completed';expected_etl='cancelled' if first else 'faulted' if held else 'succeeded'
    require(final['execution']['native']['instance']['status']==expected_native and final['execution']['run']['status']==expected_etl and result['status']==(5 if first else 4 if held else 3) and
        final['execution']['run']['acceptedRecords']==(2 if first or held else 4) and final['execution']['run']['rejectedRecords']==0,
        'The actual ETL/native terminal truth was reclassified or revived.')
    if first:
        cancellation,=fm['payload']['cancellations'];require(cancellation['settled'] and cancellation['origin']==origin and result['cancellationSources'],
            'The timer winner lacks original-worker downstream cancellation settlement.')
    if held:
        failure,=fm['payload']['failures'];old=checked['middle']['model']['payload'];incident,=final['incidents']['items']
        require(middle['execution']['model']['status']=='WaitingIncident' and old['state']==model['payload']['state'] and
            old['failures'][0]['status']=='Open' and failure['status']=='Retired' and failure['semanticState']==old['state'] and
            failure['resolutionInputId']==elapsed['inputId'] and incident['status']=='Retired' and fm['payload']['nativeResults']==old['nativeResults'] and
            'cancellations' not in fm['payload'], 'Timer interruption changed the original held semantic snapshot or failed physical result.')
        availability=read('snapshot-unavailability.json');content=q.digest(root/'lookup-source.csv')
        require(q.digest(root/'lookup-snapshot.csv')==content and availability['restoredBeforeTimerInterruption'] and all(availability[k]==content for k in ('sourceSha256','snapshotSha256Before','snapshotSha256WhileUnavailable','restoredSha256')) and
            v.instant(availability['restoredAtUtc'])<v.instant(elapsed['acceptedAt']), 'The actual held timer did not recover the same immutable external lookup before interruption.')
        refusal=read('retry-refusal.json');require(refusal['httpStatus']==409 and refusal['outcome']['result']['status']=='Rejected' and
            refusal['outcome']['result']['code']=='execution-terminal' and refusal['outcome']['result']['revision']==fm['revision'], 'A timer-retired failure reopened through retry.')
    early=read('before-wire.json');late=read('finished-wire.json')
    require(not early['overflow'] and not late['overflow'] and late['requests'][:len(early['requests'])]==early['requests'] and
        len([r for post in early['outputPosts'] for r in post['records']])==2 and
        (late==early if first or held else len([r for post in late['outputPosts'] for r in post['records']])==4),
        'The interrupted worker retried HTTP or lost actual prior output effects.')
    for phase,observed in timers.items():
        delivery,=[s for s in observed['sources'] if s['intent']['kind']=='Elsa.Bpmn.DurationTimer.v1']
        require(delivery['intent']['payload']==timer and delivery['intent']['notBefore']==timer['dueAt'] and delivery['outboxItemId']==timer['sourceOutboxItemId'],
            'The actual service timer source was replaced or lost its immutable deadline.')
        if phase=='before':require(v.instant(timer['dueAt'])>v.instant(p['cutAtUtc']) and ticks(timer['dueAt'])-ticks(delivery['intent']['recordedAt'])==p['durationSeconds']*10_000_000, 'The original service timer lost its accepted checkpoint deadline or elapsed before process loss.')
        if delivery['status']==2:require(v.instant(delivery['deliveredAt'])>=v.instant(elapsed['acceptedAt']), 'A timer source was ACKed before its exact elapsed receipt.')
        if phase in ('finished','retained'):require(all(s['status']==2 for s in observed['sources']) and not observed['queuedModelInputs'], 'The timed model still owes source or input delivery.')
    if embedded:
        early_parent=read('before-checked-embedding.json');late_parent=read('finished-checked-embedding.json')
        require(late_parent==read('retained-checked-embedding.json') and late_parent['model']==fm,'The native parent or its exact model changed after cold recovery.')
        encoded_origin=model['payload']['nativeParent']['invocation'];parent_origin=h.opaque(encoded_origin)
        run,=[a for a in late_parent['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
        original_run,=[a for a in early_parent['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
        pin={k:p['nativeParentPublication'][k] for k in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')}
        require(parent_origin['nativeExecutionId']==p['nativeParentExecutionId'] and parent_origin['nativeExecutable']==pin and
            late_parent['nativeParent']['pinnedExecutable']==pin and late_parent['nativeParent']['status']==3 and run['status']==4 and
            original_run['invocationId']==run['invocationId']==parent_origin['nativeInvocationId'] and run['completion']['outcomeKey']=='Done' and
            run['completion']['result']['inlineValue']=={'executionId':p['modelExecutionId'],'result':fm['terminalResult']} and not late_parent['bookmarks'] and
            any(d['status']==1 and d['registrationId']==parent_origin['registrationId'] and d['providerId']=='elsa.bpmn.native-parent.v1' for d in run['triggerDeliveries']),
            'The original RunBpmn wait lacks exact typed outcome consumption after the timer/worker winner.')
        successors=[a for a in late_parent['activities'] if a['execution']['executableNodeId']=='after']
        require(not successors if parent=='root' else len(successors)==1 and successors[0]['status']==4,'The timed native parent lost or duplicated its actual successor.')
        settled,=[r for r in timers['finished']['inputs'] if r['inputKind']=='native-parent-settled']
        returned,=[x for x in timers['finished']['sources'] if x['intent']['kind']=='Elsa.Bpmn.ResumeNativeParent.v1']
        require(settled['acceptedRevision']==fm['revision'] and settled['invocationId']==parent_origin['nativeInvocationId'] and settled['attemptId']==parent_origin['nativeAttemptId'] and
            returned['status']==2 and returned['intent']['payload']=={'FormatVersion':1,'Invocation':encoded_origin,'Result':fm['terminalResult']} and
            v.instant(returned['deliveredAt'])>=v.instant(settled['acceptedAt']),'The timed native parent return was ACKed without exact model settlement.')
    stores=0
    for phase in ('before','middle','finished'):
        require(set(p[phase+'Stores'])=={'elsa','etl-runtime','etl-design'},'The duration journey lacks a coupled whole store.')
        for store,snapshot in p[phase+'Stores'].items():
            file=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(file.read_bytes());require(q.digest(file)==snapshot['gzipSha256'] and hashlib.sha256(raw).hexdigest()==snapshot['databaseSha256'] and
                snapshot['observer']['readOnly'] and snapshot['observer']['snapshotSha256']==snapshot['databaseSha256'],'A service/duration snapshot differs from its provider-observed bytes.')
            with tempfile.TemporaryDirectory(prefix='elsa-service-duration-read-') as temp:
                db=Path(temp)/'db';db.write_bytes(raw)
                with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                    require(c.execute('PRAGMA integrity_check').fetchall()==[('ok',)],'A service duration whole store failed integrity verification.')
                    if store=='elsa':
                        view=timers[phase];row,=c.execute('SELECT Revision,Lifecycle,ContentJson FROM elsa_runtime_model_execution').fetchall();physical=json.loads(row[2])
                        require(row[:2]==(view['model']['revision'],view['model']['lifecycle']) and physical['payload']==view['model']['payload'] and
                            physical['terminalResult']==view['model']['terminalResult'] and physical['executable']==v.encoded_tree(view['model']['executable']), 'The physical duration model differs from its checked state.')
                        require(c.execute('SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state').fetchone()[0]==1+int(embedded),'A timed retry created a substitute/extra native worker.')
                        worker=checked[phase]['worker'];worker_row=c.execute('SELECT SchemaVersion,Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(v.key(p['workflowExecutionId']),)).fetchone();worker_body=json.loads(worker_row[2])
                        require(worker_row[0]=='2.0.0' and worker_row[1]==worker['status'] and worker_body['pinnedExecutable']==v.encoded_tree(origin['workerExecutable']) and
                            worker_body['modelWorkOrigin']==v.encoded_tree(origin),'The physical timed native worker lost its original complete pin or protected origin.')
                        activity_status,activity_json=c.execute('SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=?',(v.key(p['workflowExecutionId']),)).fetchone();activity=json.loads(activity_json)
                        require(activity_status==('Suspended' if phase=='before' else expected_native),'The physical RunEtl root differs from its actual terminal disposition.')
                        if phase!='before' and not first and not held:
                            require(activity['completion']['outcomeKey']==v.key('Done') and activity['completion']['result']['inlineValue']==result['completion']['result'],
                                'The timed native root lost its exact typed RunEtl success result.')
                        if phase!='before':
                            native_receipt=checked[phase]['resultInput'];native_source=checked[phase]['resultSource']
                            require(native_receipt is not None and native_source is not None,'The actual timed native result lacks its exact source/receipt.')
                            row=c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(v.key(native_source['outboxItemId']),)).fetchone();body=json.loads(row[1])
                            require(row[0]==native_source['status'] and body['intent']['payload']==native_source['intent']['payload'] and body['deliveredAt']==native_source['deliveredAt'],
                                'The physical original worker result source or ACK changed.')
                        materials=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable')]
                        bound,=[m['material'] for m in materials if m.get('material') and m['material']['identity']==v.encoded_tree(model['executable'])]
                        definition=bound['definition'];binding,=definition['serviceBindings']
                        require(definition['formatVersion']==4 and definition['timerBindings']==['timeout-work'] and definition['humanBindings']==[] and
                            binding['executable']==origin['workerExecutable'] and binding['faultPolicy']=={'formatVersion':1,'disposition':'HoldForRetry' if held else 'FinalFault','maxAttempts':2 if held else 1} and
                            len(bound['dependencies'])==1,'The physical timed model lost its complete explicit policy/timer/worker inventory.')
                        if embedded:
                            row=c.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(v.key(p['nativeParentExecutionId']),)).fetchone()
                            parent_view=read(phase+'-checked-embedding.json')['nativeParent']
                            require(row[0]==parent_view['status'] and json.loads(row[1])['pinnedExecutable']==v.encoded_tree(parent_view['pinnedExecutable']),
                                'The physical timed native parent lost its actual status or pin.')
                        rows={r[0]:json.loads(r[1]) for r in c.execute('SELECT InputId,ContentJson FROM elsa_runtime_model_input')}
                        require(len(rows)==len(view['inputs']),'The actual duration model accepted an extra or missing input.')
                        for receipt in view['inputs']:
                            r=rows[v.key(receipt['inputId'])];require(r['contentFingerprint']==v.key(receipt['contentFingerprint']) and r['acceptedRevision']==receipt['acceptedRevision'] and r['result']==receipt['result'], 'The physical service duration receipt changed its accepted outcome.')
                        for s in view['sources']:
                            row=c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(v.key(s['outboxItemId']),)).fetchone();r=json.loads(row[1])
                            require(row[0]==s['status'] and r['intent']['payload']==s['intent']['payload'] and r['intent']['notBefore']==s['intent']['notBefore'] and r['deliveredAt']==s['deliveredAt'], 'A physical duration source lost its payload, deadline or exact ACK.')
                    elif store=='etl-runtime':
                        row,=c.execute('SELECT InvocationJson,Status,AcceptedRecords,RejectedRecords FROM elsa_etl_runtime_runs').fetchall();inv=json.loads(row[0])
                        expected_status=1 if phase=='before' else 5 if first else 4 if held else 2
                        require(row[1]==expected_status and row[2]==(2 if phase=='before' or first or held else 4) and row[3]==0 and
                            v.decoded(inv['runId'])==p['runId'] and v.decoded(inv['resultAddress']['workflowExecutionId'])==p['workflowExecutionId'], 'The physical timed ETL invocation or exact native address changed.')
                        require(inv['artifact']['formatVersion']==2 and v.decoded(inv['artifact']['contentHash'])==p['etlPublication']['version']['contentHash'],
                            'The timed ETL run lost its actual immutable published artifact.')
                        terminals=c.execute('SELECT Outcome,ResultJson FROM elsa_etl_runtime_terminals').fetchall();outboxes=c.execute('SELECT Disposition,AcknowledgedAtUtcTicks FROM elsa_etl_runtime_outbox').fetchall()
                        if phase=='before':require(not terminals and not outboxes,'The original HTTP wait already carried a terminal ETL result.')
                        else:
                            require(len(terminals)==len(outboxes)==1 and terminals[0][0]==(3 if first else 2 if held else 0) and outboxes[0][0]==2 and outboxes[0][1] is not None,
                                'The actual timed ETL terminal lacks its original acknowledged native delivery.')
                            terminal=json.loads(terminals[0][1]);require(terminal['outcome']==expected_etl and (v.decoded(terminal['diagnostic']['code'])==('etl.snapshot-read-failed' if held else 'cancelled') if held or first else terminal['diagnostic'] is None),
                                'The physical timed ETL result was relabelled or lost its genuine technical fault.')
            stores+=1
    return {'parent':parent,'order':order,'wholeStoresVerified':stores,'actualOriginalWorkerPreserved':True}

def verify(path):
    root=path.resolve().parent;m=json.loads(path.read_text());source=m['sourceRevision'];cases=m['cases'];expected={(parent,order) for parent in ('direct','root','sequence','flowchart','compensationscope') for order in ('worker-first','timer-first','held-interruption')}
    require(m['schemaVersion']==1 and m['kind']=='actual-service-duration-matrix' and re.fullmatch('[a-f0-9]{40}',source) and len(cases)==15 and {(c['parent'],c['order']) for c in cases}==expected,
        'The service/duration matrix lacks all real direct/native parent orders.')
    results=[];products=None;traces={}
    names={'process.json','before.json','middle.json','middle-after-restart.json','finished.json','retained.json','recovered.json'}|{phase+'-checked-'+kind+'.json' for phase in ('before','middle','finished','retained') for kind in ('model','timers')}|{phase+'-wire.json' for phase in ('before','middle','finished','retained')}|{phase+'-'+store+'.sqlite.gz' for phase in ('before','middle','finished') for store in ('elsa','etl-runtime','etl-design')}
    for case in cases:
        directory=case['name'];expected_names=names|({'lookup-source.csv','lookup-snapshot.csv','snapshot-unavailability.json','retry-refusal.json'} if case['order']=='held-interruption' else set())|({phase+'-checked-embedding.json' for phase in ('before','middle','finished','retained')} if case['parent']!='direct' else set())
        require(len(case['assets'])==len(expected_names) and {a['path'] for a in case['assets']}=={directory+'/'+name for name in expected_names},'The service duration case lacks its exact raw assets.')
        for a in case['assets']:require(q.digest(q.evidence_file(root,a['path']))==a['sha256'],'An actual service duration asset differs.')
        results.append(verify_case(root/directory,case['parent'],case['order'],source));p=json.loads((root/directory/'process.json').read_text())['products']
        if products is None:products=p
        require(p==products,'Service duration cases use different executing products.')
        process=json.loads((root/directory/'process.json').read_text())
        original_worker=json.loads((root/directory/'before-checked-model.json').read_text())['worker']['modelWorkOrigin']
        stopped_handles={process['timer']['invocationId']:'<duration-work-invocation>',original_worker['invocationId']:'<native-work-invocation>'}
        def normalize(value):
            if isinstance(value,dict):return {k:'<model-execution>' if k=='producingWorkHandle' and x==process['modelExecutionId'] else
                stopped_handles[x] if k=='stoppedHandle' and isinstance(x,str) and x in stopped_handles else normalize(x) for k,x in value.items()}
            if isinstance(value,list):return [normalize(x) for x in value]
            return value
        for phase in ('before','middle','finished'):
            state=normalize(json.loads((root/directory/(phase+'-checked-timers.json')).read_text())['model']['payload']['state']);key=(case['order'],phase)
            require(key not in traces or traces[key]==state,'Direct/native timed service interpreter histories differ.')
            traces[key]=state
    return {'sourceRevision':source,'actualServiceDurationJourneys':results,'wholeStoresVerified':sum(r['wholeStoresVerified'] for r in results),'actualBothWorkerTimerOrdersProved':True,
        'actualHeldFailureTimerInterruptionProved':True,'simultaneousCommitOrdersProved':False,'finalUpgradeAndStudioProved':False}

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--manifest',type=Path,required=True);print(json.dumps(verify(parser.parse_args().manifest),indent=2))
