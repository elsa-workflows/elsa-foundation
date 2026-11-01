from pathlib import Path
import json,subprocess,xml.etree.ElementTree as ET
r=Path(__file__).parent;args=['dotnet','test','Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj','-c','Release','--filter','FullyQualifiedName~InstantBpmnWorkbenchTests&DisplayName~Root','--logger','trx;LogFileName=instant.trx','--results-directory',str(r/'red'),'-p:RestoreLockedMode=true','-p:ElsaBpmnRecoveryProbe=false','--nologo','--verbosity','quiet']
with (r/'instant-red.log').open('w')as f:p=subprocess.run(args,stdout=f,stderr=subprocess.STDOUT)
v={'args':args,'exitCode':p.returncode,'productionSource':'0d9ba38563abaf8af3e838f6b15e622e40adb281','changes':'Uncommitted regressions only; production equals reviewedbbb21.'};file=r/'red/instant.trx'
if file.exists():
 e=ET.parse(file).getroot();n={'t':e.tag.split('}')[0][1:]};v['counters']=e.find('.//t:Counters',n).attrib
(r/'instant-red.json').write_text(json.dumps(v,indent=2)+'\n');print(v,flush=True)
