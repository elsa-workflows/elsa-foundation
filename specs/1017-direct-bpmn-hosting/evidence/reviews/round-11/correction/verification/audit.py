from pathlib import Path
import json,gzip,hashlib,sqlite3,base64,datetime,subprocess
import argparse
parser=argparse.ArgumentParser(description='Verify the immutable round11 original-pending-ETL cancellation receipt with readonly SQLite inputs.')
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
repo=Path(__file__).resolve().parents[7]
src=Path(__file__).resolve().parents[1]/'host'
out=args.output.resolve();out.mkdir(parents=True,exist_ok=False)
orig=repo/'specs/1017-direct-bpmn-hosting/evidence/reviews/round-7/pre-correction/waiting'

def sha(b):return hashlib.sha256(b).hexdigest()
def load(p):return json.loads(p.read_text())
def dec(x):return base64.b64decode(x).decode('utf-16-le')
def open_db(p):
 d=sqlite3.connect('file:'+str(p)+'?mode=ro&immutable=1',uri=True);d.row_factory=sqlite3.Row
 assert d.execute('pragma integrity_check').fetchall()[0][0]=='ok';return d
case=load(src/'case.json');capture=load(orig/'process.json');assert sha(gzip.decompress((src/'executed-controller.py.gz').read_bytes()))==case['controllerSha256']
assert case['sourceRevision']=='6bd8ac3e86d1701603b1c975534514d3fe62933b';assert case['reviewedHead']=='f83df1b56ae3f8f41e70f7f61b9c0ad60bad74e1';assert case['originalWriter']==capture['sourceRevision']=='9508e25e57672c569f462aff9a1e0282c5780eb9'
assert len(case['processes'])==2 and len({p['pid'] for p in case['processes']})==2;assert all(p['exitCode']==-9 for p in case['processes'])
dbs={}; manifest=[]
for phase in ['original','before','completed','retained']:
 for store in ['elsa','etl-runtime','etl-design']:
  item=capture['stores'][store] if phase=='original' else case[phase+'Stores'][store]
  path=(orig if phase=='original' else src)/item['file'];b=path.read_bytes();d=gzip.decompress(b)
  assert sha(b)==item['gzipSha256'];assert sha(d)==item['databaseSha256'];target=out/(phase+'-'+store+'.sqlite');target.write_bytes(d);dbs[(phase,store)]=open_db(target)
  manifest.append({'phase':phase,'store':store,'source':str(path),'gzipSha256':sha(b),'databaseSha256':sha(d),'integrity':'ok'})
def table(phase,t,store='elsa'):
 return [dict(r) for r in dbs[(phase,store)].execute('select * from '+t)]
def docs(phase,t,key):
 return {dec(j[key]):j for j in [json.loads(r['ContentJson']) for r in table(phase,t)]}
def models(p):return docs(p,'elsa_runtime_model_execution','executionId')
def workers(p):return docs(p,'elsa_runtime_workflow_execution_state','workflowExecutionId')
def outbox(p):return docs(p,'elsa_runtime_post_commit_outbox','outboxItemId')
def model_inputs(p):
 return [json.loads(x['ContentJson']) for x in table(p,'elsa_runtime_model_input')]
A=capture['outerModelExecutionId'];B=capture['innerModelExecutionId'];W1=capture['middleNativeExecutionId'];W2=capture['etlWorkerExecutionId'];run_id=capture['runId']
assert len(models('original'))==len(models('before'))==len(models('completed'))==len(models('retained'))==2
assert len(workers('original'))==len(workers('before'))==len(workers('completed'))==len(workers('retained'))==2
for ident in [A,B]:
 assert models('original')[ident]==models('before')[ident]
 assert models('original')[ident]['lifecycle']=='Running';assert models('original')[ident]['revision']==1
 for p in ['completed','retained']:
  m=models(p)[ident];assert m['lifecycle']=='TerminalSettled';assert m['terminalResult']['status']=='Cancelled';assert not m['payload']['work'];assert all(x['settled'] for x in m['payload']['cancellations'])
  for f in ['scope','tenantId','protocol','executable','materialIncarnationId','authority','createdAt']:assert models('original')[ident][f]==m[f],(ident,f)
 assert models('completed')[ident]==models('retained')[ident]
for ident in [W1,W2]:
 assert workers('original')[ident]==workers('before')[ident];assert workers('before')[ident]['status']=='Running'
 for p in ['completed','retained']:
  w=workers(p)[ident];assert w['status']=='Cancelled'
  for f in ['workflowExecutionId','pinnedExecutable','createdAt','startedAt','tenantId','partition','authority','modelWorkOrigin','parentWorkflowExecutionId']:assert workers('original')[ident][f]==w[f],(ident,f)
  model=A if ident==W1 else B
  assert models(p)[model]['payload']['cancellations'][0]['origin']==models('original')[model]['payload']['work'][0]['nativeWork']
 assert workers('completed')[ident]==workers('retained')[ident]
for p in ['before','completed','retained']:
 assert table('original','elsa_runtime_workflow_executable')==table(p,'elsa_runtime_workflow_executable'),p
# Old accepted inputs and source intents stay unchanged; normal new receipts/sources are additive.
oldinputs={x['Id']:x for x in table('original','elsa_runtime_model_input')}
oldsources=outbox('original')
for p in ['before','completed','retained']:
 inputs={x['Id']:x for x in table(p,'elsa_runtime_model_input')};assert all(inputs.get(k)==v for k,v in oldinputs.items())
 sources=outbox(p);assert all(sources.get(k)==v for k,v in oldsources.items())
for p in ['original','before']:
 rows=table(p,'elsa_etl_runtime_runs','etl-runtime');assert len(rows)==1;assert dec(rows[0]['RunId'])==run_id;assert rows[0]['Status']==1
 assert not table(p,'elsa_etl_runtime_terminals','etl-runtime');assert not table(p,'elsa_etl_runtime_outbox','etl-runtime')
assert table('original','elsa_etl_runtime_runs','etl-runtime')==table('before','elsa_etl_runtime_runs','etl-runtime')
oldrun=table('original','elsa_etl_runtime_runs','etl-runtime')[0]
for p in ['before','completed','retained']:
 row=table(p,'elsa_etl_runtime_runs','etl-runtime')[0]
 for f in ['RunId','RunKey','ScopeKey','TenantId','PersistencePartition','InvocationFingerprint','InvocationJson','BindingsJson','CreatedAtUtcTicks']:assert row[f]==oldrun[f],f
 assert (row['AcceptedRecords'],row['RejectedRecords'],row['FilteredRecords'],row['OutstandingRecords'],row['OutstandingBytes'])==(2,0,0,0,0)
 assert row['PreparationOwner'] is None and row['PreparationExpiresAtUtcTicks'] is None
 assert table('original','elsa_etl_connector_runs','etl-runtime')==table(p,'elsa_etl_connector_runs','etl-runtime')
 assert table('original','elsa_etl_connector_partitions','etl-runtime')==table(p,'elsa_etl_connector_partitions','etl-runtime')
 for x in table(p,'elsa_etl_connector_partitions','etl-runtime'):assert x['Owner'] is None and x['ExpiresAtUtcTicks'] is None and x['OwedBatchId'] is None
 for x in table(p,'elsa_etl_runtime_partitions','etl-runtime'):assert x['Owner'] is None and x['ExpiresAtUtcTicks'] is None
for p in ['completed','retained']:
 assert table(p,'elsa_etl_runtime_runs','etl-runtime')[0]['Status']==5
 terminal=table(p,'elsa_etl_runtime_terminals','etl-runtime');assert len(terminal)==1 and terminal[0]['Outcome']==3;assert json.loads(terminal[0]['ResultJson'])['outcome']=='cancelled'
 delivery=table(p,'elsa_etl_runtime_outbox','etl-runtime');assert len(delivery)==1;d=delivery[0]
 assert d['Disposition']==2 and d['AcknowledgedAtUtcTicks'] is not None and d['Owner'] is None and d['ExpiresAtUtcTicks'] is None
 assert d['PayloadHash'] and d['Attempts']==1
 assert not table(p,'elsa_runtime_scheduler_work_item');assert not table(p,'elsa_runtime_bookmark_state')
 # Explicit model cancellation inventories link the exact original origins; no re-admission.
 assert len(model_inputs(p))==7
ins=model_inputs('retained')
def receipt(model,kind):return next(x for x in ins if dec(x['executionId'])==model and dec(x['inputKind'])==kind)
sources=outbox('retained')
def source(worker,kind):return next(x for x in sources.values() if dec(x['intent']['workflowExecutionId'])==worker and dec(x['intent']['kind'])==kind)
outer_result=source(W1,'Elsa.Bpmn.NativeWorkResult.v1');inner_result=source(W2,'Elsa.Bpmn.NativeWorkResult.v1');embedded=source(W1,'Elsa.Bpmn.CancelEmbedded.v1');etl_cancel=source(W2,'Elsa.Etl.Cancel.v1')
assert outer_result['intent']['payload']['cancellationSources']==[dec(embedded['outboxItemId'])]
assert inner_result['intent']['payload']['cancellationSources']==[dec(etl_cancel['outboxItemId'])]
assert etl_cancel['intent']['payload']==json.loads(oldrun['InvocationJson'])
assert not table('retained','elsa_etl_runtime_reservations','etl-runtime')
assert inner_result['intent']['payload']==models('retained')[B]['payload']['nativeResults'][0]
assert outer_result['intent']['payload']==models('retained')[A]['payload']['nativeResults'][0]
assert all(x['status']=='Delivered' for x in sources.values() if dec(x['intent']['kind']).startswith(('Elsa.Bpmn.','Elsa.Etl.')))
# The inner native result settles actual worker/ETL responsibility; its parent-return ACK then settles B's lifecycle.
times={
 'outerCancelAccepted':receipt(A,'cancel')['acceptedAt'],
 'innerCancelAccepted':receipt(B,'cancel-embedded')['acceptedAt'],
 'etlCancelled':json.loads(table('retained','elsa_etl_runtime_terminals','etl-runtime')[0]['ResultJson'])['settledAt'],
 'etlCancelSourceDelivered':etl_cancel['deliveredAt'],
 'etlTerminalDeliveryAcknowledgedTicks':table('retained','elsa_etl_runtime_outbox','etl-runtime')[0]['AcknowledgedAtUtcTicks'],
 'innerNativeResultAccepted':receipt(B,'native-result')['acceptedAt'],
 'cancelEmbeddedDelivered':embedded['deliveredAt'],
 'innerModelParentSettled':models('retained')[B]['updatedAt'],
 'innerNativeResultSourceDelivered':inner_result['deliveredAt'],
 'outerModelSettled':models('retained')[A]['updatedAt'],
 'outerNativeResultSourceDelivered':outer_result['deliveredAt'],
 'outerCancelSourceDelivered':source(A,'Elsa.Bpmn.CancelNativeWork.v1')['deliveredAt']}
def dt(v):return datetime.datetime.fromisoformat(v)
etl_ack=datetime.datetime(1,1,1,tzinfo=datetime.timezone.utc)+datetime.timedelta(microseconds=times['etlTerminalDeliveryAcknowledgedTicks']//10);times['etlTerminalDeliveryAcknowledged']=etl_ack.isoformat()
ordered=['outerCancelAccepted','innerCancelAccepted','etlCancelled','etlCancelSourceDelivered','etlTerminalDeliveryAcknowledged','innerNativeResultAccepted','cancelEmbeddedDelivered','innerModelParentSettled','innerNativeResultSourceDelivered','outerModelSettled','outerNativeResultSourceDelivered','outerCancelSourceDelivered']
assert all(dt(times[x])<=dt(times[y]) for x,y in zip(ordered,ordered[1:])),times
assert receipt(A,'cancel')['result']['lifecycle']=='TerminalPendingSettlement';assert receipt(B,'native-result')['result']['lifecycle']=='TerminalPendingSettlement';assert receipt(B,'native-parent-settled')['result']['lifecycle']=='TerminalSettled'
assert load(src/'cancel-response.json')['result']==receipt(A,'cancel')['result']
assert load(src/'completed-api.json')==load(src/'retained-api.json')
# Full physical retained observations are equal except the legitimately owed outer result-source delivery envelope.
for side in ['outer','inner']:
 completed=load(src/('completed-'+side+'-physical.json'));retained=load(src/('retained-'+side+'-physical.json'))
 for k in ['model','worker','startSource','resultInput']:assert completed[k]==retained[k],(side,k)
 # SDK checked observations agree with independently read physical header identity.
 assert completed['model']['executionId']==(A if side=='outer' else B)
 assert completed['model']['revision']==models('completed')[A if side=='outer' else B]['revision']
# Log all combined-store table changes instead of suggesting whole-file identity.
def normalized_rows(phase,t):
 return sorted([repr(tuple(x.values())) for x in table(phase,t)])
changes=[]
for name, in dbs[('completed','elsa')].execute("select name from sqlite_master where type='table'"):
 a=normalized_rows('completed',name);b=normalized_rows('retained',name)
 if a!=b:changes.append({'table':name,'completedRows':len(a),'retainedRows':len(b),'removedOrChangedRows':len(set(a)-set(b)),'addedOrChangedRows':len(set(b)-set(a))})
for store in ['etl-runtime','etl-design']:
 assert case['completedStores'][store]['databaseSha256']==case['retainedStores'][store]['databaseSha256']
assert case['completedStores']['elsa']['databaseSha256']!=case['retainedStores']['elsa']['databaseSha256']
# Recorded process-loss exits are historical receipt data; no old machine PID is queried.
processes=[{'pid':p['pid'],'recordedExit':p['exitCode']} for p in case['processes']]
summary={'verdict':'A1 resolved within actual original-input cancellation with compute bindings offline','source':case['sourceRevision'],'reviewedSourceEquivalentHead':case['reviewedHead'],'originalWriter':case['originalWriter'],'storesVerified':manifest,'originalNativeModelAndEtlIdentitiesPreserved':True,'fullPinsAuthorityMaterialAndBindingsPreserved':True,'sameTwoNativeWorkersAndTwoModels':True,'actualEtl':{'runId':run_id,'initialStatus':'Running','finalStatus':'Cancelled','historicalAcceptedEffects':2,'outstandingRecords':0,'outstandingBytes':0,'activePreparationOrPartitionLeases':0,'owedBatchIds':0,'terminalDeliveryDisposition':'ProvenParentTerminal','deliveryAcknowledged':True,'connectorPartitionAndBindingRowsUnchanged':True},'ordering':times,'twoColdApiViewsIdentical':True,'coldEtlRuntimeAndDesignWholeFilesIdentical':True,'coldCombinedRuntimeDesignWholeFilesIdentical':False,'etlCancelPayloadEqualsOriginalFrozenInvocation':True,'unsettledEtlReservations':0,'combinedStoreChangedTables':changes,'processes':processes,'scopeLimit':'No current HTTP compute, retry, new effect, Studio, other-provider or wider acceptance proof; original accepted effects and connector observations are historical.'}
(out/'validation.json').write_text(json.dumps(summary,indent=2)+'\n')
(out/'inputs.json').write_text(json.dumps({'controllerSha256':case['controllerSha256'],'caseSha256':sha((src/'case.json').read_bytes()),'controllerLogSha256':sha(gzip.decompress((src/'controller.log.gz').read_bytes())),'snapshots':manifest},indent=2)+'\n')
print(json.dumps({k:v for k,v in summary.items() if k!='storesVerified'},indent=2))
