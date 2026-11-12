from pathlib import Path
import subprocess,json,os,time,hashlib
root=Path('/Users/sipke/.codex/worktrees/f236/elsa-foundation');out=Path('/tmp/elsa-2898-post-owner-main-ab46-qualification');source='25148ea9c3565afdb27774f4af9a5155a95d6c5c'
assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip()==source
steps=[
 ('frontend-tests',['python3.14','/tmp/elsa-2898-serial-frontend.py'],'.',{}),
 ('typecheck',['pnpm','typecheck'],'Studio',{}),
 ('lint',['pnpm','lint'],'Studio',{}),
 ('frontend-build',['pnpm','build'],'Studio',{}),
 ('readability',['dotnet','test','Essentials/tests/Cluster/Readability/Tests/Elsa.Cluster.Readability.Tests.csproj','-c','Release','--logger','trx;LogFileName=readability.trx','--results-directory',str(out)],'.',{}),
 ('modularity',['dotnet','test','Essentials/tests/Modularity/Tests/Elsa.Modularity.Tests.csproj','-c','Release','--logger','trx;LogFileName=modularity.trx','--results-directory',str(out)],'.',{}),
 ('publishing-api',['dotnet','test','Essentials/tests/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj','-c','Release','--logger','trx;LogFileName=publishing-api.trx','--results-directory',str(out)],'.',{}),
 ('studio',['dotnet','test','Studio/tests/Elsa.Studio.Tests/Elsa.Studio.Tests.csproj','-c','Release','--logger','trx;LogFileName=studio.trx','--results-directory',str(out)],'.',{}),
 ('architecture',['dotnet','test','Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj','-c','Release','--logger','trx;LogFileName=architecture.trx','--results-directory',str(out)],'.',{}),
 ('host-new-main',['dotnet','test','Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj','-c','Release','--filter','FullyQualifiedName~LegacyTimerAdmissionWorkbenchTests','--logger','trx;LogFileName=host-new-main.trx','--results-directory',str(out)],'.',{'ELSA_BPMN_HOST_EVIDENCE_DIRECTORY':str(out/'host'),'ELSA_BPMN_HOST_SOURCE_REVISION':source}),
 ('studio-host-build',['dotnet','build','Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj','-c','Release'],'.',{}),
 ('maps',['dotnet','run','--project','tools/maps/Elsa.Maps.Generator','-c','Release','--','check'],'.',{}),
 ('tiers',['dotnet','run','--project','tools/maps/Elsa.Maps.Generator','-c','Release','--no-build','--','test-tiers-check'],'.',{}),
 ('tier-docs',['python3','tools/ci/test_tiers.py','check-docs'],'.',{}),
 ('tier-workflows',['python3','tools/ci/test_tiers.py','check-workflows'],'.',{})]
results=[]
for name,args,cwd,env in steps:
 start=time.time();print('starting',name,flush=True)
 with (out/(name+'.log')).open('w') as f:p=subprocess.run(args,cwd=root/cwd,stdout=f,stderr=subprocess.STDOUT,env={**os.environ,**env})
 results.append({'name':name,'arguments':args,'workingDirectory':cwd,'source':source,'exitCode':p.returncode,'elapsedSeconds':time.time()-start,'log':name+'.log','logSha256':hashlib.sha256((out/(name+'.log')).read_bytes()).hexdigest()})
 (out/'gate-results.json').write_text(json.dumps(results,indent=2)+'\n');print('finished',name,p.returncode,flush=True)
 if p.returncode:raise SystemExit(p.returncode)
