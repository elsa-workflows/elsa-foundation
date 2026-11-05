from pathlib import Path
import datetime,json,os,subprocess,xml.etree.ElementTree as ET
root=Path(__file__).parent/'qualified-e3ec219fc';source=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip();assert source=='e3ec219fcc3a93c949d2669fc585bd1e79152abe';env=os.environ.copy();keys=['ELSA_RUNTIME_BOOKMARKS_EF_POSTGRESQL_TEST_CONNECTION_STRING','ELSA_RUNTIME_BOOKMARKS_EF_SQLSERVER_TEST_CONNECTION_STRING','ELSA_RUNTIME_BOOKMARKS_EF_MYSQL_TEST_CONNECTION_STRING'];configured=[k for k in keys if k in env]
for k in keys:env.pop(k,None)
env['ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS']='1';assert not any(k in env for k in keys)
records=[]
for name,args in [('runtime-providers',['dotnet','test','Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj','-c','Release','--logger','trx;LogFileName=runtime-providers.trx','--results-directory',str(root/'results'),'-p:RestoreLockedMode=true','--nologo','--verbosity','quiet']),('runtime-providers-discovery',['dotnet','test','Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/ProviderTests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests.csproj','-c','Release','--no-build','--list-tests','--nologo','--verbosity','quiet'])]:
 started=datetime.datetime.now(datetime.timezone.utc).isoformat()
 with (root/(name+'.log')).open('w')as f:p=subprocess.run(args,env=env,stdout=f,stderr=subprocess.STDOUT)
 v={'name':name,'args':args,'startedAtUtc':started,'exitCode':p.returncode};file=root/'results'/(name+'.trx')
 if file.exists():
  e=ET.parse(file).getroot();ns={'t':e.tag.split('}')[0][1:]};v['counters']=e.find('.//t:Counters',ns).attrib
 records.append(v);(root/'provider-commands.json').write_text(json.dumps({'sourceRevision':source,'requiredNativeProviders':True,'connectionOverrideNamesRemoved':keys,'configuredOverrideNamesBeforeRemoval':configured,'testOwnedContainersOnly':True,'results':records},indent=2)+'\n');print(v,flush=True)
 if p.returncode!=0:raise SystemExit(p.returncode)
