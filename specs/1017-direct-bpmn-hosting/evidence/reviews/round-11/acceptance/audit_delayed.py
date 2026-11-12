import pathlib,json,sqlite3,base64,hashlib,gzip,datetime
out=pathlib.Path('/tmp/elsa-2898-review11-acceptance');r=pathlib.Path('/Users/sipke/.codex/worktrees/f236/elsa-foundation/specs/1017-direct-bpmn-hosting/evidence/reviews/round-10/corrections/qualification')
def rows(h,t):
 db=sqlite3.connect(f'file:{out/(h+".sqlite")}?mode=ro&immutable=1',uri=True);a=[json.loads(x[0]) for x in db.execute('select ContentJson from '+t)];db.close();return a
def dec(s):return base64.b64decode(s).decode('utf-16-le')
original='dedc7041baf31ad6d0b3de251049de7d55ff421dbe7e65e3059b6e35f9bf9e63'
oldmodel=rows(original,'elsa_runtime_model_execution')[0];oldworker=rows(original,'elsa_runtime_workflow_execution_state')[0];oldout={dec(x['outboxItemId']):x for x in rows(original,'elsa_runtime_post_commit_outbox')}
results=[]
for family in ['selected-host','host-whole']:
 root=r/family/'evidence/delayed-compensation';case=json.loads((root/'case.json').read_text());new=case['completedStore']['databaseSha256'];keep=case['retainedStore']['databaseSha256'];assert new==keep
 assert case['first']!=case['second']; assert case['firstExit']==case['secondExit']==137
 m=rows(new,'elsa_runtime_model_execution')[0];w=rows(new,'elsa_runtime_workflow_execution_state')[0];sources={dec(x['outboxItemId']):x for x in rows(new,'elsa_runtime_post_commit_outbox')}
 assert oldworker==w and w['status']=='Cancelled'
 assert rows(original,'elsa_runtime_workflow_executable')==rows(new,'elsa_runtime_workflow_executable')
 assert oldmodel['lifecycle']=='TerminalPendingSettlement' and m['lifecycle']=='TerminalSettled';assert oldmodel['revision']==2 and m['revision']==3
 for k in ['executable','materialIncarnationId','authority','tenantId','terminalResult']:assert oldmodel[k]==m[k],k
 assert oldmodel['payload']['state']==m['payload']['state']
 retry=case['retrySource'];result=case['resultSource'];assert sources[retry]==oldout[retry];assert sources[retry]['status']=='Pending'
 assert sources[result]['intent']==oldout[result]['intent'];assert sources[result]['status']=='Delivered'
 assert oldout[result]['intent']['payload']==m['payload']['nativeResults'][0]
 due=datetime.datetime.fromisoformat(sources[retry]['availableAt']);completed=datetime.datetime.fromisoformat(case['completedAt']);assert completed<due
 ins=rows(new,'elsa_runtime_model_input');newin=[x for x in ins if dec(x['inputKind'])=='native-result'];assert len(newin)==1 and newin[0]['acceptedRevision']==3
 assert datetime.datetime.fromisoformat(newin[0]['acceptedAt']) <= completed < due;assert newin[0]['result']['lifecycle']=='TerminalSettled'
 assert not rows(new,'elsa_runtime_scheduler_work_item')
 def markers(h):
  db=sqlite3.connect(f'file:{out/(h+".sqlite")}?mode=ro&immutable=1',uri=True);c=db.execute('select * from elsa_runtime_checkpoint_commit');ns=[x[0] for x in c.description];d={row[0]:row for row in c};db.close();return d
 before=markers(original);after=markers(new);assert all(after.get(k)==v for k,v in before.items())
 results.append({'family':family,'oldModelRevision':oldmodel['revision'],'newModelRevision':m['revision'],'workerAndResultIntentUnchanged':True,'semanticStateAuthorityFullPinMaterialUnchanged':True,'originalMarkersRetained':len(before),'futureRetryUnchangedPending':True,'settledAt':case['completedAt'],'futureRetryDue':str(due),'wholeColdPairSha256':new,'coldPids':[case['first'],case['second']]})
(out/'delayed-physical-audit.json').write_text(json.dumps(results,indent=2));print(json.dumps(results,indent=2))
