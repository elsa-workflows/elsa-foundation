from pathlib import Path
from contextlib import closing
import gzip,json,sqlite3,tempfile,hashlib
R=Path(__import__('os').environ.get('ELSA_REVIEW_REPOSITORY', Path.cwd())).resolve();O=Path(__file__).resolve().parent;E=R/'specs/1017-direct-bpmn-hosting/evidence/reviews/round-5';rows=[]
with tempfile.TemporaryDirectory(prefix='r6-acceptance-readonly-',dir=O) as temporary:
 for f in list((E/'qualification/fault-recovery').rglob('*.sqlite.gz'))+list((E/'frontend-adoption').rglob('*.sqlite.gz')):
  b=gzip.decompress(f.read_bytes());p=Path(temporary)/(str(len(rows))+'.sqlite');p.write_bytes(b)
  with closing(sqlite3.connect(p.as_uri()+'?mode=ro&immutable=1',uri=True)) as c:integrity=c.execute('PRAGMA integrity_check').fetchall()
  rows.append({'file':str(f.relative_to(R)),'databaseSha256':hashlib.sha256(b).hexdigest(),'readOnly':True,'integrity':integrity,'passed':integrity==[('ok',)]})
assert len(rows)==16 and all(r['passed'] for r in rows)
checks=[]
for name in ['original','layout']:
 d=E/'frontend-adoption'/name;q=E/'qualification/studio'/name
 api=json.loads((d/'test-run-api.json').read_text());instance=api['instance'];c={'family':name,'byteExactJsonTo04c':(d/'open-export.json').read_bytes()==(q/'open-export.json').read_bytes(),'byteExactXmlTo04c':(d/'open-export.bpmn').read_bytes()==(q/'open-export.bpmn').read_bytes(),'originalRunViewsStable':json.loads((d/'finished-legacy-views.json').read_text())==json.loads((d/'recovered-legacy-views.json').read_text()),'instanceStatus':instance['status'],'executionId':instance['id'] if 'id' in instance else instance.get('workflowExecutionId'),'recordedBrowserCase':json.loads((d/'browser-case.json').read_text())}
 assert c['byteExactJsonTo04c'] and c['byteExactXmlTo04c'] and c['originalRunViewsStable'] and c['instanceStatus']=='Completed';checks.append(c)
(O/'closed-store-audit.json').write_text(json.dumps({'stores':rows,'source9cChecks':checks},indent=2));print('16 immutable read-only whole stores: integrity OK; both source9c JSON/XML exports exact, original-run views stable, actual Test Runs Completed')
