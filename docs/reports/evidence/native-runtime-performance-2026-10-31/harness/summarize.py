import re as timestamp_re
import pathlib,json,re,statistics,collections,datetime

def parse_instant(value):
    value=timestamp_re.sub(r'\.(\d+)(?=[+-]\d{2}:\d{2}$)',lambda m:'.'+(m.group(1)+'000000')[:6],value)
    return datetime.datetime.fromisoformat(value)
p=pathlib.Path(__file__).resolve().parent
groups=collections.defaultdict(list)
for f in (p/'results').glob('*.json'):
 if not f.name.startswith(('baseline-','local-','scaling-','tuning-','capacity-','sqlite-scaling-','experiment-')):continue
 r=json.loads(f.read_text())
 if not r['Correct']:continue
 c=r['Config'];phase='sqlite-scaling' if f.name.startswith('sqlite-scaling-') else f.name.split('-')[0]
 variant='fast' if 'Variantfast' in c['Label'] else 'baseline'
 key=(c['Provider'],c['Mode'],c['Activities'],c['Concurrency'],c['External'],c['Cap'],c['ContextPooling'],phase,variant)
 groups[key].append(r)
summary=[]
for key,rs in groups.items():
 row=dict(zip(['provider','mode','leaves','concurrency','external','cap','contextPooling','phase','variant'],key))
 row['processes']=len(rs);row['samples']=sum(r['Count'] for r in rs)
 for field in ['WorkflowsPerSecond','MeanMs','P50Ms','P95Ms','P99Ms','AllocatedBytesPerWorkflow','CpuCoreEquivalent','ColdMs']:
  row[field]=statistics.median(r[field] for r in rs)
 row['CpuMsPerWorkflow']=statistics.median(r['CpuSeconds']*1000/r['Count'] for r in rs)
 row['throughputMin']=min(r['WorkflowsPerSecond'] for r in rs);row['throughputMax']=max(r['WorkflowsPerSecond'] for r in rs)
 row['commands']=sorted(set(r['Accounting']['Count'] for r in rs));row['transactions']=sorted(set(r['Accounting']['Commits'] for r in rs))
 row['checkpointCommits']=sorted(set(sum(q['Value']*len(re.findall(r'INSERT INTO [\"\[]?elsa_runtime_checkpoint_commit',q['Key'])) for q in r['Accounting']['Sql']) for r in rs))
 row['labels']=[r['Config']['Label'] for r in rs]
 summary.append(row)
summary.sort(key=lambda r:(r['phase'],r['provider'],r['leaves'],r['mode'],r['concurrency'],r['external'],r['cap'],r['contextPooling'],r['variant']))
(p/'summary.json').write_text(json.dumps(summary,indent=2))
lines=['| Phase | Variant | Provider | Mode | Leaves | C | External | Cap | Pool | Runs | Samples | WF/s | p50 ms | p95 ms | p99 ms | MiB/WF | SQL | Checkpoint commits | Transactions |','|---|---|---|---|---:|---:|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---|---|---|']
for r in summary:
 lines.append('| '+' | '.join(map(str,[r['phase'],r['variant'],r['provider'],r['mode'],r['leaves'],r['concurrency'],r['external'],r['cap'],r['contextPooling'],r['processes'],r['samples'],round(r['WorkflowsPerSecond'],2),round(r['P50Ms'],2),round(r['P95Ms'],2),round(r['P99Ms'],2),round(r['AllocatedBytesPerWorkflow']/1048576,2),r['commands'],r['checkpointCommits'],r['transactions']]))+' |')
(p/'tables.md').write_text('\n'.join(lines)+'\n')
print('\n'.join(lines))
clusters=[]
for f in p.glob('cluster-*.environment.json'):
 env=json.loads(f.read_text())
 if env['contaminated'] or any(env['exits']):continue
 rs=[json.loads((p/'results'/(label+'.json')).read_text()) for label in env['labels']]
 if not all(r['Correct'] for r in rs):continue
 start=min(parse_instant(r['measuredStartedAt']) for r in rs)
 end=max(parse_instant(r['measuredEndedAt']) for r in rs)
 samples=sorted(s for r in rs for s in r['SamplesMs'])
 clusters.append(dict(nodes=env['nodes'],concurrency=env['concurrency'],repeat=env['repeat'],
  count=len(samples),seconds=(end-start).total_seconds(),WorkflowsPerSecond=len(samples)/(end-start).total_seconds(),
  P95Ms=samples[max(0,__import__('math').ceil(.95*len(samples))-1)],
  AllocatedBytesPerWorkflow=sum(r['Count']*r['AllocatedBytesPerWorkflow'] for r in rs)/len(samples),
  database=env['database'],labels=env['labels']))
(p/'cluster-summary.json').write_text(json.dumps(clusters,indent=2))
