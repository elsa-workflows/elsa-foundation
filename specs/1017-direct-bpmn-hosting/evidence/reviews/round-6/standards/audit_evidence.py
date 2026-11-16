#!/usr/bin/env python3
import gzip,hashlib,json,pathlib,subprocess,xml.etree.ElementTree as ET
repo=pathlib.Path('/Users/sipke/.codex/worktrees/f236/elsa-foundation')
out=pathlib.Path('/tmp/elsa-2898-full-pr-r6-standards')
evidence=repo/'specs/1017-direct-bpmn-hosting/evidence'
selected=[evidence/'baseline/manifest.json',evidence/'reviews/round-5/qualification/manifest.json',evidence/'reviews/round-5/qualification/studio/manifest.json',evidence/'reviews/round-5/frontend-adoption/manifest.json']
results=[]
for mf in selected:
 data=json.loads(mf.read_text());errors=[];decoded=0
 for a in data['assets']:
  p=(mf.parent/a['path']).resolve()
  if not p.is_relative_to(mf.parent.resolve()):errors.append({'path':a['path'],'error':'path escapes bundle'});continue
  b=p.read_bytes();h=hashlib.sha256(b).hexdigest()
  if h!=a['sha256']:errors.append({'path':a['path'],'error':'sha256 mismatch','actual':h,'expected':a['sha256']})
  if 'bytes' in a and len(b)!=a['bytes']:errors.append({'path':a['path'],'error':'byte length mismatch'})
  expected=a.get('decodedSha256') or a.get('databaseSha256')
  if expected:
   d=gzip.decompress(b) if a.get('encoding')=='gzip' or p.suffix=='.gz' else b;decoded+=1
   if hashlib.sha256(d).hexdigest()!=expected:errors.append({'path':a['path'],'error':'decoded sha256 mismatch'})
   size=a.get('decodedBytes') or a.get('uncompressedBytes')
   if size is not None and len(d)!=size:errors.append({'path':a['path'],'error':'decoded byte length mismatch'})
 results.append({'manifest':str(mf.relative_to(repo)),'assets':len(data['assets']),'decodedChecks':decoded,'errors':errors})
# Reconcile raw executed TRX to declared whole-suite and discovery receipts.
g=evidence/'reviews/round-5/qualification/gates'
source=json.loads((g/'source-and-counts.json').read_text());discovery=json.loads((g/'discovery/reconciliation.json').read_text());trx=[]
for suite in source['wholeSuites']:
 p=g/suite['trx'];r=ET.fromstring(gzip.decompress(p.read_bytes()));ns={'x':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
 counters=r.find('x:ResultSummary/x:Counters',ns).attrib;errors=[]
 if counters!=suite['counters']:errors.append('receipt counters differ from raw TRX')
 entries=r.findall('x:Results/x:UnitTestResult',ns);statuses={}
 for e in entries:statuses[e.attrib['outcome']]=statuses.get(e.attrib['outcome'],0)+1
 d=next(x for x in discovery['projects'] if x['name']==suite['name'] or x['name']=='hosting' and suite['name']=='standard-host')
 if d['missingMethods'] or d['extraMethods']:errors.append('declared method reconciliation differs')
 trx.append({'suite':suite['name'],'executingSource':source['executingSource'],'rawCounters':counters,'rawResultCount':len(entries),'outcomes':statuses,'listedCaseCount':d['listedCaseCount'],'missingMethods':d['missingMethods'],'extraMethods':d['extraMethods'],'errors':errors})
# Compare source trees to the reviewed head, not merely to a receipt assertion.
head='d1449c0bff679212f8a64766a95b1c29cfadbcea';base='d481d6a5e624e35bba9fbf32033ed02aff64cbef';old=source['executingSource']
def tree(ref,path):return subprocess.check_output(['git','rev-parse',f'{ref}:{path}'],cwd=repo,text=True).strip()
paths=['Essentials','Extensions','Directory.Build.props','Directory.Packages.props','tools/bpmn','e2e-tests/bpmn','tools/ef','tools/ci/test-tiers.json']
equality=[{'path':p,'qualified':tree(old,p),'reviewed':tree(head,p),'equal':tree(old,p)==tree(head,p)} for p in paths]
report={'head':head,'base':base,'auditKind':'read-only artifact/source/discovery verification; no host or suite rerun','manifests':results,'wholeSuites':trx,'rawTotal':sum(int(x['rawCounters']['passed']) for x in trx),'sourceEquivalence':equality}
(out/'evidence-audit.json').write_text(json.dumps(report,indent=2)+'\n')
print(json.dumps({'manifests':results,'suites':[{'name':t['suite'],'executed':t['rawResultCount'],'outcomes':t['outcomes'],'errors':t['errors']} for t in trx],'total':report['rawTotal'],'allSourceTreesEqual':all(x['equal'] for x in equality)},indent=2))
