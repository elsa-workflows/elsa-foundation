from pathlib import Path
import json,gzip,hashlib,shutil,subprocess,importlib.util,unittest
r=Path(__file__).parent;out=r/'packaged';out.mkdir(exist_ok=True);source='4f46ac55c482b3b68932ca74f147439bfdacda49';template=Path('specs/1017-direct-bpmn-hosting/evidence/final-compatibility/studio/manifest.json');m=json.loads(template.read_text());m['sourceRevision']=source;m['cases']=json.loads((r/'cases.json').read_text())
for a in m['assets']:
 dest=out/a['path'];dest.parent.mkdir(parents=True,exist_ok=True)
 if a['path'].endswith('process.json.gz'):
  data=(r/a['path'][:-3]).read_bytes();dest.write_bytes(gzip.compress(data,mtime=0));a.update(decodedSha256=hashlib.sha256(data).hexdigest(),decodedBytes=len(data))
 else:shutil.copy2(r/a['path'],dest)
 a['sha256']=hashlib.sha256(dest.read_bytes()).hexdigest()
(out/'manifest.json').write_text(json.dumps(m,indent=2)+'\n')
cmd=['python3','tools/bpmn/verify_studio_upgrade.py','--manifest',str(out/'manifest.json')];result=subprocess.check_output(cmd,text=True);(r/'verification.json').write_text(result);print(result)
spec=importlib.util.spec_from_file_location('latest_studio_controls','e2e-tests/bpmn/test_verify_studio_upgrade.py');tests=importlib.util.module_from_spec(spec);spec.loader.exec_module(tests);tests.FinalActualStudioUpgradeControls.evidence=out
with(r/'controls.txt').open('w')as log:run=unittest.TextTestRunner(stream=log,verbosity=2).run(unittest.defaultTestLoader.loadTestsFromTestCase(tests.FinalActualStudioUpgradeControls))
assert run.wasSuccessful()and run.testsRun==13 and not run.skipped;print('Latest actual Studio bundle:13/13 controls,0skip.')
