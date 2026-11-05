import json,datetime as dt,pathlib,re,sys,statistics
r=pathlib.Path(sys.argv[1]);mon=r/'m5-capacity-final-runner-evidence/raw';a=json.loads((r/'m5-capacity-final-analysis.json').read_text())
P=dt.datetime.fromisoformat
pg=[json.loads(l) for l in (mon/'postgres-samples.jsonl').read_text().splitlines() if l.strip()]
io=[];cur=None
for line in (mon/'iostat.log').read_text().splitlines():
 m=re.match(r'(\d\d)/(\d\d)/(\d\d) (\d\d:\d\d:\d\d)',line)
 if m:cur={'at':dt.datetime.fromisoformat(f'20{m[3]}-{m[1]}-{m[2]}T{m[4]}+00:00')};io.append(cur);continue
 if cur is None:continue
 f=line.split()
 if len(f)==6 and re.match(r'^[\d.]+$',f[0]) and 'cpu' not in cur:cur['cpu']=100-float(f[5]);cur['iowait']=float(f[3])
 if f and f[0]=='sda' and len(f)>20:cur['sdaWps']=float(f[7]);cur['sdaWkB']=float(f[8]);cur['sdaWawait']=float(f[11]);cur['sdaUtil']=float(f[-1])
rows={}
for w in ['w1','w2','w3','w4']:
 for x in json.loads((r/f'm5-capacity-final-{w}-evidence/raw/ledger.json').read_text()):rows.setdefault(x['case']['label'],[]).append(x)
out=[]
for o in a['results']:
 xs=rows[o['label']];s=max(P(x['sampleStart']) for x in xs);e=min(P(x['sampleEnd']) for x in xs)
 inside=[p for p in pg if s<=P(p['at'])<=e];p0,p1=inside[0],inside[-1];secs=(P(p1['at'])-P(p0['at'])).total_seconds()
 db=xs[0]['case']['database'];c=lambda p:next(d for d in p['databases'] if d['datname']==db)
 commits=(c(p1)['xact_commit']-c(p0)['xact_commit'])/secs
 wal=(p1['wal']['wal_bytes']-p0['wal']['wal_bytes'])/secs;sync=(p1['wal']['wal_sync']-p0['wal']['wal_sync'])/secs;syncms=(p1['wal']['wal_sync_time']-p0['wal']['wal_sync_time'])/secs
 ii=[q for q in io if s<=q['at']<=e and 'cpu' in q]
 out.append({**{k:o[k] for k in ['label','aggRate','p95max']},'dbCommitsPerS':round(commits,1),'commitsPerWorkflow':round(commits/o['aggRate'],1),'walKBPerS':round(wal/1024,1),'walSyncsPerS':round(sync,1),'walSyncMsPerS':round(syncms,1),
  'hostCpuPctMean':round(statistics.mean(q['cpu'] for q in ii),1),'hostCpuPctMax':round(max(q['cpu'] for q in ii),1),'sdaUtilMean':round(statistics.mean(q.get('sdaUtil',0) for q in ii),1),'sdaWawaitMean':round(statistics.mean(q.get('sdaWawait',0) for q in ii),2),'ioSamples':len(ii),'pgSamples':len(inside)})
(r/'m5-capacity-final-server.json').write_text(json.dumps(out,indent=1))
for x in out:print(x)
