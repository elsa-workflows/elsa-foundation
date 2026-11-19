import json,pathlib,subprocess,time,os,signal,sys,tempfile
fixture=pathlib.Path(__file__).resolve().parent
root=pathlib.Path(sys.argv[1]).resolve() if len(sys.argv)>1 else pathlib.Path(tempfile.mkdtemp(prefix='elsa-retained-boundary-'))
root.mkdir(parents=True,exist_ok=True)
dll=fixture/'bin/Debug/net10.0/Worker.dll'
ledger=[]
for mode in ['Durable','Balanced','Ephemeral']:
 for scenario in ['suspend','child']:
  case=root/f'control-v2-{mode}-{scenario}';case.mkdir(exist_ok=True)
  cfg=dict(Action='run',Database=str(case/'runtime.db'),Mode=mode,Scenario=scenario,Point='none',Reached=str(case/'reached.json'),Calls=str(case/'calls.txt'),Result=str(case/'result.json'))
  p=case/'config.json';p.write_text(json.dumps(cfg))
  with (case/'run.log').open('w') as log:
   run=subprocess.run(['dotnet',str(dll),str(p)],stdout=log,stderr=subprocess.STDOUT,timeout=120)
  result=json.loads((case/'result.json').read_text()) if (case/'result.json').exists() else {}
  record=dict(mode=mode,scenario=scenario,exit=run.returncode,correct=result.get('correct'))
  ledger.append(record); print(json.dumps(record),flush=True)
  (root/'controls.json').write_text(json.dumps(ledger,indent=2))
  if run.returncode:raise SystemExit(1)
