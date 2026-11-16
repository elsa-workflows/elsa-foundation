from pathlib import Path
import gzip,hashlib,importlib.util,json,os,re,shutil,subprocess,time
repo=Path.cwd();out=Path(__file__).parent/'new-writer';out.mkdir(exist_ok=False);root=out/'host';root.mkdir();(root/'packages').mkdir()
spec=importlib.util.spec_from_file_location('qualification',repo/'tools/bpmn/qualify-hosting.py');q=importlib.util.module_from_spec(spec);spec.loader.exec_module(q)
source='1ae57d214d31ad0d58526df557e4e4f13a4ef5cf';assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==source
assert not subprocess.check_output(['git','status','--porcelain'],text=True).strip()
def save(name,value): (out/name).write_text(json.dumps(value,indent=2)+'\n')
app=repo/'Essentials/apps/Elsa.Workbench';assembly=app/'bin/Release/net10.0/Elsa.Workbench.dll';reader=repo/'tools/bpmn/DeliveryRecoveryRead/bin/Release/net10.0/DeliveryRecoveryRead.dll'
products=json.loads(subprocess.check_output(['dotnet',str(reader),'--inspect-host-products',str(assembly)],text=True));assert products['sourceCommit']==source;save('products.json',products)
for n in ('appsettings.json','appsettings.Development.json','shells.json'):shutil.copyfile(app/n,root/n)
shells=json.loads((root/'shells.json').read_text());features=shells['CShells']['Shells']['default']['Features'];features['WorkflowsRuntimeEntityFrameworkCore']['ConnectionString']='Data Source=runtime.db'
for n in ('ActivitiesDesignEntityFrameworkCore','WorkflowsDesignEntityFrameworkCore','WorkflowsPublishingEntityFrameworkCore'):features[n]['ConnectionString']='Data Source=design.db'
(root/'shells.json').write_text(json.dumps(shells,indent=2))
log=(out/'host.log').open('w');child=subprocess.Popen(['dotnet',str(assembly),'--contentRoot',str(root),'--urls','http://127.0.0.1:0'],cwd=root,env={**os.environ,'ASPNETCORE_ENVIRONMENT':'Development'},stdout=log,stderr=subprocess.STDOUT);process={'processId':child.pid,'controllerProcessId':os.getpid(),'sourceRevision':source,'startedAt':time.time()}
try:
 deadline=time.monotonic()+120;base=None
 while time.monotonic()<deadline:
  assert child.poll()is None
  match=re.search(r'Now listening on: (http://127\.0\.0\.1:\d+)',(out/'host.log').read_text())
  if match:
   base=match.group(1);api=q.Api(base)
   try:api.request('/health/ready');break
   except RuntimeError:pass
  time.sleep(.2)
 else:raise RuntimeError('Current writer not ready')
 api.login('admin','Password123!')
 event='template-baseline-real-wait';version=api.activity('Elsa.Activities.Primitives.Activities.Event');state={'variables':[],'inputs':[],'outputs':[],'rootActivity':q.event_node('real-wait',version,event),'strategyOptions':{'persistenceMode':'Durable'}}
 template=api.request('/design/workflows/templates',{'name':'Actual template migration witness','description':'Retained current writer, no state seeding.','operationKey':'template-migration-capture','content':{'state':state,'layout':[],'activityPresentation':[{'nodeId':'real-wait','displayName':'A real published wait'}]}});save('template.json',template)
 definition=api.request('/design/workflows/definitions/submit',{'name':'Actual template migration wait','state':state});save('definition.json',definition)
 published=api.request('/publishing/workflows/'+definition['version']['id']+'/publish',{});save('publication.json',published)
 run=api.request('/runtime/workflows/executables/'+published['artifactId']+'/execute',{'sourceReferenceId':published['sourceReferenceId']});execution=run['workflowExecutionId'];save('run.json',run)
 deadline=time.monotonic()+30
 while True:
  view=api.view(execution)
  if any(a['executableNodeId']=='real-wait'and a['status']=='Suspended'for a in view['activities']):break
  assert time.monotonic()<deadline;time.sleep(.1)
 assert view['instance']['status']=='Running'and not view['incidents'];save('held-view.json',view)
 save('case.json',{'sourceRevision':source,'executionId':execution,'eventName':event,'templateId':template['id'],'templateRevision':template['revision'],'template':template,'heldView':view,'definition':definition,'publication':published,'stateSeeded':False,'clockEdited':False})
finally:
 if child.poll()is None:child.terminate();child.wait(timeout=30)
 process.update(exitCode=child.returncode,stoppedAt=time.time());save('process.json',process);log.close()
for name in ('runtime','design'):
 target=out/(name+'.sqlite');receipt=json.loads(subprocess.check_output(['dotnet',str(reader),'--backup-sqlite',str(root/(name+'.db')),str(target)],text=True));assert receipt['readOnly']and receipt['snapshotSha256']==q.digest(target);save(name+'-backup.json',receipt)
 with target.open('rb')as src,gzip.open(out/(name+'.sqlite.gz'),'wb')as dst:shutil.copyfileobj(src,dst)
import sqlite3
with sqlite3.connect('file:'+str(out/'design.sqlite')+'?mode=ro&immutable=1',uri=True)as db:
 migrations=db.execute('SELECT MigrationId,ProductVersion FROM __EFMigrationsHistory_ElsaWorkflowsDesign ORDER BY MigrationId').fetchall();assert len(migrations)==1 and migrations[0][0]=='20261030020434_Initial';assert db.execute('SELECT count(*)FROM elsa_workflow_templates').fetchone()[0]==1
save('design-migrations.json',migrations)
print(json.dumps({'source':source,'executionId':execution,'templateId':template['id'],'held':True,'wholeClosedStores':2,'hostStopped':process['exitCode'],'migrations':migrations}))
