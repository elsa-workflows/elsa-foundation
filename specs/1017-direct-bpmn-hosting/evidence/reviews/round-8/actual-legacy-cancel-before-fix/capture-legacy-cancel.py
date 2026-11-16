from pathlib import Path
import gzip,hashlib,json,os,sqlite3,subprocess,time,uuid
repo=Path.cwd();out=Path('/tmp/elsa-2898-review8-closeout/actual-legacy-cancel-before-fix');out.mkdir(exist_ok=False);root=out/'owned-root';root.mkdir()
source=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip();assert not subprocess.check_output(['git','status','--porcelain'],text=True).strip()
fixture=repo/'specs/1017-direct-bpmn-hosting/evidence/reviews/round-5/acceptance/old-embedded/old-pending-parent.snapshot.sqlite.gz'
raw=gzip.decompress(fixture.read_bytes());assert hashlib.sha256(raw).hexdigest()=='0a5ea64058dcdb950fdda9e5b792264ceec2f6ca19fa9398a7733f6c62ea097e';(root/'elsa.db').write_bytes(raw)
model='bpmn-embedded:e705a3456fd038bcff55bf21094de655bd89bf8b395c96a9fe14deca2d2cb44d';nonce=str(uuid.uuid4());marker={'schemaVersion':1,'nonce':nonce,'modelExecutionId':model,'nativeParentExecutionId':'review-native-parent','controllerProcessId':os.getpid()};(root/'owned-bpmn-recovery.json').write_text(json.dumps(marker,indent=2)+'\n')
assembly=repo/'tools/bpmn/NativeWorkDispatch/bin/Release/net10.0/NativeWorkDispatch.dll';records=[]
def snapshot(name):
 p=out/(name+'.sqlite');a=sqlite3.connect('file:'+str(root/'elsa.db')+'?mode=ro',uri=True);b=sqlite3.connect(p);a.backup(b);assert b.execute('pragma integrity_check').fetchall()==[('ok',)];b.close();a.close();data=p.read_bytes();g=out/(p.name+'.gz');g.write_bytes(gzip.compress(data,mtime=0));return {'path':g.name,'sha256':hashlib.sha256(g.read_bytes()).hexdigest(),'decodedSha256':hashlib.sha256(data).hexdigest()}
snapshots={'original':snapshot('original')}
def run(mode,name):
 assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==source
 args=['dotnet',str(assembly),mode,str(root),model,nonce];start=time.time();print('starting',mode,flush=True)
 with (out/(name+'.stdout.json')).open('w') as log,(out/(name+'.stderr.log')).open('w') as err:p=subprocess.run(args,stdout=log,stderr=err,timeout=180)
 records.append({'source':source,'arguments':args,'exitCode':p.returncode,'elapsedSeconds':time.time()-start});(out/'commands.json').write_text(json.dumps(records,indent=2)+'\n');print('finished',name,p.returncode,flush=True)
 if p.returncode:raise RuntimeError('Owned SDK operation refused; see retained output')
 v=json.loads((out/(name+'.stdout.json')).read_text());snapshots[name]=snapshot(name);(out/'snapshots.json').write_text(json.dumps(snapshots,indent=2)+'\n');return v
queued=run('--enqueue-legacy-parent-cancel','parent-cancel-queued');assert len(queued['queuedParentWork'])==1
drained=run('--drain-legacy-parent-cancel','parent-cancel-consumed')
if len(drained['pendingCancelSources'])==1:
 result=run('--deliver-legacy-embedded-cancel','embedded-cancel-delivered')
else:result=drained
(out/'result.json').write_text(json.dumps({'source':source,'originalFixture':str(fixture.relative_to(repo)),'originalDecodedSha256':hashlib.sha256(raw).hexdigest(),'executionStateSeeded':False,'snapshots':snapshots,'lastOperation':result},indent=2)+'\n');print('Actual legacy transport capture complete',flush=True)
