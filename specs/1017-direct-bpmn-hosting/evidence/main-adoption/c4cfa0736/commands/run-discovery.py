from pathlib import Path
import ast,json,subprocess,time
root=Path.cwd();out=Path('/tmp/elsa-2898-main-c4cfa-qualification/discovery');out.mkdir(exist_ok=True);tree=ast.parse(Path('/tmp/elsa-2898-main-c4cfa-integration/run-qualification-resume.py').read_text());node=next(n for n in tree.body if isinstance(n,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='projects' for t in n.targets));projects=ast.literal_eval(node.value);records=[]
for name,project in projects:
 source=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip();assert source=='1cee5acd4e7d7adb01dd467342641a108e349d60'
 args=['dotnet','test',project,'-c','Release','--no-build','--no-restore','--list-tests'];p=subprocess.run(args,capture_output=True,text=True);(out/(name+'.log')).write_text(p.stdout+p.stderr);assert p.returncode==0
 marker='The following Tests are available:';assert marker in p.stdout;names=[s.strip() for s in p.stdout.split(marker,1)[1].splitlines() if s.strip()];records.append({'name':name,'source':source,'arguments':args,'exitCode':0,'discovered':len(names),'tests':names});(out/'results.json').write_text(json.dumps(records,indent=2)+'\n');print(name,len(names),flush=True)
