from pathlib import Path
import json,hashlib,urllib.request,zipfile,xml.etree.ElementTree as ET,shutil
out=Path('/tmp/elsa-2898-main-c4cfa-qualification/api-latest');refs=out/'references';refs.mkdir(exist_ok=True);published=out/'published';published.mkdir(exist_ok=True)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
assets=json.loads((out/'obj/project.assets.json').read_text());folders=[Path(p) for p in assets['packageFolders']];records=[]
for key,value in assets['targets']['net10.0'].items():
 for rel in value.get('compile',{}):
  if not rel.endswith('.dll'):continue
  library=assets['libraries'][key];candidates=[folder/library['path']/rel for folder in folders];source=next(p for p in candidates if p.exists());target=refs/source.name
  if target.exists():assert sha(target)==sha(source)
  shutil.copyfile(source,target);records.append({'package':key,'compileAsset':rel,'file':target.name,'sha256':sha(target)})
(out/'reference-map.json').write_text(json.dumps(records,indent=2)+'\n')
downloads=[]
for seed in json.loads((out/'seeds.json').read_text()):
 pid=seed['package_id'];version=seed['version'];name=pid.lower()+'.'+version+'.nupkg';url='https://f.feedz.io/elsa-workflows/elsa-4/nuget/v3/packages/'+pid.lower()+'/'+version+'/'+name;package=out/name
 if not package.exists():urllib.request.urlretrieve(url,package)
 with zipfile.ZipFile(package) as z:
  nuspec=z.read(next(n for n in z.namelist() if n.endswith('.nuspec')));doc=ET.fromstring(nuspec);repo=next(e for e in doc.iter() if e.tag.split('}')[-1]=='repository');assert repo.attrib['commit']==seed['commit']
  dllname='lib/net10.0/'+pid+'.dll';dll=published/(pid+'.dll');dll.write_bytes(z.read(dllname))
 (out/(pid+'.nuspec')).write_bytes(nuspec)
 downloads.append({**seed,'url':url,'packageSha256':sha(package),'assemblySha256':sha(dll),'repositoryCommit':repo.attrib['commit'],'assemblyFile':dll.name})
(out/'downloads.json').write_text(json.dumps(downloads,indent=2)+'\n')
print('retained',len(records),'resolved compile references and',len(downloads),'published baseline packages')
