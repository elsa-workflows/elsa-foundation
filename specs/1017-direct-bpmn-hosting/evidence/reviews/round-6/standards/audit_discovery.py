#!/usr/bin/env python3
"""Reconcile retained list-tests output and raw TRX, without executing tests."""
from pathlib import Path
import argparse,json,gzip,xml.etree.ElementTree as E
p=argparse.ArgumentParser();p.add_argument('--repo',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
b=a.repo/'specs/1017-direct-bpmn-hosting/evidence/reviews/round-5/qualification/gates';out=[]
ns={'x':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
for f in sorted((b/'discovery').glob('*.log.gz')):
 name=f.stem[:-4];suite='standard-host' if name=='hosting' else name
 listed=[line.strip() for line in gzip.decompress(f.read_bytes()).decode().splitlines() if line.startswith('    ') and line.strip()]
 lm={line.split('(',1)[0] for line in listed}
 r=E.fromstring(gzip.decompress((b/(suite+'.trx.gz')).read_bytes()))
 tm={t.attrib['id']:t.find('x:TestMethod',ns).attrib for t in r.findall('x:TestDefinitions/x:UnitTest',ns)}
 em={tm[x.attrib['testId']]['className']+'.'+tm[x.attrib['testId']]['name'] for x in r.findall('x:Results/x:UnitTestResult',ns)}
 out.append({'suite':suite,'listedCases':len(listed),'distinctListedMethods':len(lm),'distinctExecutedMethods':len(em),'missing':sorted(lm-em),'extra':sorted(em-lm)})
a.output.write_text(json.dumps(out,indent=2)+'\n')
print(json.dumps(out,indent=2))
raise SystemExit(1 if any(x['missing'] or x['extra'] for x in out) else 0)
