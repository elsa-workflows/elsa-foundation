#!/usr/bin/env python3
import pathlib,json,gzip,tempfile,sqlite3,hashlib
repo=pathlib.Path('/Users/sipke/.codex/worktrees/f236/elsa-foundation')
e=repo/'specs/1017-direct-bpmn-hosting/evidence'
roots=[e/'baseline/manifest.json',e/'reviews/round-5/qualification/manifest.json',e/'reviews/round-5/qualification/studio/manifest.json',e/'reviews/round-5/frontend-adoption/manifest.json']
paths=set()
for m in roots:
 for a in json.loads(m.read_text())['assets']:
  p=m.parent/a['path']
  if p.name.endswith(('.sqlite','.sqlite.gz')):paths.add(p.resolve())
result=[]
with tempfile.TemporaryDirectory(prefix='sqlite-readonly-',dir='/tmp/elsa-2898-full-pr-r6-standards') as t:
 for i,p in enumerate(sorted(paths)):
  data=gzip.decompress(p.read_bytes()) if p.suffix=='.gz' else p.read_bytes()
  copy=pathlib.Path(t)/f'{i}.sqlite';copy.write_bytes(data)
  connection=sqlite3.connect(f'file:{copy}?mode=ro&immutable=1',uri=True)
  check=[r[0] for r in connection.execute('PRAGMA integrity_check')];connection.close()
  result.append({'path':str(p.relative_to(repo)),'decodedSha256':hashlib.sha256(data).hexdigest(),'integrity':check,'mode':'ro immutable'});copy.unlink()
pathlib.Path('/tmp/elsa-2898-full-pr-r6-standards/sqlite-integrity-audit.json').write_text(json.dumps(result,indent=2))
print('distinct snapshots',len(result),'ok',sum(r['integrity']==['ok'] for r in result),'errors',[r for r in result if r['integrity']!=['ok']])
