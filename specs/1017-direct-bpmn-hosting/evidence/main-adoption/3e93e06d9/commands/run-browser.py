from pathlib import Path
import subprocess,json,time
out=Path('/tmp/elsa-2898-main-3e93-qualification');source='b7e23ab0984b201ab9b3eaaaf4b5d964222ca3a8'
assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==source
records=[]
for name,config in [('browser','playwright.config.ts'),('browser-root','playwright.root.config.ts')]:
 args=['pnpm','exec','playwright','test','--config',str(out/config)]
 start=time.time();print('starting',name,flush=True)
 with (out/(name+'.log')).open('w') as log:r=subprocess.run(args,cwd='Studio',stdout=log,stderr=subprocess.STDOUT)
 records.append({'name':name,'source':source,'arguments':args,'exitCode':r.returncode,'elapsedSeconds':time.time()-start,'scope':'Affected existing theme/authoring/list/canvas/run surfaces; owned strict port; no new capability matrix'})
 (out/'browser-command.json').write_text(json.dumps(records,indent=2)+'\n');print(name,'exit',r.returncode,flush=True)
 if r.returncode:raise SystemExit(r.returncode)
