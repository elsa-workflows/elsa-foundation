import json,pathlib,subprocess,time,os,signal,sqlite3,sys,tempfile
fixture=pathlib.Path(__file__).resolve().parent
root=pathlib.Path(sys.argv[1]).resolve() if len(sys.argv)>1 else pathlib.Path(tempfile.mkdtemp(prefix='elsa-retained-boundary-'))
root.mkdir(parents=True,exist_ok=True)
dll=fixture/'bin/Debug/net10.0/Worker.dll';ledger=[]
def durable(path):
 c=sqlite3.connect(path);d={}
 for table in ['elsa_runtime_activity_execution_state','elsa_runtime_workflow_execution_state','elsa_runtime_incident_state','elsa_runtime_scheduler_work_item','elsa_runtime_post_commit_outbox']:
  c.row_factory=sqlite3.Row;d[table]=sorted([dict(x) for x in c.execute('select * from '+table)],key=lambda r:r['Id'])
 c.close();return d
for mode in ['Durable','Balanced','Ephemeral']:
 for scenario in ['suspend','child']:
  case=root/f'takeover-{mode}-{scenario}';case.mkdir(exist_ok=False)
  cfg=dict(Action='run',Database=str(case/'runtime.db'),Mode=mode,Scenario=scenario,Point='after-body',Reached=str(case/'reached.json'),Release=str(case/'release'),Calls=str(case/'owner-a-calls.txt'),Result=str(case/'owner-a-result.json'))
  p=case/'a-config.json';p.write_text(json.dumps(cfg));log=(case/'a.log').open('w');a=subprocess.Popen(['dotnet',str(dll),str(p)],stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
  try:
   deadline=time.monotonic()+90
   while not pathlib.Path(cfg['Reached']).exists():
    if a.poll() is not None:raise RuntimeError(f'A exited: {a.returncode}, {case}')
    if time.monotonic()>deadline:raise RuntimeError(f'A gate timeout: {case}')
    time.sleep(.03)
   cfg.update(Action='recover',Calls=str(case/'owner-b-calls.txt'),Result=str(case/'owner-b-result.json'));p=case/'b-config.json';p.write_text(json.dumps(cfg))
   with (case/'b.log').open('w') as blog:b=subprocess.run(['dotnet',str(dll),str(p)],stdout=blog,stderr=subprocess.STDOUT,timeout=90)
   if b.returncode:raise RuntimeError(f'B failed: {b.returncode}, {case}')
   before=durable(cfg['Database']);(case/'before-stale-owner.json').write_text(json.dumps(before,indent=2))
   pathlib.Path(cfg['Release']).touch();a.wait(timeout=90);log.close()
   after=durable(cfg['Database']);(case/'after-stale-owner.json').write_text(json.dumps(after,indent=2))
   result=json.loads((case/'owner-b-result.json').read_text());stable=before==after
   rejected=a.returncode!=0 and 'RuntimeStaleFencingTokenException' in (case/'a.log').read_text()
   row=dict(mode=mode,scenario=scenario,ownerBExit=b.returncode,staleOwnerExit=a.returncode,correct=result['correct'],staleOwnerDidNotChangeDurableState=stable,staleFenceRejected=rejected,case=str(case));ledger.append(row);print(json.dumps(row),flush=True);(root/'takeovers.json').write_text(json.dumps(ledger,indent=2))
   if not stable or not result['correct'] or not rejected:raise SystemExit(1)
  finally:
   if a.poll() is None:os.killpg(a.pid,signal.SIGKILL);a.wait()
   log.close()
