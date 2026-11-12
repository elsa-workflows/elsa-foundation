import pathlib,json,subprocess,os,time
R=pathlib.Path(__import__('os').environ.get('ELSA_REVIEW_REPOSITORY', pathlib.Path.cwd())).resolve();O=pathlib.Path(__file__).resolve().parent;results=[];base='specs/1017-direct-bpmn-hosting/evidence/pipeline/'
items=[]
for name in ['root','sequence','flowchart','compensationscope']:
 items.append(('etl-origin-'+name,['python3','e2e-tests/etl/verify_http_recovery.py','--manifest',base+'embedded-pipeline-cancellation/'+name+'/manifest.json']))
for name in ['direct','root','sequence','flowchart','compensationscope']:
 items.append(('inverse-cancel-'+name,['python3','e2e-tests/etl/verify_inverse_cancellation.py',base+'inverse-cancellation-order/'+name]))
for name,cmd in items:
 at=time.monotonic();p=subprocess.run(cmd,cwd=R,env={**os.environ,'PYTHONDONTWRITEBYTECODE':'1'},capture_output=True,text=True);(O/(name+'.stdout.json')).write_text(p.stdout);(O/(name+'.stderr.log')).write_text(p.stderr);r={'name':name,'command':cmd,'exitCode':p.returncode,'seconds':round(time.monotonic()-at,3)};results.append(r);print(json.dumps(r),flush=True)
(O/'cancellation-checks.json').write_text(json.dumps(results,indent=2))
