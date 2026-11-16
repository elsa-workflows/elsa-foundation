from pathlib import Path
import json,hashlib,shutil,importlib.util
repo=Path.cwd();root=Path(__file__).parent;out=root/'packaged-parallel';out.mkdir(exist_ok=True);m=json.loads((repo/'specs/1017-direct-bpmn-hosting/evidence/embedding/parallel-native-recovery/manifest.json').read_text());m['sourceRevision']='0ce9a41dba03c4f362c4d386f14737823f33238a'
for a in m['assets']:
 p=out/a['path'];p.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(root/'evidence'/a['path'],p);a['sha256']=hashlib.sha256(p.read_bytes()).hexdigest()
(out/'manifest.json').write_text(json.dumps(m,indent=2)+'\n')
spec=importlib.util.spec_from_file_location('v',repo/'tools/bpmn/verify_parallel_embedding.py');v=importlib.util.module_from_spec(spec);spec.loader.exec_module(v);result=v.verify(out/'manifest.json');(root/'parallel-verification.json').write_text(json.dumps(result,indent=2)+'\n');print(result)
