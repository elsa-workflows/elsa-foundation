from pathlib import Path
import json,datetime
p=Path(__file__).resolve().parent;rows=[]
for f in sorted((p/'results').glob('*.json')):
 r=json.loads(f.read_text())
 if not r.get('Correct') or r.get('Config',{}).get('Provider')!='Sqlite' or not f.name.startswith(('local-','sqlite-scaling-')):continue
 db=p/'results'/(r['database']+'.db')
 with db.open('rb') as h:header=h.read(100)
 assert header[:16]==b'SQLite format 3\x00'
 rows.append({'label':r['Config']['Label'],'database':r['database'],'fileBytes':db.stat().st_size,'writeVersionByte18':header[18],'readVersionByte19':header[19],'journalModeFromHeader':'WAL' if header[18:20]==b'\x02\x02' else 'rollback','pageSize':int.from_bytes(header[16:18],'big'),'headerHex':header.hex()})
(p/'sqlite-database-settings.json').write_text(json.dumps({'observedAt':datetime.datetime.now(datetime.timezone.utc).isoformat(),'method':'Read 100-byte file headers after probe exit. Header versions 2/2 indicate WAL. No assertion about original connection-local synchronous setting; native library compile defaults separately captured.','reference':'https://sqlite.org/fileformat.html#file_format_version_numbers','databases':rows},indent=2));print('Captured',len(rows),'SQLite headers:',[r['journalModeFromHeader'] for r in rows])
