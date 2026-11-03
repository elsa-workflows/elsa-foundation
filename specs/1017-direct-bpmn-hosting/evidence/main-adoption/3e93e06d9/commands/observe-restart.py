from pathlib import Path
import importlib.util,json,sys
p=Path(sys.argv[1]);assert (p/'restarted.json').exists()
s=importlib.util.spec_from_file_location('hosting',Path.cwd()/'tools/bpmn/qualify-hosting.py');q=importlib.util.module_from_spec(s);s.loader.exec_module(q)
ready=json.loads((p/'ready.json').read_text());case=json.loads((p/'case.json').read_text());api=q.Api(ready['backendUrl']);api.login('admin','Password123!')
views={role:api.view(case[field]) for role,field in [('published-run','publishedRunId'),('test-run','testRunId')]}
before=json.loads((p/'before-restart-observation.json').read_text())['newViews'];assert views==before
q.write_json(p/'restarted-new-views.json',{case[field]:views[role] for role,field in [('published-run','publishedRunId'),('test-run','testRunId')]});q.write_json(p/'after-restart-observation.json',{'newViews':views,'allFullViewsIdenticalToBeforeRestart':True})
print('Both full views identical after actual restart',case['family'])
