import re as timestamp_re
import pathlib,subprocess,time,json,uuid,os,datetime

def parse_instant(value):
    value=timestamp_re.sub(r'\.(\d+)(?=[+-]\d{2}:\d{2}$)',lambda m:'.'+(m.group(1)+'000000')[:6],value)
    return datetime.datetime.fromisoformat(value)
ROOT=pathlib.Path('/private/tmp/elsa-native-perf-20261031');DLL=ROOT/'cluster-bin/Perf.dll'
def environment(owned=()):
 total=0
 for row in subprocess.check_output(['ps','-axo','pid,pcpu,comm'],text=True).splitlines()[1:]:
  fs=row.strip().split(None,2)
  if len(fs)==3 and int(fs[0]) not in [os.getpid(),*owned] and 'OrbStack' not in fs[2]:total+=float(fs[1])
 return dict(at=time.time(),load=os.getloadavg(),externalCpu=total)
for repeat in [1,2]:
 for nodes,total_concurrency in ([(1,8),(2,8)] if repeat==1 else [(2,8),(1,8)]):
  while True:
   quiet=0
   while quiet<5:
    e=environment();quiet=quiet+1 if e['externalCpu']<=400 else 0
    if quiet==0: print('WAITING',e,flush=True)
    time.sleep(2 if quiet else 10)
   key='cluster_'+uuid.uuid4().hex;gate=ROOT/(key+'.gate')
   subprocess.run(['docker','exec','elsa-perf-20261031','psql','-U','postgres','-c','CREATE DATABASE '+key],check=True,stdout=subprocess.DEVNULL)
   processes=[];handles=[];labels=[];load=[]
   for node in range(nodes):
    label=f'cluster-n{nodes}-c{total_concurrency}-r{repeat}-node{node}'
    labels.append(label)
    cfg=dict(Label=label,Provider='PostgreSql',Mode='Balanced',Activities=10,Concurrency=total_concurrency//nodes,Seconds=20,Minimum=100,
      WarmupSeconds=5,WarmupMinimum=10,Database=key,ArtifactKey=key,StartGate=str(gate),SkipInitialize=False,ReusePublication=node>0)
    path=ROOT/(label+'.config.json');path.write_text(json.dumps(cfg,indent=2))
    handle=(ROOT/(label+'.log')).open('w');handles.append(handle)
    child=subprocess.Popen(['dotnet',str(DLL),str(path)],stdout=handle,stderr=subprocess.STDOUT);processes.append(child)
    deadline=time.monotonic()+180
    while not pathlib.Path(str(gate)+'.'+label+'.ready').exists():
     if child.poll() is not None or time.monotonic()>deadline:
      for owned in processes:
       if owned.poll() is None:owned.terminate()
      for owned in processes:owned.wait()
      for handle in handles:handle.close()
      raise Exception('Node startup failed or timed out: '+(ROOT/(label+'.log')).read_text())
     time.sleep(.2)
   print('START',nodes,total_concurrency,repeat,flush=True);gate.touch();busy=0;contaminated=False;deadline=time.monotonic()+300
   while any(p.poll() is None for p in processes):
    e=environment([p.pid for p in processes]);load.append(e);busy=busy+1 if e['externalCpu']>500 else 0
    if busy>=2: contaminated=True
    if time.monotonic()>deadline:
     for owned in processes:
      if owned.poll() is None:owned.terminate()
     break
    time.sleep(2)
   for p in processes:p.wait()
   for h in handles:h.close()
   resultpaths=[ROOT/'results'/(label+'.json') for label in labels]
   if all(f.exists() for f in resultpaths):
    rs=[json.loads(f.read_text()) for f in resultpaths]
    start=min(parse_instant(r['measuredStartedAt']).timestamp() for r in rs)-2
    end=max(parse_instant(r['measuredEndedAt']).timestamp() for r in rs)+2
    consecutive=0;contaminated=False
    for sample in load:
     if not start<=sample['at']<=end:continue
     consecutive=consecutive+1 if sample['externalCpu']>500 else 0
     if consecutive>=2:contaminated=True
   outcome=dict(policy='timed-window-v3',nodes=nodes,concurrency=total_concurrency,repeat=repeat,labels=labels,database=key,load=load,contaminated=contaminated,exits=[p.returncode for p in processes])
   (ROOT/f'cluster-n{nodes}-c{total_concurrency}-r{repeat}.environment.json').write_text(json.dumps(outcome,indent=2))
   print(json.dumps({k:v for k,v in outcome.items() if k!='load'}),flush=True)
   if contaminated:
    for label in labels:
     f=ROOT/'results'/(label+'.json')
     if f.exists():
      excluded=ROOT/'excluded-busy-host';excluded.mkdir(exist_ok=True)
      f.rename(excluded/(key+'-'+f.name))
    with (ROOT/'cluster.progress.jsonl').open('a') as ledger:ledger.write(json.dumps(outcome)+'\n')
    continue
   if any(p.returncode for p in processes):raise Exception('Cluster validation failed')
   with (ROOT/'cluster.progress.jsonl').open('a') as ledger:ledger.write(json.dumps(outcome)+'\n')
   break
