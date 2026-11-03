from pathlib import Path
import json,os,shutil,socket,subprocess,time,urllib.request,hashlib,signal

repo=Path('/Users/sipke/.codex/worktrees/f236/elsa-foundation')
out=Path('/tmp/elsa-2898-full-pr-r5-acceptance/second-start-control');out.mkdir(exist_ok=False)
source=Path('/tmp/elsa-2898-full-pr-r5-acceptance/pending-parent-upgrade/after-upgrade.sqlite')
root=out/'host';root.mkdir();shutil.copyfile(source,root/'runtime.db')
for name in ['appsettings.json','appsettings.Development.json','shells.json']:shutil.copyfile(repo/'Essentials/apps/Elsa.Workbench'/name,root/name)
(root/'packages').mkdir()
settings=json.loads((root/'shells.json').read_text());features=settings['CShells']['Shells']['default']['Features']
allowed={'ActivitiesBpmnHosting','ActivitiesBpmnHostingApi','ActivitiesRuntime','ActivitiesPrimitives','ActivitiesFlowchart','Events','Expressions','FileSystemDistributedLocking','Primitives','Serialization','Tasks','WorkflowsRuntimeEntityFrameworkCore','WorkflowsRuntimeResumption','WorkflowsRuntimeCheckpointPersistence','FoundationIdentityAbstractions','FoundationIdentityApi','FoundationIdentityAspNetCoreIdentity','FoundationIdentityAspNetCoreIdentityEntityFrameworkCore','IdentityIamEntityFrameworkCore','IdentityProviderConfigurationEntityFrameworkCore'}
features={k:v for k,v in features.items()if k in allowed};features.update(ActivitiesBpmnHosting={'AdmitNewExecutions':True},ActivitiesBpmnHostingApi={},WorkflowsRuntimeResumption={'SweepIntervalSeconds':0.2},ActivitiesFlowchart={})
features['WorkflowsRuntimeEntityFrameworkCore']['ConnectionString']='Data Source=runtime.db';settings['CShells']['Shells']['default']['Features']=features;(root/'shells.json').write_text(json.dumps(settings,indent=2)+'\n')
with socket.socket()as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
url=f'http://127.0.0.1:{port}'
assembly=repo/'Essentials/apps/Elsa.Workbench/bin/Release/net10.0/Elsa.Workbench.dll';reader=repo/'tools/bpmn/DeliveryRecoveryRead/bin/Release/net10.0/DeliveryRecoveryRead.dll'
model='bpmn-embedded:e705a3456fd038bcff55bf21094de655bd89bf8b395c96a9fe14deca2d2cb44d'
log=(out/'host.log').open('w');child=subprocess.Popen(['dotnet',str(assembly),'--contentRoot',str(root),'--urls',url],cwd=root,env={**os.environ,'ASPNETCORE_ENVIRONMENT':'Development'},stdout=log,stderr=subprocess.STDOUT)
identity={k:subprocess.check_output(['ps','-p',str(child.pid),'-o',k+'='],text=True).strip()for k in ['ppid','lstart','comm']}
observations=[]
try:
 deadline=time.monotonic()+90
 while True:
  if child.poll()is not None:raise RuntimeError('Owned host exited: see host.log')
  try:
   with urllib.request.urlopen(url+'/health/ready',timeout=2)as r:
    if r.status==200:break
  except OSError:pass
  if time.monotonic()>deadline:raise TimeoutError('Owned host readiness')
  time.sleep(.2)
 for i in range(7):
  value=json.loads(subprocess.check_output(['dotnet',str(reader),'--read-bpmn-direct',str(root/'runtime.db'),model],text=True))
  (out/f'view-{i}.json').write_text(json.dumps(value,indent=2)+'\n');observations.append({'index':i,'revision':value['model']['revision'],'lifecycle':value['model']['lifecycle'],'workCount':len(value['model']['payload']['work']),'cancellations':value['model']['payload'].get('cancellations'),'nativeWorkflows':value['nativeWorkflowRows'],'queuedInputs':len(value['queuedModelInputs'])})
  if i<6:time.sleep(2)
 os.kill(child.pid,signal.SIGSTOP)
 try:
  deadline=time.monotonic()+5
  while 'T'not in subprocess.check_output(['ps','-p',str(child.pid),'-o','stat='],text=True):
   if time.monotonic()>deadline:raise TimeoutError('Owned host pause')
   time.sleep(.02)
  receipt=json.loads(subprocess.check_output(['dotnet',str(reader),'--backup-sqlite',str(root/'runtime.db'),str(out/'after-upgrade.sqlite')],text=True));(out/'backup-receipt.json').write_text(json.dumps(receipt,indent=2)+'\n')
 finally:os.kill(child.pid,signal.SIGCONT)
finally:
 if child.poll()is None:
  assert subprocess.check_output(['ps','-p',str(child.pid),'-o','lstart='],text=True).strip()==identity['lstart']
  child.terminate()
  try:child.wait(timeout=20)
  except subprocess.TimeoutExpired:child.kill();child.wait(timeout=10)
 log.close()
 (out/'process.json').write_text(json.dumps({'processId':child.pid,'identity':identity,'controllerProcessId':os.getpid(),'hostAssemblySha256':hashlib.sha256(assembly.read_bytes()).hexdigest(),'exitCode':child.returncode,'originalProcessStopped':True,'backendUrl':url,'runtimeOnly':True,'designAbsent':not(root/'design.db').exists(),'baselineStoreSha256':hashlib.sha256(source.read_bytes()).hexdigest(),'observations':observations},indent=2)+'\n')
print(json.dumps(observations,indent=2))
