from pathlib import Path
import subprocess,json,gzip,hashlib,shutil
root=Path('/tmp/elsa-2898-auth-pass-1');target=root/'studio';target.mkdir(exist_ok=False)
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest();baseline=Path('specs/1017-direct-bpmn-hosting/evidence/baseline');b=json.loads((baseline/'manifest.json').read_text());assets=[];cases=[]
for family in ['original','layout']:
 source=root/('diagnostic-original' if family=='original' else 'layout');dest=target/family;dest.mkdir();case=json.loads((source/'case.json').read_text());assert case['screenshotsVisuallyInspected'] is True;cases.append(case);assert (source/'process.json').exists()
 names={'definition-after.json','open-export.json','open-export.bpmn','edited-export.json','edited-export.bpmn','published-run-api.json','test-run-api.json','restarted-new-views.json'}
 for stage in ['recovered','restarted','finished']:names.update(stage+s for s in ['-runtime.sqlite.gz','-design.sqlite.gz','-frozen-contract.json','-legacy-views.json'])
 for stage in ['open','published','published-run','test-run','legacy-run','restarted-test-run' if family=='original' else 'restarted-published-run']:
  names.add(stage+'-dom.txt');png=source/(stage+'.png');jpg=dest/(stage+'.jpg');subprocess.run(['/usr/bin/sips','-s','format','jpeg',str(png),'--out',str(jpg)],check=True,capture_output=True);assets.append({'path':family+'/'+jpg.name,'sha256':sha(jpg)})
 for name in sorted(names):
  shutil.copyfile(source/name,dest/name);assets.append({'path':family+'/'+name,'sha256':sha(dest/name)})
 data=(source/'process.json').read_bytes();p=dest/'process.json.gz';p.write_bytes(gzip.compress(data,mtime=0));assets.append({'path':family+'/'+p.name,'sha256':sha(p),'decodedSha256':hashlib.sha256(data).hexdigest(),'decodedBytes':len(data)})
manifest={'schemaVersion':2,'kind':'actual-existing-studio-upgrade','sourceRevision':'82f6202a5f2e56521525092f0d06698fdeb7d10c','baselineManifestSha256':sha(baseline/'manifest.json'),'baselineAssets':{x['path']:x['sha256'] for x in b['assets'] if x['path'].startswith('studio/') or x['path'] in ['multi-instance-and-studio/runtime.sqlite.gz','multi-instance-and-studio/design.sqlite.gz']},'cases':cases,'assets':assets}
(target/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n');print('packaged',len(assets),'actual Studio proof assets')
