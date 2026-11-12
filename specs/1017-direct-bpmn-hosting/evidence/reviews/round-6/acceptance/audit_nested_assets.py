import pathlib,json,hashlib,gzip,tarfile,collections
R=pathlib.Path(__import__('os').environ.get('ELSA_REVIEW_REPOSITORY', pathlib.Path.cwd())).resolve();E=R/'specs/1017-direct-bpmn-hosting/evidence';O=pathlib.Path(__file__).resolve().parent;rows=[];errors=[];cache={}
def visit(node,f,ctx='',hint=None):
 if isinstance(node,dict):
  local=next((node[k] for k in ('directory','name','parent','case') if isinstance(node.get(k),str)),hint)
  if isinstance(node.get('assets'),list):
   for i,a in enumerate(node['assets']):
    if not isinstance(a,dict) or not isinstance(a.get('path'),str) or not isinstance(a.get('sha256'),str):continue
    n=a['path'];p=f.parent/n;b=None;storage=str(p.relative_to(R)) if p.is_relative_to(R) else str(p)
    if p.is_file():b=p.read_bytes()
    elif local and (f.parent/local/n).is_file():p=f.parent/local/n;storage=str(p.relative_to(R));b=p.read_bytes()
    elif f.name=='archive-assets.json':
     archives=list(f.parent.glob('*.tar.gz'))
     if len(archives)==1:
      if archives[0] not in cache:
       with tarfile.open(archives[0],'r:gz') as t:cache[archives[0]]={m.name.removeprefix('./'):t.extractfile(m).read() for m in t.getmembers() if m.isfile()}
      b=cache[archives[0]].get(n);storage=str(archives[0].relative_to(R))+'::'+n
    row={'manifest':str(f.relative_to(R)),'jsonPath':ctx+'/assets/'+str(i),'asset':n,'storage':storage,'passed':b is not None and hashlib.sha256(b).hexdigest()==a['sha256']}
    if b is not None and row['passed']:
     for k in ('bytes','size'):
      if k in a and isinstance(a[k],int) and a[k]!=len(b):row['passed']=False;row['error']=k+' differs'
     if n.endswith('.gz') and ('decodedSha256' in a or 'databaseSha256' in a):
      q=gzip.decompress(b)
      for k in ('decodedSha256','databaseSha256'):
       if k in a and hashlib.sha256(q).hexdigest()!=a[k]:row['passed']=False;row['error']=k+' differs'
    if not row['passed']:errors.append(row)
    rows.append(row)
  for k,v in node.items():
   if k!='assets':visit(v,f,ctx+'/'+k,local)
 elif isinstance(node,list):
  for i,v in enumerate(node):visit(v,f,ctx+'/'+str(i),hint)
for f in E.rglob('*.json'):
 try:d=json.loads(f.read_text())
 except (ValueError,UnicodeError):continue
 visit(d,f)
summary={'declaredAssetReferences':len(rows),'passed':len(rows)-len(errors),'errors':errors,'errorManifests':dict(collections.Counter(e['manifest'] for e in errors))};(O/'nested-asset-audit.json').write_text(json.dumps(summary,indent=2));(O/'nested-asset-records.json').write_text(json.dumps(rows,indent=2));print(json.dumps({k:v for k,v in summary.items() if k!='errors'},indent=2));print('first errors',errors[:3])
