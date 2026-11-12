from pathlib import Path
import xml.etree.ElementTree as E,json,collections
r=Path('/tmp/elsa-2898-main-c4cfa-qualification');ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};out=[]
for row in json.loads((r/'discovery/results.json').read_text()):
 p=r/(row['name']+'.trx')
 if not p.exists():continue
 root=E.parse(p).getroot();results=root.findall('t:Results/t:UnitTestResult',ns);discovered=row['tests'];actual=[x.attrib['testName'] for x in results]
 base=lambda s:s.split('(',1)[0]
 dg=collections.Counter(base(s) for s in discovered);ag=collections.Counter(base(s) for s in actual)
 missing=sorted(set(dg)-set(ag));extra=sorted(set(ag)-set(dg));assert not missing and not extra,(row['name'],missing,extra)
 outcomes=dict(collections.Counter(x.attrib['outcome'] for x in results));skips=[{'name':x.attrib['testName'],'message':x.findtext('t:Output/t:ErrorInfo/t:Message',namespaces=ns)} for x in results if x.attrib['outcome']!='Passed']
 out.append({'name':row['name'],'source':row['source'],'discoveredDisplayEntries':len(discovered),'runtimeRows':len(actual),'methodGroups':len(dg),'missingMethodGroups':missing,'unexpectedMethodGroups':extra,'outcomes':outcomes,'nonPassed':skips,'theoryExpansion':[{'method':s,'discoveredEntries':dg[s],'runtimeRows':ag[s]} for s in dg if dg[s]!=ag[s]]})
(r/'test-reconciliation.json').write_text(json.dumps(out,indent=2)+'\n')
print('reconciled',len(out),'projects')
for x in out:print(x['name'],x['discoveredDisplayEntries'],x['runtimeRows'],x['outcomes'],'expandedGroups',len(x['theoryExpansion']))
