from pathlib import Path
import json,subprocess,xml.etree.ElementTree as ET
root=Path(__file__).parent;results=[]
for name,project,selected in [('bpmn','Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj','FullyQualifiedName~Accepted_start_replays_after_publication_retirement|FullyQualifiedName~Human_work_inspection_wraps|FullyQualifiedName~A_refused_construct_names'),('ef','Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj','FullyQualifiedName~Model_read_provider_failures_retain_context_and_cancellation')]:
 args=['dotnet','test',project,'-c','Release','--filter',selected,'--logger','trx;LogFileName='+name+'.trx','--results-directory',str(root/'green'),'-p:RestoreLockedMode=true','--nologo','--verbosity','quiet']
 with (root/(name+'-green.log')).open('w')as log:p=subprocess.run(args,stdout=log,stderr=subprocess.STDOUT)
 item={'name':name,'args':args,'exitCode':p.returncode};trx=root/'green'/(name+'.trx')
 if trx.exists():
  e=ET.parse(trx).getroot();ns={'t':e.tag.split('}')[0][1:]};item['counters']=e.find('.//t:Counters',ns).attrib
 results.append(item);(root/'green.json').write_text(json.dumps({'productionSource':subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),'uncommittedChanges':'Development tree with round2 corrections; not exact committed-source qualification.','results':results},indent=2)+'\n');print(name,item,flush=True)
