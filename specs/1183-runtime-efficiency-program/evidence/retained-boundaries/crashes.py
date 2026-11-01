import json,pathlib,subprocess,time,os,signal,sqlite3,base64,sys,tempfile
fixture=pathlib.Path(__file__).resolve().parent
root=pathlib.Path(sys.argv[1]).resolve() if len(sys.argv)>1 else pathlib.Path(tempfile.mkdtemp(prefix='elsa-retained-boundary-'))
root.mkdir(parents=True,exist_ok=True)
dll=fixture/'bin/Debug/net10.0/Worker.dll'
ledger=[]
def dec(s):
 try:return base64.b64decode(s,validate=True).decode('utf-16-le')
 except:return s
def snapshot(database):
 c=sqlite3.connect(database); tables=[r[0] for r in c.execute("select name from sqlite_master where type='table'")]
 d={}
 for table in tables:
  if table in ['elsa_runtime_activity_execution_state','elsa_runtime_workflow_execution_state','elsa_runtime_scheduler_work_item','elsa_runtime_checkpoint_commit','elsa_runtime_post_commit_outbox']:
   c.row_factory=sqlite3.Row;d[table]=[dict(r) for r in c.execute('select * from '+table)]
 c.close();return d
for mode in ['Durable','Balanced','Ephemeral']:
 for scenario in ['suspend','child']:
  for point in ['before-commit','after-commit','after-advance','after-body']:
   case=root/f'crash-v3-{mode}-{scenario}-{point}';case.mkdir(exist_ok=False)
   cfg=dict(Action='run',Database=str(case/'runtime.db'),Mode=mode,Scenario=scenario,Point=point,Reached=str(case/'reached.json'),Calls=str(case/'calls.txt'),Result=str(case/'run-result.json'))
   p=case/'config.json';p.write_text(json.dumps(cfg));log=(case/'run.log').open('w');worker=subprocess.Popen(['dotnet',str(dll),str(p)],stdout=log,stderr=subprocess.STDOUT,start_new_session=True)
   try:
    deadline=time.monotonic()+90
    while not pathlib.Path(cfg['Reached']).exists():
     if worker.poll() is not None:raise RuntimeError(f'Worker exited before gate: {worker.returncode}, {case}')
     if time.monotonic()>deadline:raise RuntimeError(f'Gate timeout: {case}')
     time.sleep(.03)
    os.killpg(worker.pid,signal.SIGKILL);worker.wait(timeout=10);log.close()
    before=snapshot(cfg['Database']);(case/'durable-before-recovery.json').write_text(json.dumps(before,indent=2))
    completed=[]
    for row in before.get('elsa_runtime_activity_execution_state',[]):
     state=json.loads(row['ContentJson'])
     if state.get('status')=='Completed':completed.append((dec(state['execution']['activityExecutionId']),dec(state['execution']['executableNodeId'])))
    cfg.update(Action='recover',Result=str(case/'recovered.json'),Calls=str(case/'recovery-calls.txt'));p=case/'recover-config.json';p.write_text(json.dumps(cfg))
    with (case/'recover.log').open('w') as recoverylog:run=subprocess.run(['dotnet',str(dll),str(p)],stdout=recoverylog,stderr=subprocess.STDOUT,timeout=90)
    result=json.loads((case/'recovered.json').read_text()) if (case/'recovered.json').exists() else {}
    # The worker checks Completed; independently preserve the identities committed before the kill.
    final={(a['Execution']['ActivityExecutionId'],a['Execution']['ExecutableNodeId']) for a in result.get('activities',[])}
    durable_preserved=all(x in final for x in completed)
    replayed=(case/'recovery-calls.txt').read_text().splitlines() if (case/'recovery-calls.txt').exists() else []
    persisted_bodies_not_repeated=all('parent|'+node not in replayed for _,node in completed if node.startswith('work-'))
    record=dict(mode=mode,scenario=scenario,point=point,killedExit=worker.returncode,recoveryExit=run.returncode,correct=result.get('correct'),durableCompletedBefore=len(completed),durableCompletedPreserved=durable_preserved,persistedBodiesNotRepeated=persisted_bodies_not_repeated,case=str(case))
    ledger.append(record);print(json.dumps(record),flush=True);(root/'crashes.json').write_text(json.dumps(ledger,indent=2))
    if run.returncode or not result.get('correct') or not durable_preserved or not persisted_bodies_not_repeated:raise SystemExit(1)
   finally:
    if worker.poll() is None:os.killpg(worker.pid,signal.SIGKILL);worker.wait()
    log.close()
