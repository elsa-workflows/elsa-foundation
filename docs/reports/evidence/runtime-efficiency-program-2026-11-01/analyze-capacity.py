import json,hashlib,datetime as dt,collections,pathlib,statistics,sys
# Root holds m5-capacity-final-<host>-evidence/ directories extracted from the committed archives.
r=pathlib.Path(sys.argv[1])
P=lambda s:dt.datetime.fromisoformat(s)
plan=json.loads((r/'m5-capacity-final-w1-evidence/extra/capacity-13waves-r1-plan.json').read_text())
rows=collections.defaultdict(list);issues=[];hashes=set()
for w in ['w1','w2','w3','w4']:
 d=r/f'm5-capacity-final-{w}-evidence';led=json.loads((d/'raw/ledger.json').read_text())
 assert (d/'raw/finished').read_text().strip()=='passed'
 for x in led:
  lab=x['case']['label'];rows[lab].append(x)
  res=d/'raw'/f'{lab}-{w}'/f'{lab}-{w}.json'
  if hashlib.sha256(res.read_bytes()).hexdigest()!=x['resultSha256']:issues.append(('resultHash',lab,w))
  if not(x['exit']==0 and x['correct'] and x['error'] is None if 'error' in x else x['exit']==0 and x['correct']):issues.append(('notCorrect',lab,w))
  if x['totalRuns']!=x['distinctRunIds']:issues.append(('dupIds',lab,w))
  if x['worker']!=w:issues.append(('worker',lab,w))
  hashes.add(json.dumps(x['binaryHashes'],sort_keys=True))
waves=plan['waves'] if isinstance(plan,dict) and 'waves' in plan else plan
out=[];allEnds=[];allVals=[]
for lab,xs in rows.items():
 c=xs[0]['case'];exp=sorted(c['workers']);got=sorted(x['worker'] for x in xs)
 if exp!=got:issues.append(('participants',lab,exp,got))
 st=[P(x['sampleStart']) for x in xs];en=[P(x['sampleEnd']) for x in xs];val=P(xs[0]['validationReleaseAtUtc'])
 if any(e>=val for e in en):issues.append(('validationBeforeEnd',lab))
 if len({x['validationReleaseAtUtc'] for x in xs})!=1:issues.append(('valMismatch',lab))
 ov=(min(en)-max(st)).total_seconds();span=(max(en)-min(st)).total_seconds()
 out.append(dict(label=lab,mode=c['mode'],acts=c['activities'],nodes=len(xs),pooling=c['pooling'],conc=c['concurrency'],start=c['startAtUtc'][11:19],
  aggRate=round(sum(x['workflowsPerSecond'] for x in xs),2),perNode=[round(x['workflowsPerSecond'],2) for x in xs],count=sum(x['count'] for x in xs),
  p95max=round(max(x['p95Ms'] for x in xs),1),overlapS=round(ov,3),spanS=round(span,3),startSkewS=round((max(st)-min(st)).total_seconds(),4),
  slackToValS=round((val-max(en)).total_seconds(),2),cpu=[round(x['cpuCoreEquivalent'],2) for x in xs],allocMB=round(statistics.mean(x['allocatedBytesPerWorkflow'] for x in xs)/1e6,2)))
out.sort(key=lambda o:o['start'])
mon=r/'m5-capacity-final-runner-evidence/raw';ms=json.loads((mon/'status.json').read_text())
first=min(P(x['sampleStart']) for xs in rows.values() for x in xs);last=max(P(x['validationReleaseAtUtc']) for xs in rows.values() for x in xs)
samples=[json.loads(l) for l in (mon/'postgres-samples.jsonl').read_text().splitlines() if l.strip()]
summary=dict(waves=len(out),expectedWaves=13,workerRows=sum(len(v) for v in rows.values()),binaryHashSets=len(hashes),issues=issues,monitor=ms,pgSampleRows=len(samples),pgSampleKeys=sorted(samples[0].keys())[:20],firstSampleStart=str(first),lastValidation=str(last),results=out)
(r/'m5-capacity-final-analysis.json').write_text(json.dumps(summary,indent=1,default=str))
for o in out:print(o)
print({k:v for k,v in summary.items() if k!='results'})
