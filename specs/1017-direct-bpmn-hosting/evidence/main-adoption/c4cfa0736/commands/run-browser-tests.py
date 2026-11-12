from pathlib import Path
import subprocess,json,os,time
root=Path.cwd();out=Path('/tmp/elsa-2898-main-c4cfa-qualification');source='1cee5acd4e7d7adb01dd467342641a108e349d60'
files=['activity-definitions','activity-interface-layout','connection-secrets','creation-dialog-tokens','design-language','etl-connector-authoring','golden-runs','secret-scopes','workspace-appearance','workflow-run-deletion','activity-availability-layout','draft-validation-autosave','validation-labels','workflow-properties-scroll']
# Existing affected browser specifications plus the existing qualification scope.
for p in (root/'Studio/tests/browser').glob('*.spec.ts'):
 if 'activityDuplicateEditorFixture' in p.read_text() and p.stem.removesuffix('.spec') not in files: files.append(p.stem.removesuffix('.spec'))
steps=[('browser-affected',['pnpm','exec','playwright','test',*[f'tests/browser/{n}.spec.ts' for n in files],'--workers=2','--reporter=json']),('browser-library',['pnpm','exec','playwright','test','--config','tests/browser/playwright.workflow-library.config.ts','--reporter=json']),('browser-canvas',['pnpm','exec','playwright','test','--config','tests/browser/playwright.workflow-canvas.config.ts','--reporter=json'])]
records=[]
for name,args in steps:
 assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==source
 print('starting',name,flush=True);start=time.time()
 with (out/(name+'.json')).open('w') as log,(out/(name+'.stderr.log')).open('w') as err:r=subprocess.run(args,cwd=root/'Studio',stdout=log,stderr=err,env={**os.environ,'CI':'1'})
 record={'name':name,'source':source,'arguments':args,'exitCode':r.returncode,'elapsedSeconds':time.time()-start};records.append(record)
 (out/'browser-gate-results.json').write_text(json.dumps(records,indent=2)+'\n');print('finished',name,r.returncode,flush=True)
 if r.returncode:raise SystemExit(r.returncode)
