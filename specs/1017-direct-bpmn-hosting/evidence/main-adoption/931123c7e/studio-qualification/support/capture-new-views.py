from pathlib import Path
import argparse,importlib.util,json,subprocess
p=argparse.ArgumentParser();p.add_argument('family',choices=['original','layout']);p.add_argument('published');p.add_argument('test');p.add_argument('--restarted',action='store_true');a=p.parse_args();repo=Path.cwd();out=Path(__file__).parent/a.family;ready=json.loads((out/'ready.json').read_text());assert ready['sourceRevision']==subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
spec=importlib.util.spec_from_file_location('qualification',repo/'tools/bpmn/qualify-hosting.py');q=importlib.util.module_from_spec(spec);spec.loader.exec_module(q);api=q.Api(ready['backendUrl']);api.login('admin','Password123!');views={}
for name,identity,kind in [('published-run-api.json',a.published,'PublishedRun'),('test-run-api.json',a.test,'TestRun')]:
 v=api.view(identity);assert v['instance']['workflowExecutionId']==identity and v['instance']['status']=='Completed'and v['instance']['runKind']==kind and not v['incidents'];views[identity]=v
 if not a.restarted:(out/name).write_text(json.dumps(v,indent=2)+'\n')
if a.restarted:
 for name,identity in [('published-run-api.json',a.published),('test-run-api.json',a.test)]:assert views[identity]==json.loads((out/name).read_text()),identity
 (out/'restarted-new-views.json').write_text(json.dumps(views,indent=2)+'\n')
else:
 (out/'definition-after.json').write_text(json.dumps(api.request('/design/workflows/definitions/14iBsLEuiJS'),indent=2)+'\n')
print(a.family,'restarted'if a.restarted else'initial','new completed views captured and checked',list(views))
