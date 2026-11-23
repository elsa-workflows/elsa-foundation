#!/usr/bin/env python3
"""Owned-local causal safety checks. No resource deletion; all runs and preimages retained."""
import hashlib, json, os, pathlib, re, secrets, signal, subprocess, sys

container, owner, port, password_file, destination = sys.argv[1:]
root = pathlib.Path(destination); root.mkdir(exist_ok=False)
here = pathlib.Path(__file__).resolve().parent
binary = os.environ.get('CLOCK_SEAL_BINARY', str(here / 'bin/Release/net10.0/Kernel.CommitClock.Batch.dll'))
info = json.loads(subprocess.check_output(['docker','container','inspect',container], text=True))[0]
container = info['Id']
if not re.fullmatch('[0-9a-f]{64}',container) or not info['State']['Running']:
    raise RuntimeError('Running full owned container ID required')
if not owner or (info['Config']['Labels'] or {}).get('kernel4229.clock-seal-r2.owner') != owner:
    raise RuntimeError('Owned container label required')
bindings = info['NetworkSettings']['Ports'].get('5432/tcp') or []
if len(bindings)!=1 or bindings[0]['HostIp']!='127.0.0.1' or bindings[0]['HostPort']!=port:
    raise RuntimeError('Provided port must match the owned container loopback PostgreSQL mapping')
port = bindings[0]['HostPort']
(root/'endpoint.json').write_text(json.dumps(dict(container=container,image=info['Image'],host='127.0.0.1',port=port,owner=owner),indent=2)+'\n')
results = []; databases = []; runs = 0

def sql(database, statement):
    return subprocess.check_output(['docker','exec','-u','postgres',container,'psql','-XAt','-v','ON_ERROR_STOP=1','-d',database,'-c',statement], text=True).strip()
def fresh():
    name = 'ksb_' + secrets.token_hex(4)
    if sql('postgres', "SELECT count(*) FROM pg_database WHERE datname='"+name+"'") != '0':
        raise RuntimeError('Fresh database name collision; existing data retained')
    databases.append(name); return name
def exists(name):
    return sql('postgres', "SELECT count(*) FROM pg_database WHERE datname='"+name+"'") != '0'
def check(ok, case):
    results.append(dict(case=case, passed=bool(ok))); print(('PASS ' if ok else 'FAIL ')+case, flush=True)
def directory(case):
    path = root / case; path.mkdir(); return path
def invoke(name, out, flags=(), arguments=None):
    global runs
    runs += 1
    args = arguments if arguments is not None else [port,name,password_file,str(out),*flags]
    p = subprocess.run(['dotnet',binary,*args], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
    (root / ('invocation-%02d.log' % runs)).write_text(p.stdout)
    return p.returncode

def snapshot(name):
    tables = sql(name,"SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename").splitlines()
    return [(t,sql(name,'SELECT coalesce(jsonb_agg(x ORDER BY x::text)::text,\'[]\') FROM (SELECT to_jsonb(t) x FROM "'+t+'" t) s')) for t in tables]
def refused(name, out, flags=(), arguments=None):
    before = snapshot(name) if exists(name) else None
    code = invoke(name,out,flags,arguments)
    after = snapshot(name) if exists(name) else None
    return code != 0 and before == after

def interrupted(name, out, case):
    p=subprocess.Popen(['dotnet',binary,port,name,password_file,str(out)],text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT)
    lines=[]; checkpoint=False; signalled=False
    for line in p.stdout:
        lines.append(line)
        if line.rstrip('\r\n')=='PASS business 1 pending rows are invisible before commit':
            checkpoint=True; p.send_signal(signal.SIGTERM); signalled=True; break
    p.wait(timeout=30); lines.extend(p.stdout.readlines()); (root/(case+'.log')).write_text(''.join(lines))
    present=exists(name); evidence=out/'observations.json'
    status=json.loads(evidence.read_text())['status'] if evidence.is_file() else None
    incomplete=present and sql(name,"SELECT count(*) FROM fixture_run WHERE version='clock-seal-batch-r2' AND NOT completed AND database_oid=(SELECT oid FROM pg_database WHERE datname=current_database())")=='1'
    unchanged=refused(name,directory(case+'-additive'),('--early-clock-control',)) if incomplete or not present else False
    earned=checkpoint and signalled and p.returncode==-signal.SIGTERM and present and incomplete and status=='IN_PROGRESS' and unchanged
    legacy=p.returncode!=0 and unchanged
    (root/(case+'.json')).write_text(json.dumps(dict(database=name,checkpoint=checkpoint,signal_sent=signalled,exit=p.returncode,database_present=present,incomplete=incomplete,evidence_status=status,additive_refused_unchanged=unchanged,earned_interruption=earned,old_predicate=legacy),indent=2)+'\n')
    return earned,legacy

for case, selected_owner, selected_port, expected in [('owner',owner+'-wrong',port,'Owned container label required'),('port',owner,'0','Provided port must match')]:
    child=root/('rejected-endpoint-'+case)
    p=subprocess.run([sys.executable,'-O',str(here/'safety-checks.py'),container,selected_owner,selected_port,password_file,str(child)],text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=30)
    (root/('rejected-endpoint-'+case+'.log')).write_text(p.stdout)
    check(p.returncode!=0 and expected in p.stdout and not (child/'endpoint.json').exists(), 'optimized Python rejects wrong '+case+' before endpoint admission')

good = fresh(); full = directory('full')
check(invoke(good,full)==0, 'legitimate full fixture')
for case, flags in [('full-collision',()),('additive-collision',('--early-clock-control',))]:
    out=directory(case); target=out/'observations.json'; target.write_bytes((full/'observations.json').read_bytes())
    before=target.read_bytes(); (out/'retained-preimage.json').write_bytes(before)
    name=fresh() if not flags else good
    check(refused(name,out,flags) and target.read_bytes()==before, case+' preserves previous observations and database')
failed=directory('failed-refusal'); check(refused(good,failed), 'full retry refused with own failed evidence')
before=(failed/'observations.json').read_bytes(); (failed/'retained-preimage.json').write_bytes(before)
check(refused(good,failed) and (failed/'observations.json').read_bytes()==before, 'failed evidence collision preserved')
for case, out in [('missing-output',root/'missing'/'nested'),('unwritable-output',directory('readonly'))]:
    if case=='unwritable-output': out.chmod(0o500)
    name=fresh(); check(refused(name,out) and not exists(name),case+' refuses before database creation')
for case, tail in [('short',None),('typo',['--early-clock-contro']),('trailing',['--early-clock-control','extra']),('newline',[])]:
    name=fresh(); out=directory('invalid-'+case); supplied=name+'\n' if case=='newline' else name
    args=[port,supplied,password_file] if tail is None else [port,supplied,password_file,str(out),*tail]
    check(refused(name,out,arguments=args) and not exists(name) and not (out/'observations.json').exists(),'invalid '+case+' before reservation or database writes')
for case in ['empty','schema-only','seeded','wrong-version']:
    name=fresh(); sql('postgres','CREATE DATABASE '+name)
    if case!='empty':
        sql(name,(here/'schema.sql').read_text())
        version='wrong' if case=='wrong-version' else 'clock-seal-batch-r2'
        sql(name,"INSERT INTO fixture_run VALUES('"+version+"',(SELECT oid FROM pg_database WHERE datname=current_database()),"+('true' if case=='wrong-version' else 'false')+")")
        if case in ['seeded','wrong-version']: sql(name,'INSERT INTO partition(id,epoch) SELECT g,1 FROM generate_series(1,9) g')
    check(refused(name,directory('partial-'+case),('--early-clock-control',)),case+' additive refused without content writes')
check(interrupted(fresh(),directory('interrupted-full'),'interrupted-full')[0], 'actual signalled incomplete full cannot authorize additive writes')
earned,legacy=interrupted(fresh(),root/'absent-parent'/'out','startup-refusal')
check(not earned and legacy, 'exact-binary startup refusal cannot earn interruption credit; old predicate would pass')
check(invoke(good,directory('additive'),('--early-clock-control',))==0,'legitimate completed full continuation')
check(refused(good,directory('additive-retry'),('--early-clock-control',)),'used additive scope refused without content writes')
(root/'results.json').write_text(json.dumps(dict(binary=binary,binary_sha256=hashlib.sha256(pathlib.Path(binary).read_bytes()).hexdigest(),databases=databases,results=results),indent=2)+'\n')
sys.exit(0 if all(r['passed'] for r in results) else 1)
