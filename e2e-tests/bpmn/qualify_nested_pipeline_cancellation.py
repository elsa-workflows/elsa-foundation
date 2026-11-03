#!/usr/bin/env python3
"""Restore the real source9508 waiting chain; cancel through current standard Workbench with compute bindings offline."""
from pathlib import Path
import argparse
import json,gzip,hashlib,subprocess,os,time,tempfile,shutil,signal,urllib.request,urllib.error,urllib.parse,importlib.util,sqlite3,contextlib
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--workbench-assembly',type=Path,required=True)
parser.add_argument('--reader-assembly',type=Path,required=True)
parser.add_argument('--output',type=Path,required=True)
parser.add_argument('--expected-product-source',help='Default: exact HEAD. An earlier source is accepted only when the intervening changes contain docs/evidence or this controller alone.')
args=parser.parse_args()
repo=Path(__file__).resolve().parents[2]
fixture=repo/'specs/1017-direct-bpmn-hosting/evidence/reviews/round-7/pre-correction/waiting'
spec=importlib.util.spec_from_file_location('qualification',repo/'tools/bpmn/qualify-hosting.py')
q=importlib.util.module_from_spec(spec);spec.loader.exec_module(q)
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip()
if subprocess.check_output(['git','status','--porcelain','--untracked-files=all'],cwd=repo,text=True).strip():
 raise ValueError('Commit the controller/evidence changes before recording an exact-head journey.')
source=args.expected_product_source or head
changes=subprocess.check_output(['git','diff','--name-only',source+'..'+head],cwd=repo,text=True).splitlines()
for name in changes:
 if not (name.startswith('docs/') or name.startswith('specs/1017-direct-bpmn-hosting/') and ('/evidence/' in name or name.endswith('.md')) or name=='e2e-tests/bpmn/qualify_nested_pipeline_cancellation.py' or '/' not in name and name.endswith('.md') and name!='EXTENSION_POINTS.md'):
  raise ValueError('Rebuild the actual Workbench/reader: intervening product, test or build inputs changed: '+name)
assembly=args.workbench_assembly.resolve(strict=True);reader=args.reader_assembly.resolve(strict=True)
products=json.loads(subprocess.check_output(['dotnet',str(reader),'--inspect-native-bpmn-products',str(assembly)],text=True))
if products['sourceCommit']!=source:raise ValueError('The actual product source differs from the selected source.')
etl_products=json.loads(subprocess.check_output(['dotnet',str(reader),'--inspect-etl-products',str(assembly)],text=True))
if etl_products['sourceCommit']!=source:raise ValueError('The actual ETL host product source differs from the selected source.')
capture=json.loads((fixture/'process.json').read_text());root=Path(tempfile.mkdtemp(prefix='elsa-current-nested-cancel-'));out=args.output.resolve();out.mkdir(parents=True,exist_ok=False)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();host=None;logs=[];records=[]
def write(p,x):p.write_text(json.dumps(x,indent=2)+'\n')
def wait(f,pred,msg,seconds=90):
 end=time.monotonic()+seconds
 while time.monotonic()<end:
  v=f()
  if pred(v):return v
  time.sleep(.1)
 raise TimeoutError(msg)
def start():
 global host
 log=(out/f'host-{len(records)}.log').open('w');logs.append(log);host=subprocess.Popen(['dotnet',str(assembly),'--contentRoot',str(root),'--urls','http://127.0.0.1:0'],cwd=root,env={**os.environ,'ASPNETCORE_ENVIRONMENT':'Development'},stdout=log,stderr=subprocess.STDOUT)
 record={'pid':host.pid,'startedAt':q.process_field(host.pid,'lstart'),'controllerPid':os.getpid()};records.append(record)
 path=out/f'host-{len(records)-1}.log'
 def address():
  if host.poll() is not None:raise RuntimeError('Owned Workbench exited before readiness')
  rows=[s.split('Now listening on: ',1)[1].strip() for s in path.read_text().splitlines() if 'Now listening on: ' in s];return rows[0] if rows else None
 url=wait(address,bool,'Host did not listen',180)
 def ready():
  try:
   with urllib.request.urlopen(url+'/health/ready',timeout=3) as r:return r.status==200
  except urllib.error.HTTPError as e:
   if e.code==503:return False
   raise
 wait(ready,bool,'Host not ready',180);api=q.Api(url);api.login('admin','Password123!');record['url']=url;return api

def stop(crash=False):
 if host is None or host.poll() is not None:return
 assert int(q.process_field(host.pid,'ppid'))==os.getpid()
 if 'T' in q.process_field(host.pid,'stat'):os.kill(host.pid,signal.SIGCONT)
 host.kill() if crash else host.terminate()
 try:host.wait(timeout=30)
 except subprocess.TimeoutExpired:host.kill();host.wait(timeout=10)
 records[-1]['exitCode']=host.returncode

def snapshot(label):
 active=host is not None and host.poll() is None
 if active:
  assert int(q.process_field(host.pid,'ppid'))==os.getpid();os.kill(host.pid,signal.SIGSTOP);wait(lambda:q.process_field(host.pid,'stat'),lambda s:'T' in s,'Host did not pause',5)
 try:
  stores={}
  for name in ['elsa','etl-runtime','etl-design']:
   target=out/f'{label}-{name}.sqlite';receipt=json.loads(subprocess.check_output(['dotnet',str(reader),'--backup-sqlite',str(root/(name+'.db')),str(target)],text=True,timeout=30));assert receipt['readOnly'] and receipt['snapshotSha256']==sha(target)
   b=target.read_bytes();archive=out/(target.name+'.gz');archive.write_bytes(gzip.compress(b,mtime=0));stores[name]={'file':archive.name,'databaseSha256':sha(target),'gzipSha256':sha(archive),'observer':receipt}
  for part,model,worker in [('outer',capture['outerModelExecutionId'],capture['middleNativeExecutionId']),('inner',capture['innerModelExecutionId'],capture['etlWorkerExecutionId'])]:
   v=json.loads(subprocess.check_output(['dotnet',str(reader),'--read-bpmn-native',str(out/f'{label}-elsa.sqlite'),model,worker],text=True));assert v['readOnly'];write(out/f'{label}-{part}-physical.json',v)
  tables=['elsa_etl_runtime_runs','elsa_etl_runtime_outbox','elsa_etl_runtime_terminals','elsa_etl_connector_runs','elsa_etl_connector_partitions'];v={}
  # This is the closed, hash-verified BackupDatabase output above, never the live store.
  # Immutable reads do not need WAL sidecars from the .NET SQLite provider's VFS.
  with contextlib.closing(sqlite3.connect((out/f'{label}-etl-runtime.sqlite').as_uri()+'?mode=ro&immutable=1',uri=True)) as db:
   db.row_factory=sqlite3.Row
   for t in tables:v[t]=[dict(r) for r in db.execute('SELECT * FROM '+t)]
  write(out/f'{label}-etl-physical.json',v)
  return stores
 finally:
  if active:os.kill(host.pid,signal.SIGCONT)

try:
 for n in ['appsettings.json','appsettings.Development.json','shells.json']:shutil.copyfile(repo/'Essentials/apps/Elsa.Workbench'/n,root/n)
 inputs={}
 for n in ['elsa','etl-runtime','etl-design']:
  a=fixture/('waiting-'+n+'.sqlite.gz');item=capture['stores'][n];assert sha(a)==item['gzipSha256'];data=gzip.decompress(a.read_bytes());assert hashlib.sha256(data).hexdigest()==item['databaseSha256'];(root/(n+'.db')).write_bytes(data);inputs[n]=item
 shell=json.loads((root/'shells.json').read_text());f=shell['CShells']['Shells']['default']['Features']
 f.update({'ActivitiesBpmnHosting':{'AdmitNewExecutions':True},'ActivitiesBpmnHostingApi':{},'WorkflowsRuntimeResumption':{'SweepIntervalSeconds':.2},'EtlRuntime':{'SnapshotRoot':str(root/'etl-snapshots'),'FileBindings':[],'HttpBindings':[]},'EtlRuntimeEntityFrameworkCore':{'Provider':'Sqlite','ConnectionString':'Data Source=etl-runtime.db;Pooling=False'},'EtlRuntimeApi':{'CursorSigningKey':'owned-nested-cancellation-runtime-cursor-signing-key'},'EtlDesign':{},'EtlDesignEntityFrameworkCore':{'Provider':'Sqlite','ConnectionString':'Data Source=etl-design.db;Pooling=False'},'EtlDesignApi':{'CursorSigningKey':'owned-nested-cancellation-design-cursor-signing-key'},'EtlWorkflowsDesign':{},'EtlWorkflowsRuntime':{}})
 for n in ['WorkflowsRuntimeDistributed','WorkflowsRuntimeDistributedEntityFrameworkCorePersistence','WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence']:f.pop(n,None)
 (root/'etl-snapshots').mkdir();write(root/'shells.json',shell)
 outer=capture['outerModelExecutionId'];inner=capture['innerModelExecutionId'];escaped=urllib.parse.quote(capture['runId'],safe='')
 def view(api):return {'outerModel':api.request('/bpmn/executions/'+outer),'innerModel':api.request('/bpmn/executions/'+inner),'outerNative':api.view(capture['middleNativeExecutionId']),'innerNative':api.view(capture['etlWorkerExecutionId']),'run':api.request('/etl/runs/'+escaped),'partitions':api.request('/etl/runs/'+escaped+'/connector-partitions?limit=50')}
 api=start();before=view(api);write(out/'before-api.json',before);assert before['run']['status']=='running' and before['run']['terminalResult'] is None;assert before['outerModel']['status']==before['innerModel']['status']=='Running';before_stores=snapshot('before');print('Original waiting chain is Running in actual current Workbench,2 historical accepted effects; compute bindings unavailable.',flush=True)
 cancel=api.request('/bpmn/executions/'+outer+'/cancel',{'requestId':'cancel-original-waiting-nested','reason':'Cancel the retained waiting nested pipeline with original compute connectors offline.'});write(out/'cancel-response.json',cancel)
 final=wait(lambda:view(api),lambda v:v['outerModel']['status']==v['innerModel']['status']=='TerminalSettled' and v['run']['status']=='cancelled','Original nested cancellation did not settle',120);write(out/'completed-api.json',final);completed_stores=snapshot('completed');print('Both models settled; same real ETL Cancelled, original authority and historical effects retained.',flush=True)
 stop(crash=True);api=start();cold=view(api);write(out/'retained-api.json',cold);assert cold==final;retained_stores=snapshot('retained');stop(crash=True)
 write(out/'case.json',{'sourceRevision':source,'reviewedHead':head,'originalWriter':capture['sourceRevision'],'originalInput':str(fixture.relative_to(repo)),'inputs':inputs,'beforeStores':before_stores,'completedStores':completed_stores,'retainedStores':retained_stores,'products':products,'etlProducts':etl_products,'processes':records,'controllerSha256':sha(Path(__file__)),'scope':'Actual standard Workbench restores original waiting coupled stores with original compute connector bindings unavailable, normal outer HTTP cancellation reaches original ETL, then cold coupled retention. No current connector execution/retry or wider acceptance is claimed.'})
 print('Actual coupled cold retention passed; all owned hosts stopped.',flush=True)
except Exception as e:
 stop();stores={};observation_error=None
 try:
  if all((root/(n+'.db')).exists() for n in ['elsa','etl-runtime','etl-design']):stores=snapshot('failed')
 except Exception as observer:observation_error={'type':type(observer).__name__,'error':str(observer)}
 write(out/'failure.json',{'sourceRevision':source,'reviewedHead':head,'failureType':type(e).__name__,'failure':str(e),'stores':stores,'observationError':observation_error,'processes':records});raise
finally:
 stop()
 for f in logs:f.close()
 print('Owned private root:',root,flush=True)
