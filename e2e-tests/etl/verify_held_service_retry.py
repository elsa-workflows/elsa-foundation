#!/usr/bin/env python3
"""Read-only checks of the actual held-service retry captures and whole stores."""
import argparse
import base64
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

_spec=importlib.util.spec_from_file_location('http_recovery',Path(__file__).with_name('verify_http_recovery.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
q=v.q


def require(condition,message):
    if not condition:raise ValueError(message)


def opaque(value):
    if isinstance(value,str):return v.decoded(value)
    if isinstance(value,dict):return {k:opaque(x) for k,x in value.items()}
    if isinstance(value,list):return [opaque(x) for x in value]
    return value


def source_and_receipt(checked,result,revision):
    origin=result['origin'];source=checked['resultSource'];receipt=checked['resultInput']
    require(source is not None and receipt is not None and source['status']==2 and
        source['outboxItemId']==result['sourceOutboxItemId'] and source['intent']['payload']==result and
        source['intent']['kind']==origin['resultIntentKind'] and source['stimulusDeliverySourceScope']==origin['scope'] and
        receipt['executionId']==origin['executionId'] and receipt['inputId']==origin['resultInputId'] and receipt['inputKind']=='native-result' and
        receipt['invocationId']==origin['invocationId'] and receipt['attemptId']==origin['attemptId'] and receipt['acceptedRevision']==revision and
        receipt['result']['status']=='Accepted' and v.instant(source['deliveredAt'])>=v.instant(receipt['acceptedAt']),
        'A worker result was acknowledged without its exact physical-attempt consumption receipt.')


def verify_case(root,expected_parent,source,authorization=False):
    read=lambda name:json.loads((root/name).read_text())
    p=read('process.json');embedded=expected_parent!='direct';products=p['products']
    require(p['schemaVersion']==1 and p['kind']=='actual-model-held-service-retry' and p['sourceRevision']==source and p['parent']=='BpmnModel' and
        p['embeddedParent']==({'root':'Root','sequence':'Sequence','flowchart':'Flowchart','compensationscope':'CompensationScope'}.get(expected_parent)) and
        p['technicalFault'] and not p['businessError'] and p['technicalFaultPolicy']=='HoldForRetry' and p['maxServiceAttempts']==2 and
        p['retryAfterSeconds']==20 and p['resumptionSweepIntervalSeconds']==.2 and p['predecessorExitCode']==p['heldPredecessorExitCode']==-9 and
        p.get('authorizationControls',False) is authorization and
        [x['role'] for x in p['processes']]==(['http-fixture','workbench','workbench','auth-sdk','workbench','workbench'] if authorization else ['http-fixture']+['workbench']*4) and
        len({x['processId'] for x in p['processes']})==(6 if authorization else 5) and
        len({x['controllerProcessId'] for x in p['processes']})==1 and all(x['started'] for x in p['processes']),
        'The held retry lacks its exact policy, source or two real process-loss cuts.')
    require(products['sourceCommit']==source and products['metadataOnly'] and len(products['actualProductVersions'])==12 and
        all(x['productVersion'].endswith('+'+source) for x in products['actualProductVersions']), 'The held retry host products differ from their executing source.')
    before=read('before.json');held=read('held.json');final=read('finished.json')
    observations={name:read(name+'-checked-model.json') for name in ('before','held','finished','retained')}
    b,h,f=(observations[x] for x in ('before','held','finished'));replacement=read('finished-checked-replacement.json')
    require(final==read('retained.json') and held==read('held-after-restart.json') and f==observations['retained'] and
        replacement==read('retained-checked-replacement.json'),'A failed attempt, held incident or successful retry changed after another standard process.')
    first=b['worker']['modelWorkOrigin'];second=replacement['worker']['modelWorkOrigin'];pin={k:p['publication'][k] for k in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')}
    require(first['invocationId']==second['invocationId'] and first['attemptId']!=second['attemptId'] and first['workerExecutionId']!=second['workerExecutionId'] and
        first['workerExecutionId']==p['workflowExecutionId'] and second['workerExecutionId']==p['replacementWorkerExecutionId'] and p['runId']!=p['replacementRunId'],
        'The retry did not preserve one logical invocation with exactly two different physical attempts.')
    for checked,origin in [(x,first) for x in observations.values()]+[(replacement,second)]:
        model=checked['model'];worker=checked['worker'];start=checked['startSource']
        require(checked['readOnly'] and model['executionId']==p['modelExecutionId'] and model['executable']==p['modelPublication']['executable'] and
            model['materialIncarnationId']==p['modelPublication']['incarnationId'] and worker['modelWorkOrigin']==origin and worker['pinnedExecutable']==pin and
            worker['workflowExecutionId']==origin['workerExecutionId'] and worker['parentWorkflowExecutionId'] is None and worker['authority']==model['authority'] and
            origin['executable']==model['executable'] and origin['workerExecutable']==pin and origin['scope']==model['scope'] and origin['executionId']==model['executionId'] and
            origin['materialIncarnationId']==model['materialIncarnationId'] and start['status']==2 and start['outboxItemId']==origin['sourceOutboxItemId'] and
            start['intent']['payload']==origin and start['intent']['kind']=='Elsa.Bpmn.StartNativeWork.v1', 'A real physical worker lost its full model/worker pin, authority or committed start source.')
    bm,hm,fm=(x['model'] for x in (b,h,f));old,=hm['payload']['nativeResults'];failure,=hm['payload']['failures'];resolved,=fm['payload']['failures']
    require(bm['revision']==1 and bm['lifecycle']==0 and bm['payload']['formatVersion']==(8 if embedded else 7) and bm['payload']['failures']==[] and
        b['resultInput'] is None and b['resultSource'] is None and b['worker']['status']==1 and
        hm['revision']==2 and hm['lifecycle']==1 and hm['terminalResult'] is None and held['execution']['model']['status']=='WaitingIncident' and
        bm['payload']['state']==hm['payload']['state']==failure['semanticState'] and bm['payload']['work']==hm['payload']['work'] and
        len(hm['payload']['work'])==1 and hm['payload']['work'][0]['attemptNumber']==1 and failure['status']=='Open' and failure['attemptNumber']==1 and failure['maxAttempts']==2 and
        failure['failedAttemptId']==first['attemptId'] and failure['invocationId']==first['invocationId'] and failure['resultInputId']==first['resultInputId'] and
        old['origin']==first and old['status']==4 and old['completion'] is None and old['faultCode']=='etl.snapshot-read-failed' and
        h['worker']==f['worker'] and h['worker']['status']==4, 'Holding the real technical fault changed its active semantic token or original failed worker.')
    incident,=held['incidents']['items'];current_incident,=final['incidents']['items']
    require(incident=={'incidentId':failure['incidentId'],'elementId':failure['elementId'],'bindingId':failure['bindingId'],'invocationId':failure['invocationId'],
        'failedAttemptId':failure['failedAttemptId'],'workerExecutionId':first['workerExecutionId'],'faultCode':old['faultCode'],'failedAt':failure['failedAt'],
        'attemptNumber':1,'maxAttempts':2,'status':'Open','retryAttemptId':None} and current_incident=={**incident,'status':'Retried','retryAttemptId':second['attemptId']} and
        held['incidents']['nextContinuationToken'] is None and final['incidents']['nextContinuationToken'] is None,
        'Incident inspection lost its exact failed attempt or retained retry disposition.')
    command=read('retry-command.json');request=command['request'];accepted=command['accepted'];duplicate=command['duplicate']
    require(request=={'requestId':'retry-actual-held-service','incidentId':failure['incidentId'],'invocationId':first['invocationId'],'failedAttemptId':first['attemptId']} and
        not accepted['pending'] and accepted['result']=={'status':'Accepted','code':None,'revision':3,'lifecycle':'Running'} and
        duplicate['replay'] and not duplicate['pending'] and duplicate['result']==accepted['result'] and command['conflictingContentHttpStatus']==409 and
        resolved=={**failure,'status':'Retried','retryAttemptId':second['attemptId'],'resolutionInputId':request['requestId']},
        'The exact retry command lost bounded acceptance, idempotent replay or conflicting-content refusal.')
    success,=fm['payload']['nativeResults'][1:]
    require(fm['revision']==(5 if embedded else 4) and fm['lifecycle']==3 and fm['terminalResult']=={'status':'Completed','outcome':'Done'} and
        fm['payload']['work']==[] and fm['payload']['nativeResults'][0]==old and success['origin']==second and success['status']==3 and success['faultCode'] is None and
        success['completion']['outcomeKey']=='Done' and not success['completion']['isRedacted'] and
        success['completion']['resultType']['alias']=='Elsa.Etl.Workflows.Runtime.Models.RunEtlResult' and
        success['completion']['result']=={'runId':p['replacementRunId'],'outcome':0,'acceptedRecords':4,'rejectedRecords':0,'filteredRecords':0,'diagnosticCode':None},
        'The successful retry lost its typed native result, original fault history or final model settlement.')
    source_and_receipt(h,old,2);source_and_receipt(f,old,2);source_and_receipt(replacement,success,4)
    require(final['original']['run']==held['execution']['run'] and final['original']['native']==held['execution']['native'] and
        final['original']['run']['status']=='faulted' and final['original']['run']['terminalResult']['diagnostic']['code']=='etl.snapshot-read-failed' and
        final['replacement']['run']['status']=='succeeded' and final['replacement']['run']['acceptedRecords']==4 and final['replacement']['run']['rejectedRecords']==0 and
        final['replacement']['native']['instance']['status']=='Completed' and not final['replacement']['native']['incidents'],
        'A new physical attempt overwrote the original native/ETL fault or failed to complete.')
    retry,=[x['http']['fetch'] for x in before['partitions']['items'] if x['partitionOrdinal']==1]
    require(retry['phase']=='waiting' and retry['attemptCount']==1 and retry['lastStatusCode']==429 and v.instant(retry['notBefore'])>v.instant(p['cutAtUtc']) and
        next(x['http']['fetch'] for x in final['original']['partitions']['items'] if x['partitionOrdinal']==1)==retry,
        'The failed attempt lost its original durable HTTP retry checkpoint.')
    early=read('before-wire.json');late=read('finished-wire.json')
    require(early==read('held-wire.json') and late==read('retained-wire.json') and not early['overflow'] and not late['overflow'] and
        late['requests'][:len(early['requests'])]==early['requests'] and late['outputPosts'][:len(early['outputPosts'])]==early['outputPosts'] and
        Counter(x['id'] for post in early['outputPosts'] for x in post['records'])==Counter({1:1,2:1}) and
        Counter((x['id'],x['name']) for post in late['outputPosts'] for x in post['records'])==Counter({(1,'Ada'):2,(2,'Grace'):2,(3,'Linus'):1,(4,'Margaret'):1}) and
        all(x['hasIdempotencyKey'] for x in late['outputPosts']), 'The real retry lost its retained prior effects or honest at-least-once physical output.')
    unavailable=read('snapshot-unavailability.json');content=q.digest(root/'lookup-source.csv')
    require(q.digest(root/'lookup-snapshot.csv')==content and all(unavailable[x]==content for x in ('sourceSha256','snapshotSha256Before','snapshotSha256WhileUnavailable','restoredSha256')) and
        unavailable['sourceRevision']==source and unavailable['predecessorExitCode']==-9 and unavailable['sourceStillAvailable'] and unavailable['originalSnapshotReadPathMissing'] and
        unavailable['restoredBeforeAuthorizedRetry'] and re.fullmatch('[a-f0-9]{64}\\.csv',unavailable['snapshotKey']) and
        unavailable['unavailableKey']==unavailable['snapshotKey']+'.unavailable' and v.instant(unavailable['restoredAtUtc'])>v.instant(p['heldCutAtUtc']) and
        v.instant(unavailable['restoredAtUtc'])<v.instant(replacement['worker']['createdAt']), 'The retry repaired a different snapshot or changed immutable lookup bytes.')
    original_invocation=None;stores=0
    for phase in ('before','held','finished'):
        observed=observations[phase]
        require(set(p[phase+'Stores'])=={'elsa','etl-runtime','etl-design'},'The held retry lacks all coupled whole stores.')
        for store,snapshot in p[phase+'Stores'].items():
            file=q.evidence_file(root,snapshot['file']);raw=gzip.decompress(file.read_bytes())
            require(q.digest(file)==snapshot['gzipSha256'] and hashlib.sha256(raw).hexdigest()==snapshot['databaseSha256'] and snapshot['observer']['readOnly'] and
                snapshot['observer']['snapshotSha256']==snapshot['databaseSha256'],'A provider-observed held retry snapshot differs from its actual bytes.')
            with tempfile.TemporaryDirectory(prefix='elsa-held-service-read-') as temporary:
                db=Path(temporary)/'snapshot.sqlite';db.write_bytes(raw)
                with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                    require(c.execute('PRAGMA integrity_check').fetchall()==[('ok',)],'A held retry whole store failed integrity verification.')
                    if store=='elsa':physical_runtime(c,phase,p,observed,replacement,old,success,command,embedded)
                    elif store=='etl-runtime':
                        inv=physical_etl(c,phase,p,before,final,content,unavailable)
                        if original_invocation is None:original_invocation=inv
                        require(inv==original_invocation,'The original immutable ETL invocation changed after retry.')
            stores+=1
    if embedded:verify_parent(root,p,observations,final)
    if authorization:verify_authorization(root,p,h['resultInput'],read('retry-command.json')['accepted'])
    return {'parent':expected_parent,'wholeStoresVerified':stores,'physicalOutputRecords':6,'failedAttemptPreserved':True}


def verify_authorization(root,p,original_receipt,retry_command):
    read=lambda name:json.loads((root/name).read_text());setup=read('auth-probe-users.json');source=p['sourceRevision']
    expected={'reader':('default',{'bpmn:read'}),'retrier':('default',{'bpmn:retry-service'}),'nobody':('default',set()),'foreign':('bpmn-probe-foreign',{'bpmn:read','bpmn:retry-service'})}
    def no_credentials(value):
        if isinstance(value,dict):
            require(not {key.lower() for key in value}&{'password','passwordhash','access_token','accesstoken','refresh_token','refreshtoken','cookie','cookies','authorization','credentials'},
                'Public authorization observations contain a credential or cookie field.')
            for item in value.values():no_credentials(item)
        elif isinstance(value,list):
            for item in value:no_credentials(item)
    no_credentials(setup)
    users={u['userName'].removeprefix('bpmn-probe-'):u for u in setup['users']}
    require(setup['operation']=='create-owned-auth-probe-users' and setup['credentialsIncluded'] is False and setup['executionStateSeeded'] is False and
        len(setup['users'])==4 and set(users)==set(expected) and len({u['userId'] for u in users.values()})==len({u['roleId'] for u in users.values()})==4 and
        len(setup['products'])==5 and all(x['informationalVersion'].endswith('+'+source) and re.fullmatch('[a-f0-9]{64}',x['sha256']) for x in setup['products']),
        'The authorization journey lost its normal Identity setup, source or distinct scoped accounts.')
    for name,(tenant,permissions) in expected.items():
        require(users[name]['tenantId']==tenant and set(users[name]['permissions'])==permissions,'An owned Identity account acquired a different tenant or permission grant.')
    for stage in ('before','after','retained'):
        evidence=read('auth-'+stage+'.json');principals=evidence['principals'];outcomes=evidence['outcomes']
        no_credentials(evidence)
        require(evidence['schemaVersion']==1 and evidence['kind']=='actual-bpmn-retry-http-authorization' and evidence['sourceRevision']==source and evidence['stage']==stage and
            evidence['modelExecutionId']==p['modelExecutionId'] and evidence['credentialsIncluded'] is False and evidence['mockAuthenticationUsed'] is False and
            len(principals)==4 and {x['profile'] for x in principals}==set(expected),'The actual HTTP authorization stage lost its real signed-in principals or scope.')
        for principal in principals:
            profile=principal['profile'];session=principal['session'];user=users[profile];tenant,permissions=expected[profile]
            require(session['status']=='authenticated' and session['subject']==user['userId'] and session['tenantId']==tenant and set(session['permissions'])==permissions and
                session['roles']==[user['roleId']], 'The real sign-in principal differs from its stored scoped Identity grant.')
        wanted=set()
        for profile in ('anonymous',*expected):
            for operation in ('read-execution','read-incidents','read-command'):wanted.add((profile,operation))
            if profile!='reader':wanted.add((profile,'read-unknown-command'))
            if stage!='before' or profile!='retrier':wanted.add((profile,'retry-before-admission' if stage=='before' else 'retry-replay'))
        require(len(outcomes)==len(wanted) and {(x['profile'],x['operation']) for x in outcomes}==wanted,'An authorization stage omitted or duplicated a required HTTP probe.')
        for outcome in outcomes:
            profile=outcome['profile'];operation=outcome['operation'];allowed=profile=='reader' and operation.startswith('read-') or profile=='retrier' and operation=='retry-replay'
            codes=[200] if allowed else [401] if profile=='anonymous' else [403,404]
            require(outcome['permittedHttpStatuses']==codes and outcome['httpStatus'] in codes and outcome['returnedOutcome'] is allowed,
                'A denied/replayed HTTP operation disclosed an outcome or crossed its actual permission/tenant boundary.')
        for profile in ('anonymous','retrier','nobody','foreign'):
            codes=[x['httpStatus'] for x in outcomes if x['profile']==profile and x['operation'] in ('read-command','read-unknown-command')]
            require(len(set(codes))==1,'Denied existing/missing commands exposed a resource-existence distinction.')
        command=evidence['expectedCommand'];wanted_command=original_receipt if stage=='before' else retry_command
        require(command['inputId']==wanted_command['inputId'] and command['result']==wanted_command['result'],'The role-authorized read/replay inspected a different retained command result.')
    # Identity grants are ordinary persisted user/role writes; no runtime state is seeded.
    raw=gzip.decompress((root/p['finishedStores']['elsa']['file']).read_bytes())
    with tempfile.TemporaryDirectory(prefix='elsa-auth-grants-read-') as tmp:
        db=Path(tmp)/'snapshot.sqlite';db.write_bytes(raw)
        with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
            def decode(value):return base64.b64decode(value,validate=True).decode('utf-16le')
            def values(value):return {decode(x) for x in json.loads(decode(value))}
            for profile,user in users.items():
                row=c.execute('SELECT TenantId,UserId,UserName,RoleIdsJson,DirectPermissionsJson,Status,PasswordHash FROM identity_users WHERE UserId=?',(v.key(user['userId']),)).fetchone()
                require(row is not None and decode(row[0])==user['tenantId'] and decode(row[1])==user['userId'] and decode(row[2])==user['userName'] and
                    values(row[3])=={user['roleId']} and values(row[4])==set() and row[5]==0 and bool(row[6]),'The real signed-in account lacks its normal stored credential/role relationship.')
                row=c.execute('SELECT TenantId,RoleId,Name,PermissionsJson,System FROM identity_roles WHERE RoleId=?',(v.key(user['roleId']),)).fetchone()
                require(row is not None and decode(row[0])==user['tenantId'] and decode(row[1])==user['roleId'] and decode(row[2])==user['userName']+'-role' and
                    values(row[3])==expected[profile][1] and row[4]==0,'The physical Identity role lost its exact bounded permission or tenant grant.')
                links=c.execute('SELECT TenantId,UserId,RoleId FROM identity_user_roles WHERE UserId=?',(v.key(user['userId']),)).fetchall()
                require(len(links)==1 and tuple(decode(x) for x in links[0])==(user['tenantId'],user['userId'],user['roleId']), 'The actual framework user-role membership differs from the authorized principal.')


def physical_runtime(c,phase,p,observed,replacement,old,success,command,embedded):
    key=v.key;model=observed['model'];expected_count=(2 if phase=='finished' else 1)+int(embedded)
    row=c.execute('SELECT Revision,Lifecycle,ContentJson FROM elsa_runtime_model_execution WHERE ExecutionId=?',(key(model['executionId']),)).fetchone()
    require(row is not None and row[:2]==(model['revision'],model['lifecycle']) and
        json.loads(row[2])['payload']==model['payload'] and json.loads(row[2])['terminalResult']==model['terminalResult'] and
        json.loads(row[2])['executable']==v.encoded_tree(model['executable']) and
        c.execute('SELECT COUNT(*) FROM elsa_runtime_model_execution').fetchone()[0]==1 and
        c.execute('SELECT COUNT(*) FROM elsa_runtime_workflow_execution_state').fetchone()[0]==expected_count,
        'The physical model state or exact native-worker inventory differs from its reader.')
    workers=[observed]+([replacement] if phase=='finished' else [])
    for checked in workers:
        worker=checked['worker'];origin=worker['modelWorkOrigin']
        row=c.execute('SELECT SchemaVersion,Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(key(worker['workflowExecutionId']),)).fetchone()
        require(row is not None and row[0]=='2.0.0' and row[1]==worker['status'] and json.loads(row[2])['modelWorkOrigin']==v.encoded_tree(origin) and
            json.loads(row[2])['pinnedExecutable']==v.encoded_tree(worker['pinnedExecutable']), 'The physical worker lost its protected origin, full pin or original status.')
        activity,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state WHERE WorkflowExecutionId=?',(key(worker['workflowExecutionId']),)).fetchall()
        state=json.loads(activity[1])
        if worker['status']==4:
            require(activity[0]=='Faulted' and state['completion'] is None and state['fault']['code']==key('etl.snapshot-read-failed'),
                'The physical original native root lost its real technical fault.')
        elif worker['status']==3:
            completion=state['completion'];expected=success['completion']
            require(activity[0]=='Completed' and completion['outcomeKey']==key('Done') and completion['invocationId']==key(expected['invocationId']) and
                completion['attemptId']==key(expected['attemptId']) and completion['contractFingerprint']==key(expected['contractFingerprint']) and
                completion['result']['type']['alias']==key(expected['resultType']['alias']) and completion['result']['inlineValue']==expected['result'],
                'The physical replacement root lost its exact typed RunEtl success.')
        for name in ('startSource','resultSource'):
            source=checked[name]
            if source is None:continue
            row=c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(key(source['outboxItemId']),)).fetchone()
            require(row is not None and row[0]==source['status'] and json.loads(row[1])['intent']['payload']==source['intent']['payload'] and
                json.loads(row[1])['deliveredAt']==source['deliveredAt'], 'The physical start/result source differs from its exact acknowledgment proof.')
    receipts=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_model_input')]
    require(len(receipts)==(1 if phase=='before' else 2 if phase=='held' else 4+int(embedded)),
        'The physical model accepted an extra input or lost the single retry receipt.')
    for checked in workers:
        receipt=checked['resultInput']
        if receipt is None:continue
        row,=[r for r in receipts if r['inputId']==key(receipt['inputId'])]
        require(row['contentFingerprint']==key(receipt['contentFingerprint']) and row['result']==receipt['result'] and row['acceptedRevision']==receipt['acceptedRevision'] and
            row['invocationId']==key(receipt['invocationId']) and row['attemptId']==key(receipt['attemptId']), 'The physical result receipt changed its accepted attempt or original content fingerprint.')
    if phase=='finished':
        retry,=[r for r in receipts if r['inputKind']==key('retry-service')]
        require(retry['inputId']==key(command['request']['requestId']) and retry['acceptedRevision']==3 and retry['result']==command['accepted']['result'] and
            retry['invocationId']==key(old['origin']['invocationId']) and retry['attemptId']==key(old['origin']['attemptId']) and
            not c.execute('SELECT 1 FROM elsa_runtime_scheduler_work_item').fetchall(), 'The physical retry did not retain its one exact decision or left progression queued.')
    materials=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable')]
    material,=[m['material'] for m in materials if m.get('material') and m['material']['identity']==v.encoded_tree(model['executable'])]
    definition=material['definition'];binding,=definition['serviceBindings']
    require(definition['formatVersion']==3 and binding=={'bindingId':'pipeline','executable':old['origin']['workerExecutable'],'businessErrorCode':'etl.rows-rejected',
        'faultPolicy':{'formatVersion':1,'disposition':'HoldForRetry','maxAttempts':2}} and
        material['dependencies']==[{'bindingId':key('pipeline'),'executable':v.encoded_tree(old['origin']['workerExecutable'])}],
        'The physical model lost its immutable per-service policy or complete native dependency pin.')
    incidents=[json.loads(r[0]) for r in c.execute('SELECT ContentJson FROM elsa_runtime_incident_state')]
    require(len(incidents)==(0 if phase=='before' else 1) and (not incidents or incidents[0]['workflowExecutionId']==key(p['workflowExecutionId']) and
        incidents[0]['status']=='Blocking' and incidents[0]['resolutionOutcome']['actionKind']==key('FaultWorkflow')),
        'The original native fault incident was erased or reassigned during model retry.')


def physical_etl(c,phase,p,before,final,content,unavailable,include_replacement=True):
    c.row_factory=sqlite3.Row;rows=c.execute('SELECT * FROM elsa_etl_runtime_runs').fetchall()
    require(len(rows)==(2 if phase=='finished' and include_replacement else 1),'The retry created an extra physical ETL run or lost the failed run.')
    first=None
    for row in rows:
        inv=json.loads(row['InvocationJson']);run=v.decoded(inv['runId']);original=run==p['runId']
        require(original or phase=='finished' and run==p['replacementRunId'],'The physical ETL run is not one of the two actual attempts.')
        if original:first=inv
        view=before if phase=='before' else final['original'] if original else final['replacement']
        native=view['native'];activity,=native['activities'];address=inv['resultAddress'];pin=p['publication']
        require(v.decoded(address['workflowExecutionId'])==native['instance']['workflowExecutionId'] and v.decoded(address['activityExecutionId'])==activity['activityExecutionId'] and
            v.decoded(address['executableId'])==pin['artifactId'] and v.decoded(address['executableHash'])==pin['artifactHash'] and
            v.decoded(address['nodeId'])==activity['executableNodeId'] and address['registrationId']==address['bookmarkId'] and
            v.decoded(address['providerId'])=='elsa.etl.completion.v1', 'The physical ETL invocation lost its real native result address or worker pin.')
        connector=c.execute('SELECT BindingsJson,PreparedJson FROM elsa_etl_connector_runs WHERE RunKey=?',(row['RunKey'],)).fetchone()
        binding=json.loads(connector[0])['lookup'];prepared=json.loads(connector[1])['lookup']
        require(binding['kind']=='csv' and v.decoded(binding['revision']['expectedSha256'])==content and v.decoded(prepared['contentHash'])==content and
            (v.decoded(prepared['key'])==unavailable['snapshotKey'] if original else re.fullmatch('[a-f0-9]{64}\\.csv',v.decoded(prepared['key'])) is not None and v.decoded(prepared['key'])!=unavailable['snapshotKey']) and prepared['byteLength']==34 and
            v.decoded(binding['revision']['revision'])=='lookup-v1', 'The physical retry changed the original pinned and prepared lookup dependency.')
        version=p['etlPublication']['version'];artifact=inv['artifact']
        require(artifact['formatVersion']==2 and artifact['definitionVersion']==version['definitionVersion'] and v.decoded(artifact['definitionId'])==version['definitionId'] and
            v.decoded(artifact['contentHash'])==version['contentHash'], 'The physical retry changed its immutable published ETL artifact.')
        terminals=c.execute('SELECT Outcome,ResultJson FROM elsa_etl_runtime_terminals WHERE RunKey=?',(row['RunKey'],)).fetchall()
        outbox=c.execute('SELECT Attempts,Disposition,AcknowledgedAtUtcTicks FROM elsa_etl_runtime_outbox WHERE RunKey=?',(row['RunKey'],)).fetchall()
        if phase=='before':require(row['Status']==1 and not terminals and not outbox,'The original live wait already carried a terminal result.')
        else:
            require(row['Status']==(4 if original else 2) and row['AcceptedRecords']==(2 if original else 4) and row['RejectedRecords']==0 and
                row['OutstandingRecords']==row['OutstandingBytes']==0 and len(terminals)==len(outbox)==1 and terminals[0][0]==(2 if original else 0) and
                outbox[0][0]>=1 and outbox[0][1]==2 and outbox[0][2] is not None,
                'The physical old/new ETL terminal or acknowledged native delivery differs from its actual outcome.')
            terminal=json.loads(terminals[0][1])
            require(terminal['outcome']==('faulted' if original else 'succeeded') and (v.decoded(terminal['diagnostic']['code'])=='etl.snapshot-read-failed' if original else terminal['diagnostic'] is None),
                'The physical original ETL diagnostic or replacement outcome was relabeled.')
    require(first is not None,'The original ETL invocation disappeared during retry.')
    return first


def verify_parent(root,p,observations,final):
    read=lambda name:json.loads((root/name).read_text())
    phases={name:read(name+'-checked-embedding.json') for name in ('before','held','finished','retained')}
    before=phases['before'];held=phases['held'];after=phases['finished'];encoded=before['model']['payload']['nativeParent']['invocation'];origin=opaque(encoded)
    pin={k:p['nativeParentPublication'][k] for k in ('artifactId','artifactHash','artifactVersion','definitionId','definitionVersionId')}
    require(after==phases['retained'] and origin['nativeExecutionId']==p['nativeParentExecutionId'] and origin['nativeExecutable']==pin and
        origin['executionId']==p['modelExecutionId'] and origin['model']['executable']==p['modelPublication']['executable'] and
        origin['model']['materialIncarnationId']==p['modelPublication']['incarnationId'] and
        all(phases[name]['model']==observations[name]['model'] and phases[name]['model']['payload']['nativeParent']['invocation']==encoded for name in ('before','held','finished')),
        'The native parent lost its original pinned model invocation during held retry.')
    activities=[]
    for name in ('before','held','finished'):
        observed=phases[name];run,=[a for a in observed['activities'] if a['execution']['activityType']=='Elsa.Activities.Bpmn.Activities.RunBpmn'];activities.append(run)
        require(observed['nativeParent']['pinnedExecutable']==pin and observed['nativeParent']['authority']==observed['model']['authority'] and
            run['invocationId']==origin['nativeInvocationId'] and (name=='finished' or run['privateState']['producedByAttemptId']==origin['nativeAttemptId'] and
            run['privateState']['value']['inlineValue']['Invocation']==encoded) and run['status']==(4 if name=='finished' else 3) and
            observed['nativeParent']['status']==(3 if name=='finished' else 1), 'The actual RunBpmn parent changed its wait or faulted while the service was held.')
        if name!='finished':
            bookmark,=observed['bookmarks']
            require(bookmark['bookmarkId']==origin['registrationId'] and bookmark['stimulusHash']==p['modelExecutionId'] and
                not [a for a in observed['activities'] if a['execution']['executableNodeId']=='after'], 'The native parent advanced before the held model completed.')
    current=activities[-1];successors=[a for a in after['activities'] if a['execution']['executableNodeId']=='after']
    require(not after['bookmarks'] and not after['queuedModelInputs'] and not final['original']['embedding']['incidents'] and
        current['completion']['result']['inlineValue']=={'executionId':p['modelExecutionId'],'result':after['model']['terminalResult']} and
        current['completion']['outcomeKey']=='Done' and any(r['status']==1 and r['registrationId']==origin['registrationId'] and
            r['providerId']=='elsa.bpmn.native-parent.v1' and r['payloadType']['alias']=='Elsa.Activities.Bpmn.Models.RunBpmnCompletion' for r in current['triggerDeliveries']) and
        (not successors if p['embeddedParent']=='Root' else len(successors)==1 and successors[0]['status']==4),
        'The original native parent lacks its typed consumption receipt or exactly one successful successor.')
    settlement,=[r for r in after['inputs'] if r['inputKind']=='native-parent-settled']
    result,=[s for s in after['sources'] if s['intent']['kind']=='Elsa.Bpmn.ResumeNativeParent.v1']
    require(settlement['acceptedRevision']==5 and settlement['invocationId']==origin['nativeInvocationId'] and settlement['attemptId']==origin['nativeAttemptId'] and
        settlement['result']['status']=='Accepted' and len(after['inputs'])==5 and
        Counter(s['intent']['kind'] for s in after['sources'])==Counter({'Elsa.Bpmn.StartEmbedded.v1':1,'Elsa.Bpmn.StartNativeWork.v1':2,'Elsa.Bpmn.ResumeNativeParent.v1':1}) and
        all(s['status']==2 for s in after['sources']) and result['intent']['payload']=={'FormatVersion':1,'Invocation':encoded,'Result':after['model']['terminalResult']} and
        v.instant(result['deliveredAt'])>=v.instant(settlement['acceptedAt']), 'The native parent source was acknowledged without exact model settlement.')
    for phase in ('before','held','finished'):
        with tempfile.TemporaryDirectory(prefix='elsa-held-parent-read-') as tmp:
            db=Path(tmp)/'snapshot.sqlite';db.write_bytes(gzip.decompress((root/p[phase+'Stores']['elsa']['file']).read_bytes()))
            with closing(sqlite3.connect(db.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:
                observed=phases[phase];row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_workflow_execution_state WHERE WorkflowExecutionId=?',(v.key(p['nativeParentExecutionId']),)).fetchall()
                require(row[0]==observed['nativeParent']['status'] and json.loads(row[1])['pinnedExecutable']==v.encoded_tree(pin), 'The physical parent header lost its actual pin or lifecycle.')
                for activity in observed['activities']:
                    row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_activity_execution_state WHERE ActivityExecutionId=?',(v.key(activity['invocationId']),)).fetchall()
                    completion=json.loads(row[1])['completion'];expected=activity['completion']
                    require(row[0]==('Scheduled','Running','Waiting','Suspended','Completed','Faulted','Cancelled','Recovered','Superseded')[activity['status']] and
                        ((completion is None and expected is None) or completion is not None and expected is not None and
                            completion['outcomeKey']==v.key(expected['outcomeKey']) and completion['invocationId']==v.key(expected['invocationId']) and
                            completion['attemptId']==v.key(expected['attemptId']) and
                            (completion['result'] is None if expected['result'] is None else completion['result']['inlineValue']==expected['result']['inlineValue'])),
                        'The physical parent activity differs from its actual typed completion.')
                material,=c.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable WHERE ArtifactId=?',(v.key(pin['artifactId']),)).fetchall();material=json.loads(material[0])
                roots={'Root':'Elsa.Activities.Bpmn.Activities.RunBpmn','Sequence':'Elsa.Activities.Sequence.Activities.Sequence','Flowchart':'Elsa.Activities.Flowchart.Activities.Flowchart','CompensationScope':'Elsa.Activities.Compensation.Activities.CompensationScope'}
                require(material['identity']==v.encoded_tree(pin) and material['rootActivity']['activityType']==v.key(roots[p['embeddedParent']]) and
                    any(d['artifactId']==v.key(p['modelPublication']['executable']['artifactId']) and d['artifactHash']==v.key(p['modelPublication']['executable']['artifactHash']) for d in material['dependencies']),
                    'The physical native parent root shape or immutable model dependency closure differs.')
                for source in observed['sources']:
                    row,=c.execute('SELECT Status,ContentJson FROM elsa_runtime_post_commit_outbox WHERE OutboxItemId=?',(v.key(source['outboxItemId']),)).fetchall();value=json.loads(row[1])
                    require(row[0]==source['status'] and value['intent']['payload']==source['intent']['payload'] and value['deliveredAt']==source['deliveredAt'],
                        'The physical native parent source differs from its acknowledgment proof.')
                for receipt in observed['inputs']:
                    row,=c.execute('SELECT ContentJson FROM elsa_runtime_model_input WHERE InputId=?',(v.key(receipt['inputId']),)).fetchall();value=json.loads(row[0])
                    require(value['result']==receipt['result'] and value['contentFingerprint']==v.key(receipt['contentFingerprint']), 'The physical parent input receipt changed its accepted content.')


def verify(path):
    root=path.resolve().parent;manifest=json.loads(path.read_text());source=manifest['sourceRevision'];cases=manifest['cases']
    require(manifest['schemaVersion']==1 and manifest['kind']=='actual-bpmn-etl-held-retry-matrix' and re.fullmatch('[a-f0-9]{40}',source) and
        len(cases)==5 and {c['name'] for c in cases}=={'direct','root','sequence','flowchart','compensationscope'}, 'The held retry matrix lacks every unique direct/native parent.')
    authorization=manifest.get('authorizationControls',False)
    require(isinstance(authorization,bool),'The authorization qualification flag is invalid.')
    names={'process.json','before.json','recovered.json','held.json','held-after-restart.json','finished.json','retained.json','retry-command.json','snapshot-unavailability.json',
        'lookup-source.csv','lookup-snapshot.csv'}|{p+'-wire.json' for p in ('before','held','finished','retained')}|{p+'-checked-model.json' for p in ('before','held','finished','retained')}|{
        p+'-checked-replacement.json' for p in ('finished','retained')}|{p+'-'+s+'.sqlite.gz' for p in ('before','held','finished') for s in ('elsa','etl-runtime','etl-design')}
    if authorization:names|={'auth-probe-users.json','auth-before.json','auth-after.json','auth-retained.json'}
    products=None;traces={};results=[]
    for case in cases:
        directory=case['name'];assets=case['assets'];expected=names|({p+'-checked-embedding.json' for p in ('before','held','finished','retained')} if directory!='direct' else set())
        require(len(assets)==len(expected) and {a['path'] for a in assets}=={directory+'/'+n for n in expected}, 'The held retry case lacks its exact raw assets.')
        for asset in assets:require(q.digest(q.evidence_file(root,asset['path']))==asset['sha256'],'An actual held retry asset differs.')
        results.append(verify_case(root/directory,directory,source,authorization))
        process=json.loads((root/directory/'process.json').read_text())
        if products is None:products=process['products']
        require(products==process['products'],'The held retry cases used different first-party host products.')
        for phase in ('before','held','finished'):
            def normalize(value):
                if isinstance(value,dict):return {k:'<model-execution>' if k=='producingWorkHandle' and x==process['modelExecutionId'] else normalize(x) for k,x in value.items()}
                if isinstance(value,list):return [normalize(x) for x in value]
                return value
            state=normalize(json.loads((root/directory/(phase+'-checked-model.json')).read_text())['model']['payload']['state'])
            require(phase not in traces or traces[phase]==state,'The direct and native-parent held retry interpreter traces differ.')
            traces[phase]=state
    return {'actualHeldRetryJourneys':results,'wholeStoresVerified':sum(r['wholeStoresVerified'] for r in results),'sourceRevision':source,
        'oneAuthorizedPhysicalRetryProved':True,'oldFailurePreserved':True,'sameSourceInterpreterTracesCompared':True,
        'budgetExhaustionProved':False,'heldCancellationProved':False,'obsoleteResultControlsProved':False,'authorizationBeforeReplayProved':authorization,
        'tenantIsolationBeforeReplayProved':authorization,'actualAuthorizationHttpProbes':355 if authorization else 0,
        'finalUpgradeAndStudioProved':False}


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--manifest',type=Path,required=True)
    print(json.dumps(verify(parser.parse_args().manifest),indent=2))
