from pathlib import Path
import subprocess,json,shutil,hashlib
repo=Path.cwd();source='1cee5acd4e7d7adb01dd467342641a108e349d60';assert subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()==source
out=Path('/tmp/elsa-2898-main-c4cfa-qualification/api-binary');out.mkdir(exist_ok=True);current=out/'current';current.mkdir(exist_ok=True)
root=Path('/tmp/elsa-2898-main-c4cfa-qualification');latest=root/'api-latest';sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();host=repo/'Essentials/apps/Elsa.Workbench/bin/Release/net10.0';reader=repo/'tools/bpmn/DeliveryRecoveryRead/bin/Release/net10.0/DeliveryRecoveryRead.dll'
products=json.loads(subprocess.check_output(['dotnet',str(reader),'--inspect-native-bpmn-products',str(host/'Elsa.Workbench.dll')],text=True));assert products['sourceCommit']==source
(out/'host-products-before.json').write_text(json.dumps(products,indent=2)+'\n')
for f in host.glob('*.dll'):shutil.copyfile(f,current/f.name)
previous=json.loads(Path('/tmp/elsa-2898-round5-final-qualification/api-binary/all-comparisons.json').read_text());comparisons=list(previous['results']);template=comparisons[0]['arguments'];frameworks=template[template.index('--rref')+1].split(',')[1:]
for item in json.loads((latest/'downloads.json').read_text()):
 args=[template[0],'-l',str(latest/'published'/item['assemblyFile']),'-r',str(current/item['assemblyFile']),'--lref',','.join([str(latest/'references'),*frameworks]),'--rref',','.join([str(current),*frameworks]),'--enable-rule-cannot-change-parameter-name','--verbosity','High']
 comparisons.append({'package_id':item['package_id'],'version':item['version'],'arguments':args,'leftAssemblySha256':item['assemblySha256']})
results=[]
for item in comparisons:
 args=list(item['arguments']);left=Path(args[args.index('-l')+1]);right=current/Path(args[args.index('-r')+1]).name;assert sha(left)==item['leftAssemblySha256'];args[args.index('-r')+1]=str(right)
 parts=args[args.index('--rref')+1].split(',');parts[0]=str(current);args[args.index('--rref')+1]=','.join(parts)
 for part in args[args.index('--lref')+1].split(',')+parts:assert Path(part).exists(),part
 log=out/(item['package_id']+'-'+item['version']+'.log');p=subprocess.run(args,capture_output=True,text=True);log.write_text(p.stdout+p.stderr);unresolved=[s for s in (p.stdout+p.stderr).splitlines() if any(v in s.lower() for v in ['unresolved','could not resolve','cannot resolve'])]
 results.append({'package_id':item['package_id'],'version':item['version'],'arguments':args,'exitCode':p.returncode,'leftAssemblySha256':item['leftAssemblySha256'],'rightAssemblySha256':sha(right),'unresolvedReferenceLines':unresolved,'log':log.name})
 (out/'all-comparisons.json').write_text(json.dumps({'currentProductSource':source,'publishState':'51337684e4fb18ec25549ae8ba3bef1f5a3cacb7','scope':'Public binary API shape, full resolved references, parameter-name rules, no suppressions. Original12 comparisons plus3 latest public baselines. No pack/publish/version reservation. Scoped4.0-preview exception retained in PR3410.','results':results},indent=2)+'\n')
 print(item['package_id'],item['version'],p.returncode,flush=True)
 if unresolved:raise SystemExit('API reference resolution did not pass')
assert len(results)==15;print('all15 comparisons completed; inspect each exitCode',flush=True)
