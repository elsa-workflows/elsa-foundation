from pathlib import Path
import json,subprocess,xml.etree.ElementTree as ET
root=Path(__file__).parent;results=[]
for name,project,selected in [('bpmn','Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj','FullyQualifiedName~Accepted_start_replays_after_publication_retirement|FullyQualifiedName~Human_work_inspection_wraps|FullyQualifiedName~A_refused_construct_names'),('ef','Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj','FullyQualifiedName~Model_read_provider_failures_retain_context_and_cancellation')]:
 args=['dotnet','test',project,'-c','Release','--filter',selected,'--logger','trx;LogFileName='+name+'.trx','--results-directory',str(root/'red'),'-p:RestoreLockedMode=true','--nologo','--verbosity','quiet']
 with (root/(name+'-red.log')).open('w')as log:p=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT)
 item={'name':name,'args':args,'exitCode':p.returncode};trx=root/'red'/(name+'.trx')
 if trx.exists():
  e=ET.parse(trx).getroot();ns={'t':e.tag.split('}')[0][1:]};item['counters']=e.find('.//t:Counters',ns).attrib
 results.append(item);(root/'red.json').write_text(json.dumps({'productionSource':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),'uncommittedChanges':'New regressions only; production files unchanged from reviewedbbb21.','results':results},indent=2)+'\n');print(name,item,flush=True)
