import pathlib,subprocess,json,os,time
R=pathlib.Path(__import__('os').environ.get('ELSA_REVIEW_REPOSITORY', pathlib.Path.cwd())).resolve();O=pathlib.Path(__file__).resolve().parent;E='specs/1017-direct-bpmn-hosting/evidence/pipeline/';results=[]
for name in ['direct-http-recovery','embedded-native-recovery','business-error-native-recovery','technical-fault-native-recovery','consumed-before-ack','pending-native-parent-return','successful-native-parent-return']:
 cmd=['python3','e2e-tests/etl/verify_http_recovery.py','--manifest',E+name+'/manifest.json'];at=time.monotonic();p=subprocess.run(cmd,cwd=R,env={**os.environ,'PYTHONDONTWRITEBYTECODE':'1'},capture_output=True,text=True);(O/(name+'.stdout.json')).write_text(p.stdout);(O/(name+'.stderr.log')).write_text(p.stderr);r={'name':name,'command':cmd,'exitCode':p.returncode,'seconds':round(time.monotonic()-at,3)};results.append(r);print(json.dumps(r),flush=True)
(O/'outcome-checks.json').write_text(json.dumps(results,indent=2))
