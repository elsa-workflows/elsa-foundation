from pathlib import Path
import argparse,json,os,sys,subprocess,shutil,tempfile,time,urllib.request,urllib.error,importlib.util,re,hashlib,gzip
parser=argparse.ArgumentParser(description='Capture actual pre-claim human work using the pinned old writer; this is not direct Studio acceptance.');parser.add_argument('--repo',type=Path,default=Path.cwd());parser.add_argument('--workbench-assembly',type=Path,required=True);parser.add_argument('--reader-assembly',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();repo=args.repo.resolve(strict=True);out=args.output.resolve();out.mkdir(parents=True,exist_ok=False)
s=importlib.util.spec_from_file_location('q',repo/'tools/bpmn/qualify-hosting.py');q=importlib.util.module_from_spec(s);s.loader.exec_module(q)
assembly=args.workbench_assembly.resolve(strict=True);reader=args.reader_assembly.resolve(strict=True);products=json.loads(subprocess.check_output(['dotnet',str(reader),'--inspect-native-bpmn-products',str(assembly)],text=True));source=products['sourceCommit'];assert source=='1d77ed131de3305da8b8236f739c63085bff3e73';subprocess.run(['git','diff','--exit-code',source,'--','Essentials','Extensions','tools'],cwd=repo,check=True,stdout=subprocess.DEVNULL)
root=Path(tempfile.mkdtemp(prefix='elsa-1184-authoring-'));processes=[];child=None;log=None;result={'collectorSha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'researchOnly':True,'executingProductSource':source,'repositoryHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip(),'productionTreesIdentical':True,'hostProducts':products,'stages':{}}
for n in ['appsettings.json','appsettings.Development.json','shells.json']:shutil.copyfile(repo/'Essentials/apps/Elsa.Workbench'/n,root/n)
(root/'packages').mkdir();shells=json.loads((root/'shells.json').read_text());f=shells['CShells']['Shells']['default']['Features'];f['ActivitiesBpmnHosting']={'AdmitNewExecutions':True};f['ActivitiesBpmnHostingApi']={};f['WorkflowsRuntimeResumption']={'SweepIntervalSeconds':0.2}
for n in ['WorkflowsRuntimeDistributed','WorkflowsRuntimeDistributedEntityFrameworkCorePersistence','WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence']:f.pop(n,None)
f['WorkflowsRuntimeEntityFrameworkCore']['ConnectionString']='Data Source=runtime.db'
for n in ['ActivitiesDesignEntityFrameworkCore','WorkflowsDesignEntityFrameworkCore','WorkflowsPublishingEntityFrameworkCore']:
 if n in f:f[n]['ConnectionString']='Data Source=design.db'
(root/'shells.json').write_text(json.dumps(shells,indent=2)+'\n')
def save(name,value):(out/name).write_text(json.dumps(value,indent=2)+'\n')
def stop():
 global child,log
 if child is None:return
 if child.poll() is None:
  assert int(q.process_field(child.pid,'ppid'))==os.getpid();child.terminate()
  try:child.wait(timeout=30)
  except subprocess.TimeoutExpired:child.kill();child.wait(timeout=10)
 processes[-1]['exitCode']=child.returncode
 child=None;log.close();log=None

def launch():
 global child,log
 logpath=root/('host-'+str(len(processes))+'.log');log=logpath.open('w');child=subprocess.Popen(['dotnet',str(assembly),'--contentRoot',str(root),'--urls','http://127.0.0.1:0'],cwd=root,env={**os.environ,'ASPNETCORE_ENVIRONMENT':'Development'},stdout=log,stderr=subprocess.STDOUT);processes.append({'pid':child.pid,'controller':os.getpid(),'started':q.process_field(child.pid,'lstart')});deadline=time.monotonic()+180;url=None
 while time.monotonic()<deadline:
  if child.poll() is not None:raise RuntimeError('Owned host stopped before readiness; private diagnostics retained at '+str(root))
  matches=re.findall(r'Now listening on: (http://127\.0\.0\.1:\d+)',logpath.read_text())
  if matches:
   url=matches[0]
   try:
    with urllib.request.urlopen(url+'/health/ready',timeout=3) as response:
     if response.status==200:break
   except (urllib.error.URLError,TimeoutError):pass
  time.sleep(.2)
 else:raise RuntimeError('Owned authoring probe did not become ready')
 api=q.Api(url);api.login('admin','Password123!');return api

def snapshot(stage):
 stop()
 for n in ['runtime','design']:
  target=out/(stage+'-'+n+'.sqlite');q.backup_sqlite(root/(n+'.db'),target)
  with target.open('rb') as src,gzip.GzipFile(filename=str(target)+'.gz',mode='wb',mtime=0) as dst:shutil.copyfileobj(src,dst)
  target.unlink()

try:
 api=launch();version=api.activity('Elsa.Activities.Bpmn.Activities.BpmnProcess')
 elements=[{'elementId':'start','elementType':'startEvent'},{'elementId':'human','elementType':'userTask','name':'Review order','properties':{'elsa.direct.binding':json.dumps({'formatVersion':1,'kind':'human'},separators=(',',':'))}},{'elementId':'end','elementType':'endEvent'}];flows=[{'flowId':'to-human','sourceRef':'start','targetRef':'human'},{'flowId':'to-end','sourceRef':'human','targetRef':'end'}]
 state={'variables':[],'inputs':[],'outputs':[],'rootActivity':{'nodeId':'direct-authoring','activityVersionId':version,'inputs':[],'outputs':[],'structure':{'kind':'elsa.bpmn.structure','schemaVersion':'1.0.0','payload':{'activities':[],'elements':elements,'sequenceFlows':flows}}}}
 submitted=api.request('/design/workflows/definitions/submit',{'name':'Spec1184 direct human authoring probe','state':state});save('submitted.json',submitted);draftId=submitted['draftId'];draft=api.request('/design/workflows/drafts/'+draftId);save('draft-before.json',draft);assert draft['state']['rootActivity']['structure']['payload']['elements'][1]['properties']==elements[1]['properties']
 edited=json.loads(json.dumps(draft['state']));edited['rootActivity']['structure']['payload']['elements'][1]['name']='Reviewed order';req=urllib.request.Request(api.base+'/design/workflows/drafts/'+draftId,data=json.dumps({'operationKey':'probe-save','state':edited,'layout':[{'nodeId':'start','x':0,'y':0},{'nodeId':'human','x':240,'y':0},{'nodeId':'end','x':480,'y':0}]}).encode(),method='PUT',headers={'Content-Type':'application/json'})
 with api.opener.open(req,timeout=30) as response:saved=json.load(response)
 save('draft-saved.json',saved);reopened=api.request('/design/workflows/drafts/'+draftId);save('draft-reopened.json',reopened);assert saved==reopened;promoted=api.request('/design/workflows/drafts/'+draftId+'/promote',{'operationKey':'probe-promote'});save('promoted.json',promoted);rootnode=promoted['state']['rootActivity'];assert rootnode['structure']['payload']['elements'][1]['properties']==elements[1]['properties'];result['stages']['saveReopenPromote']={'passed':True,'versionId':promoted['id'],'draftId':draftId,'propertiesPreserved':True}
 exported=api.request('/interchange/bpmn/export',{'processNode':rootnode,'processId':'probe-direct-human'});(out/'exported.bpmn').write_text(exported['xml']);imported=api.request('/interchange/bpmn/import',{'xml':exported['xml'],'processId':'probe-direct-human'});save('imported.json',imported);importedhuman=next(e for e in imported['processNode']['structure']['payload']['elements'] if e['elementId']=='human');result['stages']['xmlRoundtrip']={'metadataPreserved':importedhuman.get('properties',{}).get('elsa.direct.binding')==elements[1]['properties']['elsa.direct.binding'],'issues':imported['analysis']['issues']}
 # This fixed-fixture projection only checks existing authoring and runtime primitives. It is not the future production publisher/provenance bridge.
 process={'processId':'probe-direct-human','elements':[{'elementId':'start','elementType':'startEvent'},{'elementId':'human','elementType':'userTask','bindingRef':'human-work'},{'elementId':'end','elementType':'endEvent'}],'sequenceFlows':flows};published=api.request('/bpmn/artifacts',{'requestId':'probe-publish','definition':{'formatVersion':1,'process':process,'humanBindings':['human-work']}});save('direct-publication.json',published);started=api.request('/bpmn/executions',{'sourceReferenceId':published['sourceReferenceId'],'requestId':'probe-start'});save('started.json',started);execution=started['executionId'];path='/bpmn/executions/'+execution;work=api.request(path+'/work')['items'];assert len(work)==1;save('work-before-restart.json',work);save('execution-before-restart.json',api.request(path));snapshot('unclaimed-wait')
 api=launch();assert api.request(path+'/work')['items']==work;command={'requestId':'probe-complete','attemptId':work[0]['attemptId']};completed=api.request(path+'/work/'+work[0]['invocationId']+'/complete',command);save('completion.json',completed);view=api.request(path);assert view['status']=='TerminalSettled' and view['result']['status']=='Completed';save('completed.json',view);assert api.request(path+'/work/'+work[0]['invocationId']+'/complete',command)['replay'];save('completion-replay.json',api.request(path+'/commands/probe-complete'));snapshot('unclaimed-completed');result['stages']['directPrimitiveControl']={'passed':True,'executionId':execution,'waitSurvivedActualRestart':True,'completionReplayed':True,'productionAuthoringPublisherProved':False,'directStudioProved':False}
except Exception as error:
 result['failure']={'type':type(error).__name__,'message':str(error)};raise
finally:
 stop();result['processes']=processes;result['allOwnedChildrenTerminal']=all('exitCode' in p for p in processes);save('result.json',result)
 save('asset-index.json',{'executingProductSource':source,'assets':[{'path':p.name,'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'bytes':p.stat().st_size} for p in sorted(out.iterdir()) if p.is_file() and p.name!='asset-index.json']})
 print(json.dumps({k:result.get(k) for k in ['executingProductSource','stages','failure','allOwnedChildrenTerminal']},indent=2),flush=True)
