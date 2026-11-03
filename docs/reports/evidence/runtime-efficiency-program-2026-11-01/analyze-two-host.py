import json,pathlib,sys,collections
# Root holds the extracted i68 evidence archive (runner/<series>/ledger.json, w1/, w2/).
r=pathlib.Path(sys.argv[1])/'runner';out={}
def load(s):
    p=r/s/'ledger.json'
    return json.loads(p.read_text()) if p.exists() else []
for s in ['shake-r1','failure-r1','failure-r2','f3','f4']:
    l=load(s);c=collections.Counter(f"{x['kind']}:{'pass' if x['passed'] else 'fail'}" for x in l)
    out[s]={'cases':len(l),'outcomes':dict(c),'failures':[{k:x.get(k) for k in ['kind','case','error','recoveryExit','correct','incidentsAfterRecovery']} for x in l if not x['passed']]}
for s in ['loopab-branch','loopab-main','loopdiag-branch','loopfix-branch']:
    out[s]=[{'workbench':x['workbench'],'index':x['index'],'dispatch':x.get('run',{}).get('commandDispatchStatus'),'activityRows':x.get('activityRows'),
             'poisonIncidents':len(x.get('incidentSummaries',[])),'firstChanceConflicts':len(x.get('conflicts',[])),
             'conflictEntities':sorted({e['entity'] for c in x.get('conflicts',[]) for e in c['entries']})} for x in load(s)]
for s in ['c1','c2','c3','c4','c5','c6','c7','p-main','p-branch']:
    out[s]=[{k:x.get(k) for k in ['shape','passed','error','stateBeforeCancel','stateAfter','ownerApiView','receiptStatuses','activityRowsProgressingBefore','activityRowsAfterCancel','incidents']} | {'sender':(x.get('senderFinished') or {}).get('status'),'senderMessage':(x.get('senderFinished') or {}).get('message')} for x in load(s) if 'shape' in x]
print(json.dumps(out,indent=1))
