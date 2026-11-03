from pathlib import Path
import json,gzip,hashlib
r=Path('/tmp/elsa-2898-review8-closeout')
whole=json.loads((r/'hosting-whole44-command.json').read_text());assert whole['exitCode']==0 and whole['counts']['passed']=='44' and whole['counts']['notExecuted']=='0'
five=json.loads((r/'legacy-host-five-command.json').read_text());assert five['exitCode']==0 and five['counts']['passed']=='5'
gates=json.loads((r/'correction-gates.json').read_text());assert all(x['exitCode']==0 for x in gates) and gates[0]['counts']['passed']=='506'
repo=json.loads((r/'repository-gates.json').read_text());assert len(repo)==5 and all(x['exitCode']==0 for x in repo) and repo[0]['counts']['passed']=='594'
red=json.loads((r/'legacy-cancel-red-command.json').read_text());assert red['counts']['failed']=='1' and red['counts']['passed']=='1'
dest=r/'qualification-portable';dest.mkdir(exist_ok=False);assets=[]
def put(name,p):
 data=p.read_bytes();compress=p.suffix in ['.log','.trx'] or len(data)>100000
 if compress:name+='.gz'
 f=dest/name;f.parent.mkdir(parents=True,exist_ok=True);f.write_bytes(gzip.compress(data,mtime=0) if compress else data);a={'path':name,'sha256':hashlib.sha256(f.read_bytes()).hexdigest(),'bytes':f.stat().st_size}
 if compress:a.update(decodedSha256=hashlib.sha256(data).hexdigest(),decodedBytes=len(data))
 assets.append(a)
for name in ['legacy-cancel-red-command.json','legacy-cancel-red.log','legacy-cancel-red.trx','regression-initial-compile-failure-legacy-cancel-red.log','regression-initial-compile-failure-legacy-cancel-red-command.json','correction-gates.json','bpmn-whole.log','bpmn-whole.trx','producer-build.log','producer-owned-parent-build.log','producer-fixed-build.log','legacy-host-five-command.json','legacy-host-five.log','legacy-host-five.trx','hosting-whole44-command.json','hosting-whole44.log','hosting-whole44.trx','repository-gates.json','architecture.log','architecture.trx','maps.log','tiers.log','tier-docs.log','tier-workflows.log','actual-source-recovery-reconciliation.json','studio-existing-actual-model-boundary.json']:
 put('gates/'+name,r/name)
for p in sorted((r/'host-five').rglob('*')):
 if p.is_file():put('actual-five/'+str(p.relative_to(r/'host-five')),p)
selected=['old-cancel-before-parent-ack-fault','nested-cancel','old-unrepresentable-timer','template-design-upgrade']
for name in selected:
 folder=r/'host-whole44'/name;assert folder.is_dir(),name
 for p in sorted(folder.rglob('*')):
  if p.is_file():put('whole44-selected/'+str(p.relative_to(r/'host-whole44')),p)
put('commands/prepare-qualification.py',Path(__file__))
summary={'correctionSource':'e83fc58f6fa84a7cbeb72678729aa1b78c6b1ca9','actualProducerBeforeCorrection':'1380608ead98dabf0f2e90576bdcd6f8f6444aaa','wholeBpmn':gates[0],'wholeHosting44':whole,'actualFiveOldFaults':five,'repositoryGates':repo,'regressionNegative':red,'initialRegressionCompileFailure':'Missing model-state import; no tests ran, retained separately','actualSourceRecovery':json.loads((r/'actual-source-recovery-reconciliation.json').read_text()),'actualSavedStudioBoundary':json.loads((r/'studio-existing-actual-model-boundary.json').read_text()),'earlierStudioAndFrontendSourcesRetained':True,'publicApiAuditUnchanged':{'passed':15,'executingSource':'0f5f6c6efaada666f40766478ef63ad488b28e58','changeOnlyInternalHandlerBody':True},'completedFullRounds':8,'cleanFullRounds':0,'authorizedLimit':16,'nextRound':9,'requiredCandidateCiGreenClaimed':False,'mergedClaimed':False,'programComplete':False}
(dest/'summary.json').write_text(json.dumps(summary,indent=2)+'\n');assets.append({'path':'summary.json','sha256':hashlib.sha256((dest/'summary.json').read_bytes()).hexdigest(),'bytes':(dest/'summary.json').stat().st_size});(dest/'manifest.json').write_text(json.dumps({'schemaVersion':1,'kind':'round8-legacy-cancellation-correction-qualification','sourceRevision':whole['source'],'assets':assets},indent=2)+'\n');print('Prepared',len(assets),'assets',round(sum(x['bytes'] for x in assets)/1e6,2),'MB')
