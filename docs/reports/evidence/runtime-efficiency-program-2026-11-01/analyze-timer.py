import json,pathlib,sys,hashlib,statistics,collections
# Root holds raw/ and extra/ extracted from the committed timer-pg-r1 archive.
r=pathlib.Path(sys.argv[1]);led=json.loads((r/'raw/ledger.json').read_text());cases=json.loads((r/'extra/timer-pg-r1-cases.json').read_text())
issues=[]
if [x['label'] for x in led]!=[c['label'] for c in cases]:issues.append('ledger/case order or count mismatch')
groups=collections.defaultdict(list)
for x in led:
 res=r/'raw'/x['label']/'result.json';d=json.loads(res.read_text())
 if hashlib.sha256(res.read_bytes()).hexdigest()!=x['resultSha256']:issues.append(('hash',x['label']))
 a=d['After'];n=d['config']['Count']
 ok=x['exit']==0 and d['Correct'] and a['Completed']==n and a['Timers']==a['Bookmarks']==a['Queue']==a['Incidents']==a['PendingOutbox']==0 and a['Activities']==12*n and d['BodyCalls']==10*n and d['EmptySecondSweepKeptWorkflowAndActivityBytes'] and d['Before']['Timers']==d['Before']['Bookmarks']==n
 if not ok:issues.append(('incorrect',x['label']))
 c=d['config'];groups[(c['Provider'],c['Mode'],c['Degree'])].append(d)
hashes={json.dumps(x['binaryHashes'],sort_keys=True) for x in led}
summary={'runs':len(led),'expected':len(cases),'issues':issues,'binaryHashSets':len(hashes),'binaryHashes':led[0]['binaryHashes'],'groups':[]}
for (p,m,deg),ds in sorted(groups.items()):
 rates=[d['ResumesPerSecond'] for d in ds]
 summary['groups'].append({'provider':p,'mode':m,'degree':deg,'samples':len(ds),'resumesPerSecond':[round(v,2) for v in rates],'min':round(min(rates),2),'max':round(max(rates),2),
  'sweepSeconds':[round(d['Seconds'],2) for d in ds],'cpuMsPerResume':round(statistics.mean(d['CpuSeconds']/d['config']['Count'] for d in ds)*1000,1),
  'cpuCoresDuringSweep':round(statistics.mean(d['CpuSeconds']/d['Seconds'] for d in ds),2),'allocatedMBPerResume':round(statistics.mean(d['AllocatedBytesPerResume'] for d in ds)/1e6,2),'peakWorkingSetMB':round(max(d['PeakWorkingSet'] for d in ds)/1e6)})
print(json.dumps(summary,indent=1))
