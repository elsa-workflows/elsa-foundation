from pathlib import Path
import json, hashlib, math, collections, datetime, re
p=Path(__file__).resolve().parent
prefixes=('baseline-','experiment-','scaling-','tuning-','capacity-','sqlite-scaling-','cluster-','local-')
ledgers={}
for f in p.glob('*.progress.jsonl'):
 for line in f.read_text().splitlines():
  e=json.loads(line)
  if 'label' in e:ledgers[e['label']]=e
  for label in e.get('labels',[]):ledgers[label]=e
def instant(x):
 return datetime.datetime.fromisoformat(re.sub(r'\.(\d+)(?=[+-]\d{2}:\d{2}$)',lambda m:'.'+(m.group(1)+'000000')[:6],x)).timestamp()
checks=[];pending=[];counts=collections.Counter();total=0
for f in sorted((p/'results').glob('*.json')):
 if not f.name.startswith(prefixes):continue
 r=json.loads(f.read_text());c=r['Config'];label=c['Label'];n=r['Count'];total+=n
 phase='sqlite-scaling' if label.startswith('sqlite-scaling-') else label.split('-')[0];counts[phase]+=1
 assert r['Correct'] and n>0 and not r['Errors'],label
 assert len(r['SamplesMs'])==n and r['SamplesMs']==sorted(r['SamplesMs']),label
 assert r['measuredLeaves']==n*c['Activities'],label
 assert abs(r['WorkflowsPerSecond']-n/r['Seconds'])<1e-9,label
 assert r['Queued']==r['Incidents']==r['PendingOutbox']==0,label
 assert r['Runtime']=='.NET 10.0.13' and r['ServerGC'] and r['ProcessorCount']==15,label
 for pct,key in [(.5,'P50Ms'),(.95,'P95Ms'),(.99,'P99Ms')]:
  assert r['SamplesMs'][math.ceil(pct*n)-1]==r[key],(label,key)
 e=ledgers.get(label)
 if not e:pending.append(label);continue
 assert not e['contaminated'],label
 if e.get('policy')=='timed-window-v3':
  begin=instant(r['measuredStartedAt'])-2;end=instant(r['measuredEndedAt'])+2
  load=e.get('loadSamples',e.get('load',[]))
  samples=[x for x in load if begin<=x['at']<=end]
  assert len(samples)>=2,(label,'No load coverage')
  streak=0
  for sample in samples:
   streak=streak+1 if sample['externalCpu']>500 else 0
   assert streak<2,(label,'Load policy violation')
 if phase=='cluster':
  assert not any(e['exits']),label
  assert r['database']==e['database'],label
 else:
  assert e['exit']==0 and e['Correct'],label
  assert e['Count']==n and abs(e['WorkflowsPerSecond']-r['WorkflowsPerSecond'])<1e-9,label
 checks.append(label)
expected={'baseline':24,'experiment':8,'scaling':11,'tuning':6,'capacity':3,'sqlite-scaling':2,'cluster':6,'local':12}
failed={label:e for label,e in ledgers.items() if label.startswith('local-') and e.get('exit',0)!=0 and e.get('Correct') is False and not e.get('contaminated')}
attempted=dict(counts)
attempted['local']=attempted.get('local',0)+len(failed)
out={'completionFailureLabels':sorted(failed),'attemptedCounts':attempted,'qualifiedResultsAudited':len(checks),'pendingLedger':pending,'counts':dict(counts),'expected':expected,'complete':attempted==expected and not pending,'measuredCompletedWorkflows':total}
(p/'audit.json').write_text(json.dumps(out,indent=2));print(json.dumps(out,indent=2))
