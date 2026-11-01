import subprocess,pathlib,json,os,time
R=pathlib.Path(__import__('os').environ.get('ELSA_REVIEW_REPOSITORY', pathlib.Path.cwd())).resolve();O=pathlib.Path(__file__).resolve().parent;E='specs/1017-direct-bpmn-hosting/evidence/'
items=[
('parallel-native','tools/bpmn/verify_parallel_embedding.py',['--manifest',E+'embedding/parallel-native-recovery/manifest.json']),
('human-duration','tools/bpmn/verify_duration_timer.py',['--manifest',E+'races/human-duration/manifest.json']),
('service-duration','e2e-tests/etl/verify_service_duration.py',['--manifest',E+'races/service-duration/manifest.json']),
('held-retry','e2e-tests/etl/verify_held_service_retry.py',['--manifest',E+'pipeline/held-service-retry/manifest.json']),
('held-cancellation','e2e-tests/etl/verify_held_service_cancellation.py',['--manifest',E+'pipeline/held-service-cancellation/manifest.json']),
('held-command-race','e2e-tests/etl/verify_held_command_race.py',['--manifest',E+'races/held-command-race/manifest.json']),
('budget-exhaustion','e2e-tests/etl/verify_service_budget_exhaustion.py',['--manifest',E+'pipeline/service-budget-exhaustion/manifest.json']),
('signal-recovery','e2e-tests/bpmn/verify_signal_recovery.py',[E+'events/signal-recovery/manifest.json']),
('model-broadcast','e2e-tests/bpmn/verify_model_broadcast.py',[E+'events/model-broadcast/manifest.json']),
('signal-interruption','e2e-tests/bpmn/verify_signal_interruption.py',[E+'events/pending-sibling-interruption/manifest.json']),
('runtime-only-rollback','e2e-tests/bpmn/verify_model_rollback.py',[E+'retention/runtime-only-rollback/manifest.json']),
]
results=[]
for name,script,args in items:
 at=time.monotonic();p=subprocess.run(['python3',script,*args],cwd=R,env={**os.environ,'PYTHONDONTWRITEBYTECODE':'1'},capture_output=True,text=True)
 (O/(name+'.stdout.json')).write_text(p.stdout);(O/(name+'.stderr.log')).write_text(p.stderr);r={'name':name,'command':['python3',script,*args],'exitCode':p.returncode,'seconds':round(time.monotonic()-at,3)};results.append(r);print(json.dumps(r),flush=True)
(O/'portable-checks.json').write_text(json.dumps(results,indent=2))
