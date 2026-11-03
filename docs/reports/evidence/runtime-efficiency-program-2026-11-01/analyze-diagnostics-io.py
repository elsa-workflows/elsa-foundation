from pathlib import Path
import json,datetime,re,statistics,collections
r=Path('/private/tmp/elsa-runtime-efficiency-program');root=r/'azure-diagnostics-six-cases-r1/raw';rows=[]
for entry in json.loads((root/'diagnostics-qualification-attestation.json').read_text())['cases']:
 case=entry['case'];folder=root/case['label'];blocks=[];block=None;headers=None;cpu_pending=False
 for line in (folder/'iostat.log').read_text().splitlines():
  if re.fullmatch(r'\d\d/\d\d/\d\d \d\d:\d\d:\d\d',line):
   block={'at':datetime.datetime.strptime(line,'%m/%d/%y %H:%M:%S').replace(tzinfo=datetime.timezone.utc),'devices':{}};blocks.append(block)
  elif line.startswith('avg-cpu:'):cpu_pending=True
  elif cpu_pending and line.strip():
   block['iowait']=float(line.split()[3]);cpu_pending=False
  elif line.startswith('Device '):headers=line.split()[1:]
  elif block and headers and line.startswith(('sda ','sdb ')):
   v=line.split();block['devices'][v[0]]=dict(zip(headers,map(float,v[1:])))
 # The first report is a lifetime average, not a one-second interval.
 blocks=blocks[1:]
 for step in json.loads((folder/'capture.json').read_text())['steps']:
  start=datetime.datetime.fromisoformat(step['measurementStartedAtUtc'].replace('Z','+00:00'));end=datetime.datetime.fromisoformat(step['measurementEndedAtUtc'].replace('Z','+00:00'));sample=[b for b in blocks if start<b['at']<=end];waits=collections.Counter()
  for s in step['samples']:
   for wait in s.get('postgres',{}).get('waits',[]):waits[wait['key']]+=wait['count']
  devices={}
  for device in ['sda','sdb']:
   ss=[s['devices'][device] for s in sample if device in s['devices']];devices[device]={key:round(statistics.mean(x[key] for x in ss),3) for key in ['w/s','wkB/s','w_await','f/s','f_await','aqu-sz','%util']}
  rows.append({'case':case['label'],'mode':case['mode'],'diagnostics':case['diagnostics'],'placement':case['placement'],'clients':step['clients'],'rate':step['successfulExecutionsPerSecond'],'samples':len(sample),'meanCpuIowaitPercent':round(statistics.mean(b['iowait'] for b in sample),3),'devices':devices,'postgresObservedWaitSamples':dict(waits),'ungrantedLocksMax':max(s.get('postgres',{}).get('ungrantedLocks',0) for s in step['samples'])})
(r/'m5-diagnostics-io.json').write_text(json.dumps({'note':'UTC-aligned one-second iostat intervals; first lifetime report excluded. Device values are arithmetic means, not operation-weighted latency. Sampled waits are observations, not durations.','windows':rows},indent=2))
for c in sorted(set(x['case'] for x in rows)):
 peak=max((x for x in rows if x['case']==c),key=lambda x:x['rate']);print(json.dumps(peak))
