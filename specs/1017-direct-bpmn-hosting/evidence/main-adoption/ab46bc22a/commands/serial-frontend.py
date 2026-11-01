from pathlib import Path
import subprocess,json,re,time

root=Path('/Users/sipke/.codex/worktrees/f236/elsa-foundation/Studio')
out=Path('/tmp/elsa-2898-post-owner-main-ab46-qualification/frontend-serial');out.mkdir(exist_ok=True)
projects=json.loads(subprocess.check_output(['pnpm','-r','list','--depth','-1','--json'],cwd=root,text=True))
results=[]
for project in projects:
    path=Path(project['path'])
    if path==root:continue
    pkg=json.loads((path/'package.json').read_text());script=pkg.get('scripts',{}).get('test')
    if not script:continue
    name=re.sub('[^A-Za-z0-9._-]+','_',pkg['name']);log=out/(name+'.log');report=out/(name+'.json')
    args=['pnpm','test']
    if 'vitest' in script:args+=['--maxWorkers=2','--minWorkers=1','--reporter=json','--outputFile='+str(report)]
    start=time.time()
    print('starting',pkg['name'],flush=True)
    with log.open('w') as f:run=subprocess.run(args,cwd=path,stdout=f,stderr=subprocess.STDOUT)
    row={'package':pkg['name'],'path':str(path.relative_to(root.parent)),'script':script,'arguments':args,'exitCode':run.returncode,'elapsedSeconds':time.time()-start,'log':log.name,'kind':'vitest' if 'vitest' in script else 'script-only'}
    if report.exists():
        raw=json.loads(report.read_text());row['report']=report.name
        row['counts']={k:raw.get(k) for k in ['numTotalTests','numPassedTests','numFailedTests','numPendingTests','numTodoTests','numTotalTestSuites','numPassedTestSuites','numFailedTestSuites']}
    results.append(row);(out/'results.json').write_text(json.dumps(results,indent=2)+'\n')
    print('finished',pkg['name'],run.returncode,row.get('counts',{}),flush=True)
    if run.returncode:raise SystemExit(run.returncode)
print('all workspace test scripts completed; Vitest package execution serialized with max2 workers; script-only steps kept separate',flush=True)
