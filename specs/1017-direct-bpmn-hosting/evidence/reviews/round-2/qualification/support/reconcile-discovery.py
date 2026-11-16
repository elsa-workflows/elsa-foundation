from pathlib import Path
from collections import Counter
import json,xml.etree.ElementTree as ET
r=Path(__file__).parent/'qualified-e3ec219fc';commands=json.loads((r/'commands.json').read_text());results=[]
for name in ['bpmn','runtime','hosting','runtime-ef','architecture','runtime-providers']:
 if not(r/(name+'-discovery.log')).exists():continue
 rows=(r/(name+'-discovery.log')).read_text().split('The following Tests are available:',1)[1].splitlines();listed=[x.strip()for x in rows if x.startswith('    ')and x.strip()];listing=Counter(x.split('(',1)[0]for x in listed)
 e=ET.parse(r/'results'/(name+'.trx')).getroot();ns={'t':e.tag.split('}')[0][1:]};c=e.find('.//t:Counters',ns).attrib;actual=Counter(x.attrib['testName'].split('(',1)[0]for x in e.findall('.//t:UnitTestResult',ns));missing=sorted(listing.keys()-actual.keys());unlisted=sorted(actual.keys()-listing.keys());assert not missing and not unlisted,(name,missing,unlisted);expansions=[]
 for method,count in listing.items():
  if actual[method]!=count:
   assert count==1 and method in listed and actual[method]>1,(name,method,count,actual[method]);expansions.append({'test':method,'listed':count,'executed':actual[method]})
 assert len(listed)+sum(x['executed']-x['listed']for x in expansions)==int(c['executed'])==int(c['passed'])and c['failed']=='0'and c['notExecuted']=='0'
 results.append({'project':name,'listed':len(listed),'executed':int(c['executed']),'passed':int(c['passed']),'skipped':0,'runtimeTheoryExpansions':expansions,'missingDiscoveries':missing,'unlistedExecutions':unlisted})
(r/'discovery-reconciliation.json').write_text(json.dumps({'sourceRevision':commands['sourceRevision'],'projects':results,'passed':sum(x['passed']for x in results),'skipped':0},indent=2)+'\n');print([(x['project'],x['listed'],x['executed'],len(x['runtimeTheoryExpansions']))for x in results])
