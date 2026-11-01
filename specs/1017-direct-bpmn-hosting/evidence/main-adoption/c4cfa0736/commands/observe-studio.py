import importlib.util,json,sys
from pathlib import Path
spec=importlib.util.spec_from_file_location('q',Path.cwd()/'tools/bpmn/qualify-hosting.py');q=importlib.util.module_from_spec(spec);spec.loader.exec_module(q)
p=Path(sys.argv[1]);ready=json.loads((p/'ready.json').read_text());api=q.Api(ready['backendUrl']);api.login('admin','Password123!')
for role,identity in [s.split('=',1) for s in sys.argv[2:]]:
 view=api.view(identity)
 if view['instance']['status']!='Completed' or len(view['activities'])!=2 or any(a['status']!='Completed' for a in view['activities']) or view['incidents']:raise ValueError('The actual run is not complete')
 q.write_json(p/(role+'-api.json'),view)
 print(role,identity,view['instance']['runKind'],view['instance']['status'])
q.write_json(p/'definition-after.json',api.request('/design/workflows/definitions/14iBsLEuiJS'))
print('Saved definition API')
