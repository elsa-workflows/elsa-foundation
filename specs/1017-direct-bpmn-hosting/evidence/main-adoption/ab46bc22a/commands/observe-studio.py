from pathlib import Path
import importlib.util,json,sys,datetime
spec=importlib.util.spec_from_file_location('q','tools/bpmn/qualify-hosting.py');q=importlib.util.module_from_spec(spec);spec.loader.exec_module(q)
p=Path(sys.argv[1]);phase=sys.argv[2];case=json.loads((p/'case.json').read_text());ready=json.loads((p/'ready.json').read_text());api=q.Api(ready['backendUrl']);api.login('admin','Password123!');views={key:api.view(case[key])for key in ['publishedRunId','testRunId']};assert all(x['instance']['status']=='Completed'for x in views.values());now=datetime.datetime.now(datetime.timezone.utc).isoformat()
if phase=='before':
 assert not(p/'restarted.json').exists(),'First API observation must precede restart.'
 for key,name in [('publishedRunId','published-run-api.json'),('testRunId','test-run-api.json')]:q.write_json(p/name,views[key])
 q.write_json(p/'definition-after.json',api.request('/design/workflows/definitions/14iBsLEuiJS'))
 q.write_json(p/'before-restart-observation.json',{'observedAtUtc':now,'sourceRevision':ready['sourceRevision'],'processes':ready['processes'],'executionIds':[case['publishedRunId'],case['testRunId']],'commandWrittenAfterObservation':True})
 q.write_json(p/'command.json',{'action':'restart'})
elif phase=='after':
 assert(p/'restarted.json').exists();expected={case['publishedRunId']:json.loads((p/'published-run-api.json').read_text()),case['testRunId']:json.loads((p/'test-run-api.json').read_text())};actual={case[key]:views[key]for key in ['publishedRunId','testRunId']};assert actual==expected
 q.write_json(p/'restarted-new-views.json',actual);q.write_json(p/'after-restart-observation.json',{'observedAtUtc':now,'sameFullViews':True,'restarted':json.loads((p/'restarted.json').read_text())})
else:raise ValueError(phase)
print(phase,'complete',now)
