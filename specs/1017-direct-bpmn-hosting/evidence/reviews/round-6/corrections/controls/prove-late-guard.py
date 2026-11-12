from pathlib import Path
import subprocess,hashlib,json
p=Path('Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnDirectExecutionDriver.cs');original=p.read_bytes();text=original.decode();a=text.index('            try { document = Apply(context, document, evaluation, commitId, timerDecisionAt ?? clock.GetUtcNow()); }');b=text.index('            if (timedFrame && document.Failures',a)
mutated=text[:a]+'            document = Apply(context, document, evaluation, commitId, timerDecisionAt ?? clock.GetUtcNow());\n'+text[b:]
out=Path('/tmp/elsa-2898-round6-corrections')
cmd=['dotnet','test','Essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj','-c','Release','--no-restore','--filter','FullyQualifiedName~A_timer_that_becomes_unrepresentable_after_admission','--logger','trx;LogFileName=late-guard-removed.trx','--results-directory',str(out)]
try:
 p.write_text(mutated)
 with (out/'late-guard-removed.log').open('w') as f: code=subprocess.call(cmd,stdout=f,stderr=subprocess.STDOUT)
finally:
 p.write_bytes(original)
assert p.read_bytes()==original
(out/'late-guard-mutation.json').write_text(json.dumps({'mutation':'Remove only the newly added durable timer-fault disposition guard; keep admission guards unchanged','productionSourceRestoredByteExactly':True,'originalSha256':hashlib.sha256(original).hexdigest(),'mutatedSha256':hashlib.sha256(mutated.encode()).hexdigest(),'exitCode':code,'command':cmd},indent=2)+'\n')
print('mutation exit',code,'source restored byte-exact',flush=True)
if code==0:raise SystemExit('Guard-removal control unexpectedly passed')
