import pathlib,json,hashlib,gzip,subprocess,xml.etree.ElementTree as ET
R=pathlib.Path(__import__('os').environ.get('ELSA_REVIEW_REPOSITORY', pathlib.Path.cwd())).resolve();E=R/'specs/1017-direct-bpmn-hosting/evidence';O=pathlib.Path(__file__).resolve().parent
errors=[];records=[];decoded=0
for f in E.rglob('*.json'):
 try:d=json.loads(f.read_text())
 except (ValueError,UnicodeError):continue
 if not isinstance(d,dict) or not isinstance(d.get('assets'),list):continue
 for a in d['assets']:
  if not isinstance(a,dict) or not isinstance(a.get('path'),str) or not isinstance(a.get('sha256'),str):continue
  p=f.parent/a['path']; row={'manifest':str(f.relative_to(R)),'path':a['path']}
  try:
   b=p.read_bytes();actual=hashlib.sha256(b).hexdigest();row.update(present=True,sha256=actual,hashMatches=actual==a['sha256'])
   if actual!=a['sha256']: errors.append({**row,'error':'hash mismatch'})
   if a.get('bytes') is not None and a['bytes']!=len(b):errors.append({**row,'error':'byte size differs'})
   if p.suffix=='.gz' and any(k in a for k in ('decodedSha256','databaseSha256')):
    q=gzip.decompress(b);decoded+=1
    for key in ('decodedSha256','databaseSha256'):
     if key in a and hashlib.sha256(q).hexdigest()!=a[key]:errors.append({**row,'error':key+' differs'})
    for key in ('decodedBytes','uncompressedBytes'):
     if key in a and len(q)!=a[key]:errors.append({**row,'error':key+' differs'})
  except Exception as ex: row['error']=str(ex);errors.append(row)
  records.append(row)
summary={'reviewedHead':subprocess.check_output(['git','rev-parse','HEAD'],cwd=R,text=True).strip(),'manifestAssets':len(records),'uniqueAssets':len({(r['manifest'],r['path']) for r in records}),'gzipPayloadsChecked':decoded,'errors':errors}
(O/'manifest-assets.json').write_text(json.dumps(records,indent=2));(O/'manifest-audit.json').write_text(json.dumps(summary,indent=2));print(json.dumps(summary,indent=2))
trs=[]
for f in (E/'reviews/round-5/qualification/gates').glob('*.trx.gz'):
 root=ET.fromstring(gzip.decompress(f.read_bytes()));ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};c=root.find('t:ResultSummary/t:Counters',ns);results=root.findall('t:Results/t:UnitTestResult',ns)
 trs.append({'file':f.name,'counters':c.attrib,'actualResults':len(results),'outcomes':{x:sum(r.attrib.get('outcome')==x for r in results) for x in {r.attrib.get('outcome') for r in results}}})
(O/'trx-audit.json').write_text(json.dumps(trs,indent=2));print('TRX',[(t['file'],t['actualResults'],t['outcomes']) for t in trs])
