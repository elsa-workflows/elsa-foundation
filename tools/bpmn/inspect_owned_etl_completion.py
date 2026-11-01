#!/usr/bin/env python3
"""Reopen only this controller's completed private fixture for read-only Studio inspection."""
import argparse,hashlib,json,os,signal,subprocess,time,urllib.request
from pathlib import Path

p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--workbench',type=Path,required=True);p.add_argument('--studio',type=Path,required=True);p.add_argument('--reader',type=Path,required=True);args=p.parse_args()
root=args.directory.resolve();ready=json.loads((root/'ready.json').read_text());previous=json.loads((root/'process.json').read_text())
private=Path(ready['privateRoot']);children=[];logs=[];records=[]
assert ready['family']=='direct-etl' and (root/'browser-result.json').exists()
assert json.loads((root/'browser-result.json').read_text())['passed'] is True
assert not (root/'replacement-inspection-controller.json').exists()
sha=lambda path:hashlib.sha256(path.read_bytes()).hexdigest()
try:
 for role,assembly,folder,url in [('workbench',args.workbench.resolve(),'workbench',ready['backendUrl']),('studio',args.studio.resolve(),'studio',ready['studioUrl'])]:
  expected=next(item for item in previous['processes'] if item['role']==role)['assemblySha256'];assert sha(assembly)==expected
  log=(root/('replacement-inspection-'+role+'.log')).open('w');logs.append(log)
  child=subprocess.Popen(['dotnet',str(assembly),'--contentRoot',str(private/folder),'--urls',url],cwd=private/folder,env={**os.environ,'ASPNETCORE_ENVIRONMENT':'Development'},stdout=log,stderr=subprocess.STDOUT);children.append(child)
  records.append({'role':role,'processId':child.pid,'controllerProcessId':os.getpid(),'assemblySha256':sha(assembly)})
 for child,record in zip(children,records):
  url=ready['backendUrl']+'/health/ready' if record['role']=='workbench' else ready['studioUrl'];deadline=time.monotonic()+60
  while True:
   assert child.poll() is None,'Owned inspection host exited before readiness'
   try:
    with urllib.request.urlopen(url,timeout=2) as response:
     if response.status==200:break
   except (OSError,TimeoutError):pass
   if time.monotonic()>deadline:raise TimeoutError('Owned inspection host readiness')
   time.sleep(.2)
 subprocess.run(['node','tools/bpmn/inspect_completed_etl_studio.mjs',str(root)],check=True)
finally:
 for child in reversed(children):
  if child.poll() is None:
   # Popen still owns this exact child; no PID search or unrelated process is stopped.
   child.terminate()
   try:child.wait(timeout=20)
   except subprocess.TimeoutExpired:child.kill();child.wait(timeout=10)
 for log in logs:log.close()
original=json.loads((root/'finished-typed-read.json').read_text())['models'][0]
execution=original['model']['executionId']
observed=json.loads(subprocess.check_output(['dotnet',str(args.reader.resolve()),'--read-bpmn-direct',str(private/'workbench/runtime.db'),execution],text=True))
assert observed==original,'Read-only replacement inspection changed model receipts or native state'
(root/'replacement-inspection-controller.json').write_text(json.dumps({'passed':True,'readOnlyUi':True,'modelAndReceiptsUnchanged':True,'productSource':ready['sourceRevision'],'processes':records,'allChildrenStopped':all(child.poll() is not None for child in children)},indent=2)+'\n')
print('Replacement Studio inspection passed; no execution commands, unchanged model/receipts; all owned hosts stopped.')
