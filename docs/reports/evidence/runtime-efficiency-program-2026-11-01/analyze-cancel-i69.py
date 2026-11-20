import json,pathlib,sys
# Root holds the extracted cancel-i69 evidence archive (runner/<series>/ledger.json, w-a/, w-b/).
r=pathlib.Path(sys.argv[1]);out={}
for s in ['x1','x2','x3']:
    l=[x for x in json.loads((r/'runner'/s/'ledger.json').read_text()) if 'shape' in x]
    out[s]=[{k:x.get(k) for k in ['shape','passed','error','stateBeforeCancel','stateAfter','ownerApiView','receiptStatuses','secondsToSettledReceipt','activityRowsProgressingBefore','activityRowsAfterCancel','transportRows','incidents']}
            | {'sender':(x.get('senderFinished') or {}).get('status'),'senderHostId':json.loads((r/'w-b'/f"control-{s}-{x['shape']}"/'ready.json').read_text()).get('hostId'),
               'placementBefore':[(p['OwnerId'],p['Revision']) for p in x.get('placementBefore',[])],'placementAfter':[(p['OwnerId'],p['Revision']) for p in x.get('placementAfter',[])]} for x in l]
# EF logs a PostgreSQL 40001 serialization conflict at fail level before the transport's bounded compare-and-swap retries it.
logs=sorted((r/'w-a').glob('*.log'))+sorted((r/'w-b').glob('*.log'))
def saves(text): return text.split('fail: Microsoft.EntityFrameworkCore.Update[10000]')[1:]
out['nodeLogs']={p.name:{'efSaveFailures':len(saves(p.read_text())),
                         'serializationConflicts':sum('40001: could not serialize access' in b.split('\nfail: ')[0] for b in saves(p.read_text())),
                         'failLines':sum(1 for line in p.read_text().splitlines() if line.startswith('fail:'))} for p in logs}
print(json.dumps(out,indent=1))
