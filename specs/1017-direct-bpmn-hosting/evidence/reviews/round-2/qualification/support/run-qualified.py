from pathlib import Path
import datetime,json,os,subprocess,xml.etree.ElementTree as ET
repo=Path.cwd();root=Path(__file__).parent/'qualified-e3ec219fc';root.mkdir(exist_ok=True);source=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip();assert source=='e3ec219fcc3a93c949d2669fc585bd1e79152abe';assert not subprocess.check_output(['git','status','--porcelain'],text=True).strip();results=[]
def run(name,args,env=None):
 start=datetime.datetime.now(datetime.timezone.utc).isoformat()
 with(root/(name+'.log')).open('w')as log:r=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT,env=env)
 v={'name':name,'args':args,'startedAtUtc':start,'exitCode':r.returncode};trx=root/'results'/(name+'.trx')
 if trx.exists():
  e=ET.parse(trx).getroot();n={'t':e.tag.split('}')[0][1:]};v['counters']=e.find('.//t:Counters',n).attrib
 results.append(v);(root/'commands.json').write_text(json.dumps({'sourceRevision':source,'results':results},indent=2)+'\n');print(name,v,flush=True);return r.returncode
projects=[('bpmn','Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj'),('runtime','Essentials/tests/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj'),('hosting','Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj'),('runtime-ef','Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj'),('architecture','Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj')]
for name,project in projects:
 env=os.environ.copy()
 if name=='hosting':env.update(ELSA_BPMN_HOST_EVIDENCE_DIRECTORY=str(root/'hosting-evidence'),ELSA_BPMN_HOST_SOURCE_REVISION=source)
 if name=='runtime-ef':
  if run('docker-ready',['docker','info','--format','{{.ServerVersion}}'])!=0:raise SystemExit('Container runtime unavailable; no provider pass claimed.')
  for key in ['ELSA_RUNTIME_BOOKMARKS_EF_POSTGRES_CONNECTION_STRING','ELSA_RUNTIME_BOOKMARKS_EF_SQLSERVER_CONNECTION_STRING','ELSA_RUNTIME_BOOKMARKS_EF_MYSQL_CONNECTION_STRING']:env.pop(key,None)
  env['ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS']='1'
 if name=='architecture'and run('architecture-project-graph',['bash','tools/architecture/restore-ci-project-graph.sh'])!=0:raise SystemExit(1)
 args=['dotnet','test',project,'-c','Release','--logger','trx;LogFileName='+name+'.trx','--results-directory',str(root/'results'),'-p:RestoreLockedMode=true','--nologo','--verbosity','quiet']
 if name=='hosting':args+=['-p:ElsaBpmnRecoveryProbe=false']
 code=run(name,args,env)
 if code!=0:raise SystemExit(code)
 listargs=['dotnet','test',project,'-c','Release','--no-build','--list-tests','--nologo','--verbosity','quiet']
 if run(name+'-discovery',listargs,env)!=0:raise SystemExit(1)
for name,args in [('maps',['dotnet','run','--project','tools/maps/Elsa.Maps.Generator','-c','Release','--','check']),('tiers',['dotnet','run','--project','tools/maps/Elsa.Maps.Generator','-c','Release','--','test-tiers-check'])]:
 if run(name,args)!=0:raise SystemExit(1)
print('Qualified source gates finished.',flush=True)
