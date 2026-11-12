"""Read-only semantic/integrity checks over actual closed standard-host captures; controls alter copies only."""
from pathlib import Path
import copy,gzip,hashlib,json
SOURCE='e3ec219fcc3a93c949d2669fc585bd1e79152abe'
def verify(case,prepared,completed,retained):
 assert case['sourceRevision']==SOURCE
 assert all(case[k]==137 for k in ['firstExit','secondExit','thirdExit'])
 assert len({case[k]for k in ['first','second','third']})==3
 assert all(x['readOnly']for x in [prepared,completed,retained])
 assert prepared['model']is None and len(prepared['bookmarks'])==1
 assert completed==retained
 model=completed['model'];direct=completed['directCounterpart'];pin=case['published']['executable']
 assert model['executionId']==case['modelExecutionId']and direct['executionId']==case['directExecutionId']
 assert model['executable']==direct['executable']==pin
 assert model['materialIncarnationId']==direct['materialIncarnationId']==case['published']['incarnationId']
 assert model['lifecycle']==direct['lifecycle']==3 and model['revision']==2 and direct['revision']==1
 assert direct==prepared['directCounterpart']
 assert case['final']['status']=='TerminalSettled'and case['nativeFinal']['instance']['status']=='Completed'
 assert case['final']['result']==case['directFinal']['result']and case['final']['result']['status']=='Completed'
 inputs={x['inputKind']:x for x in completed['inputs']};assert set(inputs)=={'start-embedded','native-parent-settled'}
 assert inputs['start-embedded']['acceptedRevision']==1 and inputs['start-embedded']['result']['lifecycle']=='TerminalPendingSettlement'
 assert inputs['native-parent-settled']['acceptedRevision']==2 and inputs['native-parent-settled']['result']['lifecycle']=='TerminalSettled'
 assert sorted(x['intent']['kind']for x in completed['sources'])==['Elsa.Bpmn.ResumeNativeParent.v1','Elsa.Bpmn.StartEmbedded.v1']
 assert all(x['status']==2 for x in completed['sources'])and not completed['bookmarks']and not completed['queuedModelInputs']
 for file in ['Elsa.Workbench.dll','Elsa.Activities.Bpmn.dll','Elsa.Workflows.Runtime.Core.dll','Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.dll','Elsa.Persistence.EntityFramework.dll']:
  assert SOURCE in next(x for x in case['binaries']if x['file']==file)['productVersion']
def main(root):
 outcomes=[];negative=0
 for folder in sorted((root/'journeys').iterdir()):
  if not folder.is_dir():continue
  records=[json.loads((folder/name).read_text())for name in ['case.json','prepared.json','completed.json','retained.json']];verify(*records)
  for snap in records[0]['snapshots']:
   data=(folder/snap['file']).read_bytes();assert hashlib.sha256(data).hexdigest()==snap['gzipSha256'];assert hashlib.sha256(gzip.decompress(data)).hexdigest()==snap['databaseSha256']
  for mutation in ['running-first','different-incarnation','missing-source','graceful-exit','missing-settlement','changed-retained']:
   case,prepared,completed,retained=copy.deepcopy(records)
   if mutation=='running-first':next(x for x in completed['inputs']if x['inputKind']=='start-embedded')['result']['lifecycle']='Running';retained=copy.deepcopy(completed)
   elif mutation=='different-incarnation':completed['model']['materialIncarnationId']='foreign';retained=copy.deepcopy(completed)
   elif mutation=='missing-source':completed['sources'].pop();retained=copy.deepcopy(completed)
   elif mutation=='graceful-exit':case['firstExit']=0
   elif mutation=='missing-settlement':completed['inputs']=[x for x in completed['inputs']if x['inputKind']!='native-parent-settled'];retained=copy.deepcopy(completed)
   else:retained['model']['revision']+=1
   try:verify(case,prepared,completed,retained)
   except AssertionError:negative+=1
   else:raise AssertionError('Observation control was accepted: '+mutation)
  outcomes.append({'parent':records[0]['parent'],'actualCompleted':True,'wholeStores':4,'ownedHosts':3,'syntheticObservationControlsRejected':6})
 assert len(outcomes)==4 and negative==24
 return{'executingSource':SOURCE,'actualJourneys':outcomes,'wholeStores':16,'coupledPairs':8,'ownedHosts':12,'syntheticObservationControlsRejected':negative,'controlsAreRuntimeAcceptance':False}
if __name__=='__main__':print(json.dumps(main(Path(__file__).resolve().parents[1]),indent=2))
