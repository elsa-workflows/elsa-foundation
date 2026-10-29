#!/usr/bin/env python3
"""Read-only verification of actual cancellation before first native start delivery."""
import argparse,base64,gzip,hashlib,importlib.util,json,sqlite3,tempfile,struct
from pathlib import Path
from contextlib import closing
_spec=importlib.util.spec_from_file_location('etl_verified',Path(__file__).with_name('verify_http_recovery.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
q=v.q


def identity(*parts):
    values=[p.encode("utf-16le") for p in parts]
    return hashlib.sha256(b"".join(struct.pack("<i",len(p)//2)+p for p in values)).hexdigest()


def verify(path):
    root=path.parent;m=json.loads(path.read_text());source=m['sourceRevision']
    if m.get('schemaVersion')!=1 or m.get('kind')!='actual-bpmn-cancellation-before-native-admission' or len(source)!=40:
        raise ValueError('Unknown actual native start retirement evidence.')
    names={'process.json','cancel-response.json'}|{phase+'-'+store+'.sqlite.gz' for phase in ('before','pending','finished') for store in ('elsa','etl-runtime','etl-design')}
    names|={phase+'.json' for phase in ('before','pending','finished','retained')}|{phase+'-checked.json' for phase in ('before','pending','finished','retained')}|{phase+'-wire.json' for phase in ('before','finished','retained')}
    if len(m['assets'])!=len(names) or {a['path'] for a in m['assets']}!=names:raise ValueError('The retirement bundle lacks its complete actual observations.')
    for a in m['assets']:
        if q.digest(q.evidence_file(root,a['path']))!=a['sha256']:raise ValueError('An actual start retirement asset differs.')
    def read(name):return json.loads(q.evidence_file(root,name).read_text())
    process=read('process.json');children=process['processes']
    if (process['sourceRevision']!=source or process['products']['sourceCommit']!=source or process['predecessorExitCode']!=-9 or
            process['sweepIntervalsSeconds']!=[300,.2,.2] or [x['role'] for x in children]!=['http-fixture','workbench','workbench','workbench'] or
            len({x['processId'] for x in children})!=4 or len({x['controllerProcessId'] for x in children})!=1):
        raise ValueError('The actual retirement lacks exact source and process-loss/restart provenance.')
    checked={p:read(p+'-checked.json') for p in ('before','pending','finished','retained')};views={p:read(p+'.json') for p in checked}
    origin=process['origin'];model_pin=process['modelPublication']['executable'];worker_pin={k:process['publication'][k] for k in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')}
    if checked['finished']!=checked['retained'] or views['finished']!=views['retained']:raise ValueError('The retired start changed after another actual host.')
    interpreter=checked['before']['model']['payload']['state'];material=checked['before']['workerMaterial']
    for phase,c in checked.items():
        model=c['model'];view=views[phase];revision={'before':1,'pending':2,'finished':3,'retained':3}[phase]
        if (c['readOnly'] is not True or c['origin']!=origin or c['worker'] is not None or c['queuedNativeWork'] or c['workerMaterial']!=material or
                material['identity']!=worker_pin or origin['workerExecutable']!=worker_pin or origin['executable']!=model_pin or
                origin['executionId']!=process['modelExecutionId'] or model['executionId']!=origin['executionId'] or model['executable']!=model_pin or
                model['materialIncarnationId']!=process['modelPublication']['incarnationId'] or origin['materialIncarnationId']!=model['materialIncarnationId'] or
                model['scope']!=origin['scope'] or model['revision']!=revision or model['payload']['state']!=interpreter or
                view['native'] or view['runs']['items'] or view['runs']['nextCursor'] is not None or view['model']['revision']!=revision):
            raise ValueError('Native start retirement lost its exact pin/invocation or admitted a native/ETL execution.')
        if phase=='before':
            if model['lifecycle']!=0 or model['terminalResult'] is not None or [x['nativeWork'] for x in model['payload']['work']]!=[origin] or len(c['inputs'])!=1 or len(c['sources'])!=1:
                raise ValueError('The original real native start was not owed before cancellation.')
        else:
            cancellation,=model['payload']['cancellations'];settled=phase in ('finished','retained')
            if (model['lifecycle']!=(3 if settled else 2) or model['payload']['formatVersion']!=4 or model['payload']['work'] or model['payload'].get('nativeResults') or
                    model['terminalResult']!={'status':'Cancelled','reason':'Retire the original native start before admission.'} or
                    cancellation['origin']!=origin or cancellation['settled'] is not settled or cancellation.get('notAdmitted',False) is not settled):
                raise ValueError('The model lost its pending decision or fabricated an admitted-worker result.')
            if len(c['sources'])!=2 or len(c['inputs'])!=(3 if settled else 2):raise ValueError('The exact cancellation/start obligations or receipts are absent.')
        sources={s['intent']['kind']:s for s in c['sources']};start=sources['Elsa.Bpmn.StartNativeWork.v1']
        if start['outboxItemId']!=origin['sourceOutboxItemId'] or start['intent']['payload']!=origin or start['intent']['intentId']!=origin['sourceIntentId']:
            raise ValueError('The retired native start differs from its actual committed origin.')
        if phase in ('before','pending'):
            if any(s['status'] in (2,4) for s in c['sources']):raise ValueError('An owed start/cancel was acknowledged before positive retirement.')
        else:
            if any(s['status']!=2 for s in c['sources']) or c['queuedModelInputs']:raise ValueError('Retired start/cancel responsibility remains owed.')
            receipt,=[r for r in c['inputs'] if r['inputKind']=='native-start-retired']
            cancel=sources['Elsa.Bpmn.CancelNativeWork.v1'];cancellation=model['payload']['cancellations'][0]
            if (receipt['inputId']!='native-start-retired:'+identity(origin['sourceOutboxItemId'],cancellation['sourceOutboxItemId']) or
                    receipt['invocationId']!=origin['invocationId'] or receipt['attemptId']!=origin['attemptId'] or receipt['acceptedRevision']!=3 or
                    receipt['result']['status']!='Accepted' or receipt['result']['lifecycle']!='TerminalSettled' or
                    v.instant(start['deliveredAt'])<v.instant(receipt['acceptedAt']) or v.instant(cancel['deliveredAt'])<v.instant(start['deliveredAt']) or
                    cancel['outboxItemId']!=cancellation['sourceOutboxItemId'] or cancel['intent']['payload']!={k:z for k,z in {**cancellation,'settled':False}.items() if k!='notAdmitted'}):
                raise ValueError('Start/cancel acknowledgment lacks the exact positive retirement consumption receipt.')
    response=read('cancel-response.json');pending_cancel,=[r for r in checked['pending']['inputs'] if r['inputKind']=='cancel']
    if response['pending'] or response['result']!=pending_cancel['result'] or pending_cancel['result']['lifecycle']!='TerminalPendingSettlement' or pending_cancel['acceptedRevision']!=2:
        raise ValueError('The accepted cancellation was relabeled as already settled.')
    wires=[read(p+'-wire.json') for p in ('before','finished','retained')]
    if any(w!=wires[0] for w in wires) or wires[0]['overflow'] or wires[0]['requests'] or wires[0]['outputPosts']:
        raise ValueError('The unadmitted native worker produced an actual HTTP effect.')
    materials=None
    for phase,stores in ((p,process[p+'Stores']) for p in ('before','pending','finished')):
        for store,snapshot in stores.items():
            path=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(path.read_bytes())
            if q.digest(path)!=snapshot['gzipSha256'] or hashlib.sha256(raw).hexdigest()!=snapshot['databaseSha256'] or snapshot['observer']['snapshotSha256']!=snapshot['databaseSha256'] or snapshot['observer']['readOnly'] is not True:
                raise ValueError('An actual provider-observed retirement store differs.')
            with tempfile.TemporaryDirectory(prefix='elsa-retirement-read-') as temporary:
                db=Path(temporary)/'s.sqlite';db.write_bytes(raw)
                with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                    if c.execute('PRAGMA integrity_check').fetchall()!=[('ok',)]:raise ValueError('A retirement store failed integrity.')
                    if store=='etl-runtime':
                        if c.execute('SELECT COUNT(*) FROM elsa_etl_runtime_runs').fetchone()[0]:raise ValueError('The retired native start admitted a physical ETL run.')
                    if store!='elsa':continue
                    for table in ('elsa_runtime_workflow_execution_state','elsa_runtime_activity_execution_state','elsa_runtime_bookmark_state'):
                        if c.execute('SELECT COUNT(*) FROM '+table).fetchone()[0]:raise ValueError('The retired native start created a physical carrier or work.')
                    current=checked[phase]['model'];row,=c.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchall();stored=json.loads(row[0])
                    if stored['payload']!=current['payload'] or stored['terminalResult']!=current['terminalResult'] or stored['revision']!=current['revision'] or stored['executable']!=v.encoded_tree(model_pin):raise ValueError('The checked model differs from its actual physical retirement decision.')
                    rows=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_model_input')]
                    if len(rows)!=len(checked[phase]['inputs']):raise ValueError('The actual model receipt count differs.')
                    for receipt in checked[phase]['inputs']:
                        physical,=[r for r in rows if r['inputId']==v.key(receipt['inputId'])]
                        if physical['result']!=receipt['result'] or physical['contentFingerprint']!=v.key(receipt['contentFingerprint']) or physical['acceptedRevision']!=receipt['acceptedRevision'] or physical['inputKind']!=v.key(receipt['inputKind']):raise ValueError('The physical retirement receipt differs from its checked consumption proof.')
                    rows=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox')]
                    if len(rows)!=len(checked[phase]['sources']):raise ValueError('The actual retired delivery source count differs.')
                    for observed in checked[phase]['sources']:
                        physical,=[r for r in rows if r['outboxItemId']==v.key(observed['outboxItemId'])]
                        if physical['intent']['payload']!=observed['intent']['payload'] or physical['deliveredAt']!=observed['deliveredAt'] or physical['status']!=('Delivered' if phase=='finished' else 'Pending' if observed['status']==0 else 'Delivering' if observed['status']==1 else 'FailedRetryable'):raise ValueError('The physical retired source differs from its retained acknowledgment.')
                    value=c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable ORDER BY ContentJson').fetchall()
                    if materials is None:materials=value
                    if value!=materials:raise ValueError('Native/model material changed while retiring the original start.')
    return {'sourceRevision':source,'originalUndeliveredStartRetirementProved':True,'wholeStoresVerified':9,'actualWorkbenchProcesses':3,'nativeWorkflowsAdmitted':0,'etlRunsAdmitted':0,'queuedNativeStartRetirementProved':False,'admissionRaceProved':False,'runBpmnEtlIntegrationProved':False,'modelHeldFaultRetryProved':False}


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--manifest',required=True,type=Path)
    print(json.dumps(verify(p.parse_args().manifest.resolve(strict=True)),indent=2))
