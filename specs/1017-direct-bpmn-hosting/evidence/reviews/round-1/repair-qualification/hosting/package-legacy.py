import gzip,hashlib,importlib.util,json,shutil,subprocess,sys,tempfile
from pathlib import Path
repo=Path.cwd(); raw=Path('/tmp/elsa-2898-review-1/fixed-0ce9a41db/hosting'); out=raw/'packaged-legacy'
source=json.loads((raw/'run.json').read_text())['sourceRevision']
spec=importlib.util.spec_from_file_location('q',repo/'tools/bpmn/qualify-hosting.py');q=importlib.util.module_from_spec(spec);spec.loader.exec_module(q)
results={}
designs={'partial-forks':'legacy-partial-forks/upgraded-design.sqlite.gz','native-parents':'legacy-native-terminals/native-terminals-design.sqlite.gz','called-ack':'legacy-called-ack/notice-finished.sqlite.gz','broadcast':'legacy-broadcast/before-selection-finished.sqlite.gz','controls':'legacy-controls/finished-design.sqlite.gz','multi-instance':'legacy-multi-instance/finished-design.sqlite.gz'}
for family,design in designs.items():
 directory=out/family;directory.mkdir(parents=True,exist_ok=True)
 manifest=json.loads((repo/'specs/1017-direct-bpmn-hosting/evidence/upgrade'/family/'manifest.json').read_text())
 manifest['sourceRevision']=source
 for asset in manifest['assets']:
  if asset['path']=='frozen-contract.json':continue
  target=directory/asset['path'];target.parent.mkdir(parents=True,exist_ok=True)
  if asset['path'].endswith('process.json.gz'):
   original=raw/'evidence'/asset['path'][:-3];target.write_bytes(gzip.compress(original.read_bytes(),mtime=0));asset['uncompressedSha256']=q.digest(original)
  else:shutil.copyfile(raw/'evidence'/asset['path'],target)
  asset['sha256']=q.digest(target)
 with tempfile.TemporaryDirectory() as temp:
  database=Path(temp)/'design.sqlite';database.write_bytes(gzip.decompress((raw/'evidence'/design).read_bytes()))
  subprocess.run([sys.executable,str(repo/'e2e-tests/bpmn/verify_published_contracts.py'),'--database',str(database),'--source-revision',source,'--closed-snapshot','--output',str(directory/'frozen-contract.json')],check=True,stdout=subprocess.DEVNULL)
 next(a for a in manifest['assets'] if a['path']=='frozen-contract.json')['sha256']=q.digest(directory/'frozen-contract.json')
 q.write_json(directory/'manifest.json',manifest)
 results[family]=q.verify_upgrade(directory/'manifest.json')
q.write_json(out/'results.json',results)
print(json.dumps(results,indent=2))
