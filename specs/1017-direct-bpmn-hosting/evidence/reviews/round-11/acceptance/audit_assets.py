import pathlib,json,gzip,hashlib,sqlite3
repo=pathlib.Path('/Users/sipke/.codex/worktrees/f236/elsa-foundation'); out=pathlib.Path('/tmp/elsa-2898-review11-acceptance')
def sha(b):return hashlib.sha256(b).hexdigest()
def decode(b):
 while b.startswith(b'\x1f\x8b'):b=gzip.decompress(b)
 return b
r=repo/'specs/1017-direct-bpmn-hosting/evidence'; manif=[r/'baseline/manifest.json',r/'reviews/round-10/corrections/delayed-retry-original/manifest.json',r/'reviews/round-10/corrections/qualification/manifest.json',r/'reviews/round-10/corrections/qualification/studio/manifest.json']
manif.insert(1,r/'reviews/round-7/manifest.json')
summary=[]; stores={}
for mp in manif:
 m=json.loads(mp.read_text());rows=m['assets'];checked=0
 if isinstance(rows,dict): rows=[dict(path=k,sha256=v) for k,v in rows.items()]
 for a in rows:
  p=(mp.parent/a['path']).resolve();b=p.read_bytes();assert sha(b)==a['sha256'],p
  if 'size' in a:assert len(b)==a['size'],p
  if 'decodedSha256' in a:
   d=decode(b);assert sha(d)==a['decodedSha256'],p
   if 'decodedBytes' in a:assert len(d)==a['decodedBytes'],p
  if '.sqlite' in p.name:
   d=decode(b);assert d.startswith(b'SQLite format 3\0'),p
   key=sha(d)
   if key not in stores:
    local=out/(key+'.sqlite');local.write_bytes(d);db=sqlite3.connect(f'file:{local}?mode=ro&immutable=1',uri=True);assert db.execute('pragma integrity_check').fetchall()==[('ok',)],p
    stores[key]={'references':[],'tables':db.execute("select count(*) from sqlite_master where type='table'").fetchone()[0]};db.close()
   stores[key]['references'].append(str(p.relative_to(repo)))
  checked+=1
 summary.append({'manifest':str(mp.relative_to(repo)),'assetsVerified':checked})
result={'manifests':summary,'uniqueWholeStoresIntegrityChecked':len(stores),'wholeStoreReferences':sum(len(x['references']) for x in stores.values()),'stores':stores}
(out/'asset-audit.json').write_text(json.dumps(result,indent=2)); print(json.dumps({k:v for k,v in result.items() if k!='stores'},indent=2))
# Physical delayed input table names / payload headers for a separate causal comparison.
original=out/'dedc7041baf31ad6d0b3de251049de7d55ff421dbe7e65e3059b6e35f9bf9e63.sqlite';db=sqlite3.connect(f'file:{original}?mode=ro&immutable=1',uri=True)
for name,sql in db.execute("select name,sql from sqlite_master where type='table'"):
 if any(t in name.lower() for t in ['model','workflowexecution','outbox','scheduler']):print(name,sql[:500])
