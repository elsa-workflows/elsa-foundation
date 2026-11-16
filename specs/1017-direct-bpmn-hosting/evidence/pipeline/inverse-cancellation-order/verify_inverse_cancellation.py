"""Verify actual result-first cancellation snapshots; imports the existing physical bridge checks."""
import argparse,gzip,hashlib,importlib.util,json,sqlite3,tempfile
from contextlib import closing
from pathlib import Path


def common_verifier():
    here=Path(__file__).resolve()
    candidate=here.with_name('verify_http_recovery.py')
    if not candidate.exists():candidate=Path.cwd()/'e2e-tests/etl/verify_http_recovery.py'
    spec=importlib.util.spec_from_file_location('etl_inverse_common',candidate);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module


def require(value,message):
    if not value:raise ValueError(message)


def verify_case(root):
    v=common_verifier()
    manifest=json.loads((root/'manifest.json').read_text())
    require(manifest['kind']=='actual-bpmn-etl-inverse-cancel-case','Unknown inverse cancellation evidence')
    assets={a['path']:a for a in manifest['assets']}
    require(len(assets)==len(manifest['assets']),'Duplicate evidence asset path')
    for name,asset in assets.items():require(v.q.digest(v.q.evidence_file(root,name))==asset['sha256'],'Evidence asset hash mismatch: '+name)
    def read(name):
        require(name in assets,'Unmanifested inverse cancellation receipt: '+name)
        return json.loads(v.q.evidence_file(root,name).read_text())
    process=read('process.json');source=process['sourceRevision'];embedded=process.get('embeddedParent')is not None
    require(manifest['source']==source and len(source)==40 and all(c in '0123456789abcdef'for c in source),'Manifest/source mismatch')
    require(process['nativeResultBeforeEtlCancel']is True and process['nativeCancel']is True and process['predecessorExitCode']==-9 and process['pendingPredecessorExitCode']==-9,'Actual inverse ordering or two process losses absent')
    require(process['products']['sourceCommit']==source and all(source in p['productVersion']for p in process['products']['actualProductVersions']),'Source mismatch')
    before,finished,retained=[read(p+'.json')for p in ('before','finished','retained')]
    bridge=v.model_bridge(read,process,before,finished,False,cancelled=True,embedded=embedded);checked,origin,result=bridge
    pending=read('pending-checked.json');deferred=read('deferred-checked.json');sdk=read('sdk-inverse-cancel.json')
    require(sdk['disposition']=='Deferred'and sdk['prematureDrain']=={'code':'bpmn.native-work-unavailable','safeSummary':'Native downstream cancellation remains owed.'},'Expected dependency refusal absent')
    require(all(source in p['informationalVersion']for p in sdk['products']),'SDK source mismatch')
    require(pending['model']==deferred['model']==sdk['before']==sdk['deferredModel']==checked['before']['model'],'Model changed before ETL cancellation')
    require(pending['model']['revision']==1 and pending['model']['lifecycle']==0 and not pending['model']['payload'].get('cancellations'),'Cancellation was model-origin or prematurely terminal')
    require(not pending['queuedModelInputs']and len(deferred['queuedModelInputs'])==1 and deferred['queuedModelInputs']==sdk['deferredQueue']and sdk['prematureReceipt']is None,'Original input lost, replaced or prematurely consumed')
    command=deferred['queuedModelInputs'][0]['payload']
    require(command['inputId']==origin['resultInputId']and command['inputKind']=='native-result'and command['payload']==result and not any(x['inputKind']=='native-result'for x in deferred['inputs']),'Queued result differs or has a premature receipt')
    sources={s['outboxItemId']:s for s in deferred['sources']};result_source=sources[result['sourceOutboxItemId']]
    require(len(result['cancellationSources'])==1,'Exact downstream cancellation inventory lost')
    cancel=sources[result['cancellationSources'][0]]
    require(cancel==sdk['pendingEtlCancel']and cancel['status']==0 and cancel['intent']['kind']=='Elsa.Etl.Cancel.v1'and result_source==sdk['prematureSource']and result_source['status']==3 and result_source['deliveredAt']is None,'Original pending cancellation/result source changed or acknowledged early')
    require(result_source['intent']['payload']==result and result_source['intent']['workflowExecutionId']==origin['workerExecutionId']and cancel['intent']['workflowExecutionId']==origin['workerExecutionId'],'Wrong physical result/cancel owner')
    prefix=lambda item:item['outboxItemId'][:-(len(item['intent']['intentId'])+1)]
    require(prefix(cancel)==prefix(result_source),'Result/cancellation came from different terminal checkpoints')
    require(all(process['deferredStores'][n]['databaseSha256']==process['pendingStores'][n]['databaseSha256']for n in ('etl-runtime','etl-design')),'Coupled ETL stores changed during refused result')
    require(read('before-wire.json')==read('pending-wire.json')==read('finished-wire.json')==read('retained-wire.json'),'Physical HTTP work resumed after cancellation')
    require(finished==retained and finished['run']['status']=='cancelled'and finished['run']['acceptedRecords']==2 and finished['native']['instance']['status']=='Cancelled','Cold terminal state changed or false ETL outcome')
    retry=next(i['http']['fetch']for i in before['partitions']['items']if i['http']['fetch']and i['http']['fetch']['lastStatusCode']==429)
    require(v.instant(process['observedCancelledAfterRetryDeadlineAtUtc'])>v.instant(retry['notBefore']),'No actual post-deadline observation')
    stores=0
    for phase in ('before','pending','deferred','finished'):
        for name,snapshot in process[phase+'Stores'].items():
            require(snapshot['file']in assets,'Unmanifested whole store')
            data=v.q.evidence_file(root,snapshot['file']).read_bytes();raw=gzip.decompress(data)
            require(hashlib.sha256(data).hexdigest()==snapshot['gzipSha256']and hashlib.sha256(raw).hexdigest()==snapshot['databaseSha256']==snapshot['observer']['snapshotSha256']and snapshot['observer']['readOnly']is True,'Whole-store receipt mismatch')
            with tempfile.TemporaryDirectory(prefix='inverse-cancel-read-')as temporary:
                path=Path(temporary)/'store.sqlite';path.write_bytes(raw)
                with closing(sqlite3.connect(path.as_uri()+'?mode=ro&immutable=1',uri=True))as db:
                    require(db.execute('PRAGMA integrity_check').fetchall()==[('ok',)],'Physical store integrity failure')
                    if name=='elsa':
                        if phase in ('before','finished'):v.physical_model_bridge(db,phase,checked,origin,result,embedded=embedded)
                        else:
                            observation=pending if phase=='pending'else deferred
                            row,=db.execute('SELECT ContentJson FROM elsa_runtime_model_execution').fetchall();actual=json.loads(row[0]);require(actual['revision']==1 and actual['terminalResult']is None and actual['payload']==observation['model']['payload'],'Physical premature model settlement')
                            receipts=[json.loads(r[0])for r in db.execute('SELECT ContentJson FROM elsa_runtime_model_input')];require(len(receipts)==1 and receipts[0]['inputKind']==v.key('start-embedded'if embedded else'start'),'Physical premature result receipt')
                            if phase=='deferred':
                                rows=[json.loads(r[0])for r in db.execute('SELECT ContentJson FROM elsa_runtime_scheduler_work_item')]
                                require(len(rows)==1 and rows[0]['payload']==deferred['queuedModelInputs'][0]['payload'],'Physical original queued result lost')
                                for item,status in ((cancel,'Pending'),(result_source,'FailedRetryable')):
                                    row=db.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(v.key(item['outboxItemId']),)).fetchone();require(row is not None and json.loads(row[0])['status']==status and json.loads(row[0])['intent']['payload']==item['intent']['payload'],'Physical source differs from ordering proof')
                    elif name=='etl-runtime':
                        row,=db.execute('SELECT Status,AcceptedRecords,RejectedRecords,OutstandingRecords,OutstandingBytes FROM elsa_etl_runtime_runs').fetchall();terminal=db.execute('SELECT Outcome FROM elsa_etl_runtime_terminals').fetchall()
                        invocation,=db.execute('SELECT InvocationJson FROM elsa_etl_runtime_runs').fetchall();invocation=json.loads(invocation[0])
                        require(invocation==cancel['intent']['payload']and v.decoded(invocation['runId'])==process['runId']and v.decoded(invocation['resultAddress']['workflowExecutionId'])==origin['workerExecutionId'],'Physical ETL invocation differs from exact native cancellation')
                        require(row==((5,2,0,0,0)if phase=='finished'else(1,2,0,0,0))and terminal==([(3,)]if phase=='finished'else[]),'Physical ETL terminal contradicts cancellation ordering')
            stores+=1
    if embedded:
        first,last,cold=[read(p+'-checked-embedding.json')for p in ('before','finished','retained')]
        require(last==cold and last['model']==checked['finished']['model']and last['nativeParent']['status']==5 and not last['bookmarks']and not last['queuedModelInputs'],'Native parent did not durably settle')
        activity,=[a for a in last['activities']if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
        original,=[a for a in first['activities']if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn']
        registration,=first['bookmarks']
        require(activity['invocationId']==original['invocationId']and activity['status']==6 and any(t['status']==1 and t['providerId']=='elsa.bpmn.native-parent.v1'and t['registrationId']==registration['bookmarkId']and t['payloadType']['alias']=='Elsa.Activities.Bpmn.Models.RunBpmnCompletion'for t in activity['triggerDeliveries'])and not any(a['execution']['executableNodeId']=='after'for a in last['activities']),'Native cancellation consumption/continuation changed')
        settlement,=[i for i in last['inputs']if i['inputKind']=='native-parent-settled'];require(settlement['acceptedRevision']==3 and settlement['result']['status']=='Accepted','Native parent settlement receipt missing')
    return {'source':source,'case':root.name,'wholeStores':stores,'originalResultDeferredAndRecovered':True,'embedded':embedded}


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('directory',type=Path);args=parser.parse_args();print(json.dumps(verify_case(args.directory),indent=2))
