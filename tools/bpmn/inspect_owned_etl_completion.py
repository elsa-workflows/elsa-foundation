#!/usr/bin/env python3
"""Reopen only this controller's completed private fixture for read-only Studio inspection.

Without --upgrade-archive, DIRECTORY is the controller's own output and its private root must still exist;
the inspection hosts must be the exact capture products. With --upgrade-archive, DIRECTORY is a new output:
the pinned archive's finished stores are restored into a fresh private root, the legacy capture's product
identities are validated and recorded, and the supplied newer products must be built from --product-source.
"""
import argparse,gzip,hashlib,importlib.util,json,os,shutil,subprocess,tempfile,time,urllib.request,zipfile
from pathlib import Path, PurePosixPath

REPO=Path(__file__).resolve().parents[2]
ARCHIVE_RECORD=REPO/'specs/1184-direct-bpmn-studio/evidence/etl/actual-recovery-archive.json'
HARNESS=('tools/bpmn/','e2e-tests/etl/','specs/1184-direct-bpmn-studio/')
STORES=('runtime','design','etl-runtime','etl-design')
p=argparse.ArgumentParser(description=__doc__);p.add_argument('directory',type=Path);p.add_argument('--workbench',type=Path,required=True);p.add_argument('--studio',type=Path,required=True);p.add_argument('--reader',type=Path,required=True)
p.add_argument('--upgrade-archive',type=Path,help='Pinned legacy recovery archive to restore and inspect with newer products.');p.add_argument('--product-source',help='Committed source that built the newer products (upgrade mode).');args=p.parse_args()
sha=lambda path:hashlib.sha256(path.read_bytes()).hexdigest()
git=lambda *command:subprocess.check_output(['git',*command],cwd=REPO,text=True).strip()
reader=lambda *command:json.loads(subprocess.check_output(['dotnet',str(args.reader.resolve()),*command],text=True))


def controller_module():
    spec=importlib.util.spec_from_file_location('owned_studio_upgrade',REPO/'tools/bpmn/run_owned_studio_upgrade.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module


def restore_upgrade(output):
    """Verify the pinned archive and both product identities, then restore a fresh private root from its finished stores."""
    record=json.loads(ARCHIVE_RECORD.read_text());archive=args.upgrade_archive.resolve(strict=True)
    if archive.stat().st_size!=record['bytes'] or sha(archive)!=record['sha256']:raise ValueError('The legacy archive differs from its pinned size and SHA-256.')
    if not args.product_source:raise ValueError('Upgrade inspection needs the committed --product-source of the newer products.')
    harness=git('rev-parse','HEAD');product=git('rev-parse',args.product_source+'^{commit}')
    if git('status','--porcelain','--untracked-files=all'):raise ValueError('Commit the inspection harness before recording upgrade evidence.')
    extracted=Path(tempfile.mkdtemp(prefix='elsa-etl-legacy-'))
    with zipfile.ZipFile(archive) as source:
        names=[item.filename for item in source.infolist() if not item.is_dir()]
        if len(names)!=record['files']:raise ValueError('The legacy archive inventory differs from its pinned receipt.')
        for name in names:
            relative=PurePosixPath(name)
            if relative.is_absolute() or '..' in relative.parts:raise ValueError('Unexpected archive path: '+name)
        source.extractall(extracted)
    manifest=json.loads((extracted/'manifest.json').read_text())
    if len(manifest['assets'])+1!=record['files'] or any(sha(extracted/asset['path'])!=asset['sha256'] for asset in manifest['assets']):raise ValueError('An archived member differs from its manifest.')
    legacy=extracted/'recovery';ready=json.loads((legacy/'ready.json').read_text());previous=json.loads((legacy/'process.json').read_text())
    legacy_source=manifest['metadata']['productSource']
    assert ready['family']=='direct-etl' and json.loads((legacy/'browser-result.json').read_text())['passed'] is True
    assert previous['sourceRevision']==ready['sourceRevision']==legacy_source==record['coldVerification']['productSource']
    assert manifest['metadata']['completedOccurrenceNavigationPassed'] is False,'The archive no longer records the legacy navigation failure.'
    legacy_products={}
    for role in ('workbench','studio'):
        hashes={item['assemblySha256'] for item in previous['processes'] if item['role']==role}
        if len(hashes)!=1:raise ValueError('The legacy capture used more than one '+role+' product.')
        legacy_products[role]=hashes.pop()
    assert previous['studioAssemblySha256']==legacy_products['studio']
    host=reader('--inspect-host-products',str(args.workbench.resolve()));studio=reader('--inspect-studio-products',str(args.studio.resolve()))
    if host['sourceCommit']!=product or studio['sourceCommit']!=product:raise ValueError('Build both newer products from the exact committed --product-source.')
    subprocess.run(['git','merge-base','--is-ancestor',legacy_source,product],cwd=REPO,check=True)
    subprocess.run(['git','merge-base','--is-ancestor',product,harness],cwd=REPO,check=True)
    if any(not controller_module().product_neutral(name) for name in git('diff','--name-only',product,harness).splitlines()):raise ValueError('Product inputs changed after --product-source; rebuild the products.')
    new_products={'workbench':sha(args.workbench.resolve()),'studio':sha(args.studio.resolve())}
    # The legacy guard becomes explicit: these are different, identified products, never an unnoticed rebuild.
    if any(new_products[role]==legacy_products[role] for role in new_products):raise ValueError('Upgrade inspection needs newer products than the legacy capture.')
    controller=importlib.util.spec_from_file_location('owned_studio_upgrade',REPO/'tools/bpmn/run_owned_studio_upgrade.py');module=importlib.util.module_from_spec(controller);controller.loader.exec_module(module)
    private=Path(tempfile.mkdtemp(prefix='elsa-etl-upgrade-'));backend_root=private/'workbench';studio_root=private/'studio';etl_root=private/'etl'
    backend_root.mkdir();studio_root.mkdir()
    finished=previous['stages']['finished'];restored={}
    for name in STORES:
        target=backend_root/(name+'.db')
        with gzip.open(legacy/finished[name]['file'],'rb') as compressed,target.open('wb') as raw:shutil.copyfileobj(compressed,raw)
        if sha(target)!=finished[name]['databaseSha256']:raise ValueError('A restored legacy store differs from its capture: '+name)
        restored[name]=finished[name]['databaseSha256']
    files=json.loads((legacy/'finished-etl-files.json').read_text())['files']
    for folder in ('main-output','error-output','snapshots'):(etl_root/folder).mkdir(parents=True)
    for item in files:
        relative=PurePosixPath(item['file'])
        if relative.is_absolute() or '..' in relative.parts or relative.parts[0] not in ('main-output','error-output','snapshots'):raise ValueError('Unexpected ETL file: '+item['file'])
        target=etl_root/relative;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(legacy/'finished-etl-files'/relative,target)
        if sha(target)!=item['sha256']:raise ValueError('A restored ETL file differs from its capture: '+item['file'])
    (etl_root/'lookup.csv').write_text(module.LOOKUP)
    backend_url=f'http://127.0.0.1:{module.free_port()}';studio_url=f'http://127.0.0.1:{module.free_port()}'
    shells,features=module.compose_host_roots(backend_root,studio_root,backend_url,studio_url,direct_human=True)
    # No dependency fixture runs: a read-only inspection must not reach the HTTP source.
    module.compose_direct_etl(features,etl_root,f'http://127.0.0.1:{module.free_port()}')
    module.write_json(backend_root/'shells.json',shells)
    output.mkdir(parents=True,exist_ok=False)
    for name in ('pipeline-selection.json','finished-typed-read.json'):shutil.copyfile(legacy/name,output/name)
    (output/'ready.json').write_text(json.dumps({'family':'direct-etl','sourceRevision':product,'harnessSourceRevision':harness,'legacySourceRevision':legacy_source,
        'backendUrl':backend_url,'studioUrl':studio_url,'privateRoot':str(private)},indent=2)+'\n')
    upgrade={'archive':{'name':record['name'],'sha256':record['sha256'],'files':record['files']},'legacy':{'productSource':legacy_source,'assemblySha256':legacy_products,
        'hostProducts':previous['hostProducts']['actualProductVersions'],'restoredStage':'finished','restoredDatabaseSha256':restored,'etlFiles':len(files),
        'navigationFailureLog':{'file':'checks/actual-etl-replacement-inspection.log','sha256':sha(extracted/'checks/actual-etl-replacement-inspection.log')}},
        'product':{'source':product,'assemblySha256':new_products,'hostProducts':host['actualProductVersions'],'studioProducts':studio['actualProductVersions']},'harnessSource':harness}
    return output,private,upgrade,{role:new_products[role] for role in new_products}


if args.upgrade_archive:
    root,private,upgrade,expected=restore_upgrade(args.directory.resolve());ready=json.loads((root/'ready.json').read_text())
else:
    root=args.directory.resolve();ready=json.loads((root/'ready.json').read_text());previous=json.loads((root/'process.json').read_text());upgrade=None
    private=Path(ready['privateRoot'])
    assert ready['family']=='direct-etl' and (root/'browser-result.json').exists()
    assert json.loads((root/'browser-result.json').read_text())['passed'] is True
    expected={role:next(item for item in previous['processes'] if item['role']==role)['assemblySha256'] for role in ('workbench','studio')}
children=[];logs=[];records=[];label='upgrade-inspection' if upgrade else 'replacement-inspection'
assert not (root/(label+'-controller.json')).exists()
try:
 for role,assembly,folder,url in [('workbench',args.workbench.resolve(),'workbench',ready['backendUrl']),('studio',args.studio.resolve(),'studio',ready['studioUrl'])]:
  assert sha(assembly)==expected[role]
  log=(root/(label+'-'+role+'.log')).open('w');logs.append(log)
  child=subprocess.Popen(['dotnet',str(assembly),'--contentRoot',str(private/folder),'--urls',url],cwd=private/folder,env={**os.environ,'ASPNETCORE_ENVIRONMENT':'Development'},stdout=log,stderr=subprocess.STDOUT);children.append(child)
  records.append({'role':role,'processId':child.pid,'controllerProcessId':os.getpid(),'assemblySha256':sha(assembly)})
 for child,record in zip(children,records):
  url=ready['backendUrl']+'/health/ready' if record['role']=='workbench' else ready['studioUrl'];deadline=time.monotonic()+(180 if upgrade else 60)
  while True:
   assert child.poll() is None,'Owned inspection host exited before readiness'
   try:
    with urllib.request.urlopen(url,timeout=2) as response:
     if response.status==200:break
   except (OSError,TimeoutError):pass
   if time.monotonic()>deadline:raise TimeoutError('Owned inspection host readiness')
   time.sleep(.2)
 subprocess.run(['node','tools/bpmn/inspect_completed_etl_studio.mjs',str(root),*(['--with-original'] if upgrade else [])],cwd=REPO,check=True)
finally:
 for child in reversed(children):
  if child.poll() is None:
   # Popen still owns this exact child; no PID search or unrelated process is stopped.
   child.terminate()
   try:child.wait(timeout=20)
   except subprocess.TimeoutExpired:child.kill();child.wait(timeout=10)
 for log in logs:log.close()
original=json.loads((root/'finished-typed-read.json').read_text())['models'][0]
execution=original['model']['executionId']
observed=reader('--read-bpmn-direct',str(private/'workbench/runtime.db'),execution)
assert observed==original,'Read-only replacement inspection changed model receipts or native state'
result={'passed':True,'readOnlyUi':True,'modelAndReceiptsUnchanged':True,'productSource':ready['sourceRevision'],'processes':records,'allChildrenStopped':all(child.poll() is not None for child in children)}
if upgrade:
 # Startup may maintain provider metadata; only the typed model/receipt equality above is asserted.
 upgrade['storesAfterInspection']={name:sha(private/'workbench'/(name+'.db')) for name in STORES}
 result.update(upgrade=upgrade,originalFaultNavigation=True)
(root/(label+'-controller.json')).write_text(json.dumps(result,indent=2)+'\n')
print(('Upgrade' if upgrade else 'Replacement')+' Studio inspection passed; no execution commands, unchanged model/receipts; all owned hosts stopped.')
