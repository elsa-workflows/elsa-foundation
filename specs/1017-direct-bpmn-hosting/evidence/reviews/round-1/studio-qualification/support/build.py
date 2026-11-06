from pathlib import Path
import json,subprocess
repo=Path('/Users/sipke/.codex/worktrees/f236/elsa-foundation');root=Path(__file__).parent;source='cb0354a5b507271b67c5923a700d88f950975e25'
assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip()==source
assert not subprocess.check_output(['git','status','--porcelain'],cwd=repo,text=True).strip()
jobs=[('install',['pnpm','install','--frozen-lockfile'],repo/'Studio'),('browser-build',['pnpm','-r','build'],repo/'Studio'),('studio-host',['dotnet','build','Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj','-c','Release','-p:RestoreLockedMode=true'],repo),('standard-hosts',['dotnet','build','Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj','-c','Release','-p:ElsaBpmnRecoveryProbe=false'],repo)]
results=[]
for name,cmd,cwd in jobs:
 assert subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip()==source
 with(root/(name+'.txt')).open('w')as log:code=subprocess.call(cmd,cwd=cwd,stdout=log,stderr=subprocess.STDOUT)
 results.append({'name':name,'command':cmd,'cwd':str(cwd),'exitCode':code});(root/'build.json').write_text(json.dumps({'sourceRevision':source,'results':results},indent=2)+'\n');print(name,code,flush=True)
 if code:raise SystemExit(code)
