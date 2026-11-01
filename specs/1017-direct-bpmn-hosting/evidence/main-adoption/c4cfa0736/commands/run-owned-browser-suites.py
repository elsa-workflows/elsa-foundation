from pathlib import Path
import subprocess,os,json,time
r=Path('/tmp/elsa-2898-main-c4cfa-qualification');source='1cee5acd4e7d7adb01dd467342641a108e349d60';records=json.loads((r/'browser-gate-results.json').read_text())
for name,config in [('browser-library','playwright.workflow-library.config.ts'),('browser-canvas','playwright.workflow-canvas.config.ts')]:
 assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==source
 args=['pnpm','exec','playwright','test','--config','tests/browser/'+config,'--reporter=json'];print('starting',name,flush=True);load=subprocess.check_output(['uptime'],text=True).strip();start=time.time()
 with (r/(name+'.json')).open('w') as log,(r/(name+'.stderr.log')).open('w') as err:p=subprocess.run(args,cwd=Path.cwd()/'Studio',stdout=log,stderr=err,env={**os.environ,'CI':'1'})
 records.append({'name':name,'source':source,'arguments':args,'exitCode':p.returncode,'elapsedSeconds':time.time()-start,'loadBeforeRun':load});(r/'browser-gate-results.json').write_text(json.dumps(records,indent=2)+'\n');print('finished',name,p.returncode,flush=True)
 if p.returncode:raise SystemExit(p.returncode)
