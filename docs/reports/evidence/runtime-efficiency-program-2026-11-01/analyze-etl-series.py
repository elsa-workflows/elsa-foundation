from pathlib import Path
import json,hashlib,re
r=Path('/private/tmp/elsa-runtime-efficiency-program');root=r/'w3-etl-series-r1-evidence/raw';status=json.loads((root/'status.json').read_text());assert status['status']=='passed';rows=[]
for case in status['cases']:
 d=root/case['label'];result=json.loads((d/'output/result.json').read_text());cfg=result['config'];seen=set();chunks=[]
 for p in (d/'output/output').rglob('*.jsonl'):
  content=p.read_bytes();sha=hashlib.sha256(content).hexdigest();chunks.append({'key':p.name,'sha256':sha,'bytes':len(content)})
  for line in content.splitlines():
   row=json.loads(line);name=row['name'];assert row['category']=='one' and re.fullmatch(r'record-\d{6}-x{'+str(cfg['PayloadCharacters'])+'}',name) and name not in seen;seen.add(name)
 assert len(chunks)==result['ManifestEntries'] and len(seen)==cfg['Rows'] and all('record-'+str(i).zfill(6)+'-'+'x'*cfg['PayloadCharacters'] in seen for i in range(cfg['Rows']))
 rows.append({'case':case['label'],'attempts':cfg['Attempts'],'rows':cfg['Rows'],'partitions':cfg['Partitions'],'batch':cfg['Batch'],'payloadCharacters':cfg['PayloadCharacters'],'rate':result['RecordsPerSecond'],'seconds':result['Seconds'],'cpuSeconds':result['CpuSeconds'],'allocatedBytesPerRecord':result['AllocatedBytesPerRecord'],'peakWorkingSet':result['PeakWorkingSet'],'gc':result['GcCollections'],'owedNativeCallbacks':result['OwedNativeCallbacks'],'independentlyCheckedOutputRows':len(seen),'chunks':chunks})
summary={'libraryHead':status['libraryHead'],'worker':'elsa-eff-i67-w3','machine':'D2s_v6, 2 vCPU / 8 GiB, Linux x64 .NET10.0.8','provider':'SQLite on worker OS volume','scope':status['scope'],'coldWorkload':'Fresh process and first finite invocation per sample; host migration, synthetic input creation and compilation excluded; worker JIT/preparation/wakeup included. No diagnostics exporter.','totalIndependentlyCheckedRows':sum(x['rows'] for x in rows),'samples':rows,'caveat':'Two samples per attempt count; observed ranges, no confidence intervals or SLO. One owed native callback per standalone run. Not a clustered model or native activity comparison.'};(r/'m5-etl-summary.json').write_text(json.dumps(summary,indent=2));print('Independently checked',summary['totalIndependentlyCheckedRows'],'output rows and every collected output chunk SHA-256')
