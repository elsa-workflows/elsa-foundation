import re as timestamp_re
import json, subprocess, time, pathlib, sys, random, os, datetime

def parse_instant(value):
    value=timestamp_re.sub(r'\.(\d+)(?=[+-]\d{2}:\d{2}$)',lambda m:'.'+(m.group(1)+'000000')[:6],value)
    return datetime.datetime.fromisoformat(value)
ROOT = pathlib.Path('/private/tmp/elsa-native-perf-20261031')
DLL = ROOT/'baseline-bin/Perf.dll'
phase = sys.argv[1] if len(sys.argv)>1 else 'baseline'
cases=[]
if phase == 'baseline':
    for repeat in range(1,3):
        for activities in [1,10,100]:
            modes=['Durable','Balanced','Ephemeral','EphemeralNoRecord']
            random.Random(repeat*101+activities).shuffle(modes)
            for mode in modes:
                cases.append(dict(Provider='PostgreSql',Mode=mode,Activities=activities,Repeat=repeat))
elif phase == 'local':
    for provider in ['Memory','Sqlite']:
        for activities in [1,10,100]:
            for mode in ['Balanced','EphemeralNoRecord']:
                cases.append(dict(Provider=provider,Mode=mode,Activities=activities,Repeat=1))
elif phase == 'scaling':
    for repeat in range(1,3):
        for mode in ['Balanced','Durable','EphemeralNoRecord']:
            if repeat==2 and mode!='Balanced': continue
            for concurrency in [4,16]:
                cases.append(dict(Provider='PostgreSql',Mode=mode,Activities=10,Concurrency=concurrency,Repeat=repeat))
        for mode in (['Durable','Balanced','EphemeralNoRecord'] if repeat==1 else []):
            cases.append(dict(Provider='PostgreSql',Mode=mode,Activities=10,External=True,Repeat=repeat))
elif phase == 'capacity':
    for repeat in [1]:
        for activities,concurrency in [(1,16),(1,64),(10,64)]:
            cases.append(dict(Provider='PostgreSql',Mode='Balanced',Activities=activities,Concurrency=concurrency,Repeat=repeat))
elif phase == 'tuning':
    for repeat in range(1,3):
        for pool in ([False,True] if repeat==1 else [True,False]):
            cases.append(dict(Provider='PostgreSql',Mode='Balanced',Activities=10,ContextPooling=pool,Concurrency=4,Repeat=repeat))
        for cap in ([10,250] if repeat==1 else []):
            cases.append(dict(Provider='PostgreSql',Mode='Balanced',Activities=100,Cap=cap,Repeat=repeat))
elif phase == 'experiment':
    for repeat in range(1,3):
        for activities in [10,100]:
            for variant in (['baseline','fast'] if repeat==1 else ['fast','baseline']):
                cases.append(dict(Provider='PostgreSql',Mode='Balanced',Activities=activities,Variant=variant,Repeat=repeat))
elif phase == 'sqlite-scaling':
    for concurrency in [4,16]:
        cases.append(dict(Provider='Sqlite',Mode='Balanced',Activities=10,Concurrency=concurrency,Repeat=1))
else: raise ValueError(phase)

def environment(owned_pid=None):
    external=0
    for row in subprocess.check_output(['ps','-axo','pid,pcpu,comm'],text=True).splitlines()[1:]:
        fields=row.strip().split(None,2)
        if len(fields)!=3:continue
        pid,cpu,command=fields
        if int(pid) in [os.getpid(),owned_pid] or 'OrbStack' in command:continue
        external+=float(cpu)
    return dict(at=time.time(),load=os.getloadavg(),externalCpu=round(external,1))

with (ROOT/(phase+'.progress.jsonl')).open('a',buffering=1) as progress:
    for i,case in enumerate(cases):
        repeat=case.pop('Repeat')
        label=phase+'-'+ '-'.join(f'{k}{v}' for k,v in case.items())+f'-r{repeat}'
        config=dict(Label=label,Seconds=15,Minimum=100,WarmupSeconds=5,WarmupMinimum=10,**case)
        dll=ROOT/'experiment-bin/Perf.dll' if case.get('Variant')=='fast' else (ROOT/'local-bin/Perf.dll' if phase=='local' else DLL)
        if phase=='local': config.update(Seconds=10,WarmupSeconds=3,Minimum=30)
        if phase=='tuning' and case.get('Activities')==100: config.update(Minimum=30)
        if case.get('Mode')=='Durable' and case.get('Activities')==100:
            config.update(Minimum=30,WarmupMinimum=3)
        cfg=ROOT/(label+'.config.json')
        cfg.write_text(json.dumps(config,indent=2))
        result=ROOT/'results'/(label+'.json')
        if result.exists() and json.loads(result.read_text())['Correct']:
            print('ALREADY',label,flush=True); continue
        while True:
            env=environment()
            quiet=0
            while quiet<5:
                quiet=quiet+1 if env['externalCpu']<=400 else 0
                if quiet==0: print('WAITING FOR HOST',env,flush=True)
                time.sleep(2 if quiet else 10);env=environment()
            before=subprocess.check_output(['uptime'],text=True).strip()
            print(f'{i+1}/{len(cases)} START {label} {before}',flush=True)
            started=time.time();contaminated=False;busySamples=0
            with (ROOT/(label+'.log')).open('w') as log:
                run=subprocess.Popen(['dotnet',str(dll),str(cfg)],stdout=log,stderr=subprocess.STDOUT)
                loadSamples=[]
                while run.poll() is None:
                    env=environment(run.pid);loadSamples.append(env)
                    busySamples=busySamples+1 if env['externalCpu']>500 else 0
                    if busySamples>=2: contaminated=True
                    if time.time()-started>900: run.terminate();run.wait();break
                    time.sleep(2)
            rejectedWindow=[]
            if result.exists():
                window=json.loads(result.read_text())
                start=parse_instant(window['measuredStartedAt']).timestamp()-2
                end=parse_instant(window['measuredEndedAt']).timestamp()+2
                rejectedWindow=[e for e in loadSamples if start<=e['at']<=end]
                consecutive=0;contaminated=False
                for sample in rejectedWindow:
                    consecutive=consecutive+1 if sample['externalCpu']>500 else 0
                    if consecutive>=2: contaminated=True
            entry=dict(policy='timed-window-v3',measuredLoadSamples=rejectedWindow,label=label,exit=run.returncode,seconds=time.time()-started,loadBefore=before,loadAfter=subprocess.check_output(['uptime'],text=True).strip(),loadSamples=loadSamples,contaminated=contaminated)
            if result.exists():
                r=json.loads(result.read_text())
                entry.update({k:r[k] for k in ['Correct','Count','WorkflowsPerSecond','P95Ms','AllocatedBytesPerWorkflow']})
                entry['SqlCommands']=r['Accounting']['Count'];entry['Transactions']=r['Accounting']['Commits']
            progress.write(json.dumps(entry)+'\n');print(json.dumps({k:v for k,v in entry.items() if k not in ['loadSamples','measuredLoadSamples']}),flush=True)
            if contaminated:
                if result.exists():
                    excluded=ROOT/'excluded-busy-host';excluded.mkdir(exist_ok=True);result.rename(excluded/result.name)
                continue
            if run.returncode:
                print((ROOT/(label+'.log')).read_text()[-3000:],flush=True)
                if phase!='local':sys.exit(run.returncode)
                failed=ROOT/'failed-results';failed.mkdir(exist_ok=True)
                if result.exists():result.rename(failed/result.name)
            break

if phase == 'tuning':
    for followup in ['capacity','sqlite-scaling','experiment']:
        with (ROOT/(followup+'.log')).open('w') as log:
            subprocess.run([sys.executable,__file__,followup],stdout=log,stderr=subprocess.STDOUT,check=followup!='sqlite-scaling')
    with (ROOT/'cluster.log').open('w') as log:
        subprocess.run([sys.executable,str(ROOT/'run_cluster.py')],stdout=log,stderr=subprocess.STDOUT,check=True)
    (ROOT/'matrix.complete').write_text(str(time.time()))
