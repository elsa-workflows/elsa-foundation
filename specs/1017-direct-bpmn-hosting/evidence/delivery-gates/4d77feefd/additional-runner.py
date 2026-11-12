from pathlib import Path
import hashlib,json,os,re,subprocess,sys,xml.etree.ElementTree as ET
repo=Path.cwd();root=Path(__file__).parent;source=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
assert source=='4d77feefdd55132f167c072f2163f10201cad886'
assert not subprocess.check_output(['git','status','--porcelain'],text=True).strip()
env=dict(os.environ)
removed=[]
for key in list(env):
 if key.startswith('ELSA_RUNTIME_BOOKMARKS_EF_') and key.endswith('_TEST_CONNECTION_STRING') or key in ('ELSA_BPMN_HOST_EVIDENCE_DIRECTORY','ELSA_BPMN_HOST_SOURCE_REVISION','ELSA_BPMN_LEGACY_MATRIX_DIRECTORY','ELSA_BPMN_LEGACY_CAPTURE_DIRECTORY'):
  env.pop(key);removed.append(key)
env['ELSA_RUNTIME_PLACEMENT_EF_REQUIRE_NATIVE_PROVIDERS']='1'
projects = [('publishing', 'Essentials/tests/Workflows/Publishing/Tests/Elsa.Workflows.Publishing.Tests.csproj'), ('readability', 'Essentials/tests/Cluster/Readability/Tests/Elsa.Cluster.Readability.Tests.csproj'), ('behavioral', 'Essentials/tests/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj'), ('architecture', 'Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj')]
summary={'sourceRevision':source,'strictNativeProviders':True,'externalConnectionOverridesRemoved':removed,'projects':[]}
def save(): (root/'results.json').write_text(json.dumps(summary,indent=2)+'\n')
save()
print('START locked Release/Debug project graph restore',flush=True)
summary['restoreCommand']=['bash','tools/architecture/restore-ci-project-graph.sh','--locked-mode']
with (root/'restore.txt').open('w') as log:summary['restoreExitCode']=subprocess.call(summary['restoreCommand'],cwd=repo,env=env,stdout=log,stderr=subprocess.STDOUT)
save()
print('FINISH graph restore',summary['restoreExitCode'],flush=True)
if summary['restoreExitCode']:sys.exit(2)
for name,project in projects:
 out=root/name;out.mkdir(exist_ok=False)
 command=['dotnet','test',project,'-c','Release','--no-restore','--logger','trx;LogFileName=whole.trx','--results-directory',str(out)]
 print('START',name,flush=True)
 row={'name':name,'project':project,'testCommand':command};summary['projects'].append(row);save()
 with (out/'test.txt').open('w') as log:row['testExitCode']=subprocess.call(command,cwd=repo,env=env,stdout=log,stderr=subprocess.STDOUT)
 trx=out/'whole.trx'
 if trx.exists():
  ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};tree=ET.parse(trx)
  row['counters']=tree.find('t:ResultSummary/t:Counters',ns).attrib
  row['nonPassed']=[{'name':r.attrib['testName'],'outcome':r.attrib['outcome']} for r in tree.findall('t:Results/t:UnitTestResult',ns) if r.attrib['outcome']!='Passed']
  row['trxSha256']=hashlib.sha256(trx.read_bytes()).hexdigest()
 list_command=['dotnet','test',project,'-c','Release','--no-build','--no-restore','--list-tests']
 row['listCommand']=list_command
 with (out/'listed-tests.txt').open('w') as log:row['listExitCode']=subprocess.call(list_command,cwd=repo,env=env,stdout=log,stderr=subprocess.STDOUT)
 text=(out/'listed-tests.txt').read_text();row['listedTests']=[m.group(1) for line in text.splitlines() if (m:=re.match(r'^    (\S.*)$',line))]
 row['listedCount']=len(row['listedTests']);save()
 print('FINISH',name,'exit',row['testExitCode'],'counters',row.get('counters'),'listed',row['listedCount'],flush=True)
 if row['testExitCode'] or row['listExitCode']:sys.exit(2)
print('All whole affected-project commands finished',flush=True)
