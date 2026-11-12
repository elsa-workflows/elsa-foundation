from pathlib import Path
import subprocess,json,time,os
root=Path.cwd();out=Path('/tmp/elsa-2898-main-3e93-qualification');source='b7e23ab0984b201ab9b3eaaaf4b5d964222ca3a8'
assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==source
assert not subprocess.check_output(['git','status','--porcelain'],text=True).strip()
unchanged=['Essentials','tools/bpmn','e2e-tests/bpmn']
equivalence={p:subprocess.check_output(['git','diff','8e9b31360d4c71c4cdd70f18cb93d70bedc57bb0',source,'--',p],text=True)=='' for p in unchanged}
assert all(equivalence.values())
(out/'source-equivalence.json').write_text(json.dumps({'currentSource':source,'priorHostingSource':'8e9b31360d4c71c4cdd70f18cb93d70bedc57bb0','unchangedTrees':equivalence},indent=2)+'\n')
steps=[('install',['pnpm','install','--frozen-lockfile'],'Studio'),('frontend-tests',['python3.14',str(out/'serial-frontend.py')],'.'),('typecheck',['pnpm','typecheck'],'Studio'),('lint',['pnpm','lint'],'Studio'),('frontend-build',['pnpm','build'],'Studio'),('studio-dotnet',['dotnet','test','Studio/tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj','-c','Release','--no-restore','--logger','trx;LogFileName=studio-dotnet.trx','--results-directory',str(out)],'.'),('workbench-build',['dotnet','build','Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj','-c','Release','--no-restore'],'.'),('studio-host-build',['dotnet','build','Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj','-c','Release','--no-restore'],'.'),('maps',['dotnet','run','--project','tools/maps/Elsa.Maps.Generator','-c','Release','--','check'],'.'),('tiers',['dotnet','run','--project','tools/maps/Elsa.Maps.Generator','-c','Release','--no-build','--','test-tiers-check'],'.')]
records=[]
for name,args,cwd in steps:
 assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==source
 print('starting',name,flush=True);start=time.time()
 with (out/(name+'.log')).open('w') as log:r=subprocess.run(args,cwd=root/cwd,stdout=log,stderr=subprocess.STDOUT)
 records.append({'name':name,'source':source,'arguments':args,'workingDirectory':cwd,'exitCode':r.returncode,'elapsedSeconds':time.time()-start});(out/'gate-results.json').write_text(json.dumps(records,indent=2)+'\n');print('finished',name,r.returncode,flush=True)
 if r.returncode:raise SystemExit(r.returncode)
