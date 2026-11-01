from pathlib import Path
import subprocess,json,shutil,hashlib
base=Path('/tmp/elsa-2898-round5-final-qualification/api-binary');out=Path('/tmp/elsa-2898-round6-integration/api-binary');out.mkdir(exist_ok=True);current=out/'current';current.mkdir(exist_ok=True)
source='4dc934c444705ddf40707b8eeb1991e1949fefa9';assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==source
host=Path('Essentials/apps/Elsa.Workbench/bin/Release/net10.0');reader=Path('tools/bpmn/DeliveryRecoveryRead/bin/Release/net10.0/DeliveryRecoveryRead.dll')
products=json.loads(subprocess.check_output(['dotnet',str(reader),'--inspect-native-bpmn-products',str(host/'Elsa.Workbench.dll')],text=True));assert products['sourceCommit']==source
(out/'host-products-before.json').write_text(json.dumps(products,indent=2)+'\n')
for f in host.glob('*.dll'):shutil.copyfile(f,current/f.name)
previous=json.loads((base/'all-comparisons.json').read_text());results=[]
for item in previous['results']:
 args=list(item['arguments']);left=Path(args[args.index('-l')+1]);right=current/Path(args[args.index('-r')+1]).name
 assert hashlib.sha256(left.read_bytes()).hexdigest()==item['leftAssemblySha256']
 args[args.index('-r')+1]=str(right)
 parts=args[args.index('--rref')+1].split(',');parts[0]=str(current);args[args.index('--rref')+1]=','.join(parts)
 for part in args[args.index('--lref')+1].split(',')+parts:assert Path(part).exists(),part
 log=out/(item['package_id']+'-'+item['version']+'.log');p=subprocess.run(args,capture_output=True,text=True);log.write_text(p.stdout+p.stderr)
 unresolved=[s for s in (p.stdout+p.stderr).splitlines() if 'unresolved' in s.lower() or 'could not resolve' in s.lower() or 'cannot resolve' in s.lower()]
 r={'package_id':item['package_id'],'version':item['version'],'arguments':args,'exitCode':p.returncode,'leftAssemblySha256':item['leftAssemblySha256'],'rightAssemblySha256':hashlib.sha256(right.read_bytes()).hexdigest(),'unresolvedReferenceLines':unresolved,'log':log.name};results.append(r)
 (out/'all-comparisons.json').write_text(json.dumps({'currentProductSource':source,'publishState':'76d0132a58b8024d99f7ec24ea461ad647bbfc31','scope':'Public binary API shape; no suppressions or version-magnitude permission; baseline inputs unchanged from source04c audit','results':results},indent=2)+'\n')
 print(item['package_id'],item['version'],p.returncode,flush=True)
 if p.returncode or unresolved:raise SystemExit('API shape/reference comparison did not pass')
print('all12 public binary comparisons passed',flush=True)
