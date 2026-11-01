#!/usr/bin/env python3
"""Verify the actual runtime-only duration/human race captures and whole stores read-only."""
import argparse,base64,gzip,hashlib,importlib.util,json,re,sqlite3,struct,tempfile
from contextlib import closing
from datetime import datetime,timedelta,timezone
from pathlib import Path
_spec=importlib.util.spec_from_file_location('duration_hosting',Path(__file__).with_name('qualify-hosting.py'))
q=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(q)
KIND='Elsa.Bpmn.DurationTimer.v1'

def require(value,message):
    if not value:raise ValueError(message)
def key(value):return base64.b64encode(value.encode('utf-16le','surrogatepass')).decode()
def encoded(value):
    if isinstance(value,str):return key(value)
    if isinstance(value,list):return [encoded(x) for x in value]
    if isinstance(value,dict):return {k:encoded(x) for k,x in value.items()}
    return value
def identity(*parts):
    raw=[p.encode('utf-16le','surrogatepass') for p in parts]
    return hashlib.sha256(b''.join(struct.pack('<i',len(p)//2)+p for p in raw)).hexdigest()
def instant(value):
    # Runtime emits up to seven fractional digits. Compare in 100ns ticks without float conversion.
    m=re.fullmatch(r'(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)',value)
    require(m is not None,'A duration timestamp is not exact UTC.')
    t=datetime.strptime(m[1],'%Y-%m-%dT%H:%M:%S');seconds=(t-datetime(1,1,1)).days*86400+(t-datetime(1,1,1)).seconds
    return seconds*10_000_000+int((m[2] or '').ljust(7,'0'))

def verify_case(root,name,source):
    read=lambda file:json.loads((root/file).read_text());p=read('process.json');before=read('before-checked.json');recovered=read('recovered-checked.json')
    phases={phase:read(phase+'-checked.json') for phase in ('before','decided','finished','retained')}
    bm=before['model'];fm=phases['finished']['model'];timer,=[w['timer'] for w in bm['payload']['work'] if w.get('timer')];scope=bm['scope']['value'];execution=bm['executionId']
    require(p['schemaVersion']==1 and p['kind']=='actual-runtime-only-human-duration-race' and p['sourceRevision']==source and p['order']==name and p['durationSeconds']==30 and
        p['noDesignStorage'] and p['stateSeeded'] is False and p['clockEdited'] is False and len(p['processes'])==4 and p['exits']==[137]*4 and
        len({c['processId'] for c in p['processes']})==4 and len({c['controllerProcessId'] for c in p['processes']})==1 and all(c['startedAtUtc'] for c in p['processes']),
        'The actual duration journey lost its runtime-only source or four real process-loss exits.')
    require(len(p['binaries'])==9 and len({b['file'] for b in p['binaries']})==9 and all(re.fullmatch('[a-f0-9]{64}',b['sha256']) and
        (b['productVersion'].endswith('+'+source) if b['file'].startswith('Elsa.') else b['productVersion'].startswith('0.5.0')) for b in p['binaries']) and
        p['observer']['productVersion'].endswith('+'+source) and re.fullmatch('[a-f0-9]{64}',p['observer']['sha256']),
        'The duration host or checked observer differs from its executing source.')
    require(before['readOnly'] and recovered['model']==bm and all(x['readOnly'] and x['nativeWorkflowRows']==x['nativeActivityRows']==x['nativeBookmarkRows']==0 for x in phases.values()) and
        bm['revision']==1 and bm['lifecycle']==0 and bm['terminalResult'] is None and bm['payload']['formatVersion']==11 and bm['payload']['failures']==[] and
        fm['revision']==2 and fm['lifecycle']==3 and fm['terminalResult']=={'status':'Completed','outcome':'Done'} and fm['payload']['work']==[] and
        phases['decided']['model']==fm and phases['retained']==phases['finished'] and all(m['model']['executable']==p['published']['executable'] and
            m['model']['materialIncarnationId']==p['published']['incarnationId'] for m in phases.values()),
        'The actual duration decision, native-free state or accepted winner changed through recovery.')
    intent='bpmn-duration:'+identity(scope,execution,timer['invocationId'],timer['attemptId']);input_id='timer-elapsed:'+identity(scope,execution,timer['invocationId'],timer['attemptId'])
    source_id='model-input:'+identity(scope,execution,'start-duration')+':'+intent
    require(timer['formatVersion']==1 and timer['scope']==bm['scope'] and timer['executionId']==execution and timer['executable']==bm['executable'] and
        timer['materialIncarnationId']==bm['materialIncarnationId'] and timer['bindingId']=='timeout-work' and timer['sourceOutboxItemId']==source_id and
        timer['attemptId']=='attempt:'+identity(timer['invocationId'],'1') and p['dueAt']==timer['dueAt'], 'The original duration listener lost its deterministic address or full material pin.')
    original,=before['sources'];require(original['status']==0 and original['deliveryAttemptCount']==0 and original['availableAt']==timer['dueAt'] and
        original['outboxItemId']==source_id and original['intent']['kind']==KIND and original['intent']['intentId']==intent and original['intent']['payload']==timer and
        original['intent']['notBefore']==timer['dueAt'] and instant(timer['dueAt'])-instant(original['intent']['recordedAt'])==30*10_000_000,
        'The actual timer lost its committed thirty-second deadline or original delayed source.')
    expected_end='timed-out' if name=='timer-first' else 'done';other='done' if name=='timer-first' else 'timed-out'
    require(any(d['elementId']==expected_end for d in fm['payload']['state']['diagnostics']) and not any(d['elementId']==other for d in fm['payload']['state']['diagnostics']),
        'The model did not retain the actual first winner route.')
    final_inputs=phases['finished']['inputs'];require(len(final_inputs)==3 and {r['inputKind'] for r in final_inputs}=={'start','complete-human','timer-elapsed'},'The duration journey accepted an extra input or omitted late delivery.')
    elapsed,=[r for r in final_inputs if r['inputKind']=='timer-elapsed'];human,=[r for r in final_inputs if r['inputKind']=='complete-human']
    require(elapsed['inputId']==input_id and elapsed['invocationId']==timer['invocationId'] and elapsed['attemptId']==timer['attemptId'] and
        elapsed['acceptedRevision']==human['acceptedRevision']==2 and instant(elapsed['acceptedAt'])>=instant(timer['dueAt']) and
        elapsed['result']['status']==('Accepted' if name=='timer-first' else 'Rejected') and human['result']['status']==('Rejected' if name=='timer-first' else 'Accepted') and
        (elapsed if name=='human-first' else human)['result']['code']=='execution-terminal' and
        (instant(human['acceptedAt'])<instant(timer['dueAt']) if name=='human-first' else instant(human['acceptedAt'])>=instant(elapsed['acceptedAt'])),
        'The duration eligibility, first accepted receipt or late rejection differs from physical ordering.')
    for phase,observed in phases.items():
        delivery,=observed['sources'];require(delivery['intent']==original['intent'] and delivery['outboxItemId']==source_id and delivery['stimulusDeliverySourceScope']==bm['scope'], 'The original timer source was replaced across recovery.')
        if delivery['status']==2:require(instant(delivery['deliveredAt'])>=instant(elapsed['acceptedAt']), 'The timer source was acknowledged before its exact model receipt.')
        if phase in ('finished','retained'):require(delivery['status']==2 and observed['queuedModelInputs']==[], 'The actual elapsed timer still owes consumption or acknowledgment.')
        if phase!='before':require(read(phase+'-api.json')==read('decided-api.json'),'The public winner view changed after a late input or cold host.')
    require(p['human']['invocationId']==human['invocationId'] and p['human']['attemptId']==human['attemptId'] and
        (p['replay']['result']['status']=='Rejected' if name=='timer-first' else p['replay']['result']==p['humanResponse']['result']), 'The original human request lost its exact accepted/rejected replay.')
    require(len(p['snapshots'])==4 and {s['file'] for s in p['snapshots']}=={phase+'-runtime.sqlite.gz' for phase in phases}, 'The duration journey lacks every whole-store boundary.')
    for phase,observed in phases.items():
        snapshot=next(s for s in p['snapshots'] if s['file']==phase+'-runtime.sqlite.gz');file=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(file.read_bytes())
        require(q.digest(file)==snapshot['gzipSha256'] and hashlib.sha256(raw).hexdigest()==snapshot['databaseSha256'],'A duration whole store differs from its captured bytes.')
        with tempfile.TemporaryDirectory(prefix='elsa-duration-read-') as temporary:
            db=Path(temporary)/'snapshot.sqlite';db.write_bytes(raw)
            with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                require(c.execute('PRAGMA integrity_check').fetchall()==[('ok',)],'A duration whole store failed integrity verification.')
                for table in ('elsa_runtime_workflow_execution_state','elsa_runtime_activity_execution_state','elsa_runtime_bookmark_state'):
                    require(c.execute('SELECT COUNT(*) FROM '+table).fetchone()[0]==0,'The actual direct timer used a native workflow/activity/bookmark carrier.')
                row,=c.execute('SELECT Revision,Lifecycle,ContentJson FROM elsa_runtime_model_execution').fetchall();model=json.loads(row[2]);view=observed['model']
                require(row[:2]==(view['revision'],view['lifecycle']) and model['payload']==view['payload'] and model['terminalResult']==view['terminalResult'] and
                    model['executable']==encoded(view['executable']) and model['materialIncarnationId']==key(view['materialIncarnationId']), 'The physical duration model differs from its checked view.')
                rows={r[0]:json.loads(r[1]) for r in c.execute('SELECT InputId,ContentJson FROM elsa_runtime_model_input')}
                require(len(rows)==len(observed['inputs']), 'The physical duration input inventory differs from its exact receipts.')
                for receipt in observed['inputs']:
                    stored=rows[key(receipt['inputId'])]
                    require(stored['contentFingerprint']==key(receipt['contentFingerprint']) and stored['result']==receipt['result'] and stored['acceptedRevision']==receipt['acceptedRevision'] and
                        stored['acceptedAt']==receipt['acceptedAt'] and stored['invocationId']==(None if receipt['invocationId'] is None else key(receipt['invocationId'])) and
                        stored['attemptId']==(None if receipt['attemptId'] is None else key(receipt['attemptId'])), 'A physical duration receipt changed its accepted content or exact attempt.')
                status,content=c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(key(source_id),)).fetchone();stored=json.loads(content);delivery,=observed['sources']
                require(status==delivery['status'] and stored['intent']['payload']==timer and stored['intent']['notBefore']==timer['dueAt'] and
                    stored['deliveredAt']==delivery['deliveredAt'], 'The physical timer source differs from its retained deadline and acknowledgment.')
                material,=c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable').fetchall();material=json.loads(material[0])['material']
                require(material['identity']==encoded(bm['executable']) and material['profile']==key('foundation-native-work-v1') and
                    material['definition']['formatVersion']==4 and material['definition']['timerBindings']==['timeout-work'] and material['definition']['humanBindings']==['review-work'] and
                    not material['dependencies'], 'The physical duration material lost its explicit bound profile or acquired a worker substitute.')
    return {'order':name,'wholeStoresVerified':4,'actualWorkbenchProcesses':4,'lateInputDoesNotAdvance':True,'timerAcknowledgedFromExactReceipt':True}

def verify(path):
    manifest=json.loads(path.read_text());root=path.resolve().parent;source=manifest['sourceRevision'];cases=manifest['cases']
    require(manifest['schemaVersion']==1 and manifest['kind']=='actual-runtime-only-human-duration-matrix' and re.fullmatch('[a-f0-9]{40}',source) and
        len(cases)==2 and {c['name'] for c in cases}=={'human-first','timer-first'},'The actual duration matrix lacks both unique first-winner journeys.')
    names={'process.json','recovered-checked.json'}|{phase+'-checked.json' for phase in ('before','decided','finished','retained')}|{phase+'-api.json' for phase in ('before','decided','finished','retained')}|{phase+'-runtime.sqlite.gz' for phase in ('before','decided','finished','retained')}
    results=[];binaries=None
    for case in cases:
        require(len(case['assets'])==len(names) and {a['path'] for a in case['assets']}=={case['name']+'/'+name for name in names},'An actual duration case lacks its exact raw assets.')
        for asset in case['assets']:require(q.digest(q.evidence_file(root,asset['path']))==asset['sha256'],'An actual duration asset differs from its manifest.')
        results.append(verify_case(root/case['name'],case['name'],source));products=json.loads((root/case['name']/'process.json').read_text())['binaries']
        if binaries is None:binaries=products
        require(products==binaries,'The actual duration cases used different products.')
    return {'sourceRevision':source,'actualHumanDurationJourneys':results,'wholeStoresVerified':8,'actualWorkbenchProcesses':8,'noNativeCarrierOrDesignStorage':True,
        'actualBothHumanTimerOrdersProved':True,'actualWorkerTimerOrdersProved':False,'heldServiceTimerInterruptionProved':False,'simultaneousCommitOrdersProved':False,'finalUpgradeAndStudioProved':False}

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--manifest',type=Path,required=True);print(json.dumps(verify(parser.parse_args().manifest),indent=2))
