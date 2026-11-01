"""Two real Workbench nodes in one durable PostgreSQL cluster; node B sends duplicate actor Cancel to node A's execution."""
import base64, json, pathlib, subprocess, sys, time

T = pathlib.Path('/root/elsa-eff-i68'); OUT = T / sys.argv[1]; OUT.mkdir()
HOSTS = {'A': '10.238.2.5', 'B': '10.238.2.6'}; IDS = {'A': 'eff-i68-node-a', 'B': 'eff-i68-node-b'}
RB = '/home/elsarunner/elsa-eff-i68'; PG = 'elsa-efficiency-i68-pg'; DB = 'i68_' + sys.argv[1].replace('-', '_'); WORKBENCH = sys.argv[2] if len(sys.argv) > 2 else 'workbench'; PASSWORD = (T / 'pg-password').read_text().strip()
SSH = ['ssh', '-i', str(T / 'ssh-key'), '-o', 'StrictHostKeyChecking=accept-new', '-o', f'UserKnownHostsFile={T}/known_hosts',
       '-o', 'ControlMaster=auto', '-o', f'ControlPath={T}/cm-%h', '-o', 'ControlPersist=900', '-o', 'BatchMode=yes']
for _ip in HOSTS.values():
    subprocess.run(SSH + ['-fN', 'elsarunner@' + _ip], stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True, timeout=60)
ledger = []; pids = {}


def ssh(host, command, data=None, timeout=200):
    p = subprocess.run(SSH + ['elsarunner@' + HOSTS[host], command], input=data, capture_output=True, text=True, timeout=timeout)
    return p.returncode, p.stdout


def psql(sql, db=DB):
    p = subprocess.run(['docker', 'exec', PG, 'psql', '-U', 'postgres', '-d', db, '-At', '-c', sql], capture_output=True, text=True, timeout=120)
    if p.returncode: raise RuntimeError('psql failed: ' + p.stderr[-300:])
    return p.stdout.strip()


def enc(value): return base64.b64encode(value.encode('utf-16-le')).decode()


def record(row):
    ledger.append(row); print(json.dumps(row), flush=True); (OUT / 'ledger.json').write_text(json.dumps(ledger, indent=1))


def env(host, host_id):
    shared = f'Host=10.238.2.4;Port=15432;Database={DB};Username=postgres;Password={PASSWORD}'
    lines = {'ASPNETCORE_ENVIRONMENT': 'Development', 'ConnectionStrings__Shared': shared,
             'Elsa__Cluster__Membership__HostId': host_id, 'Elsa__Cluster__Membership__HeartbeatInterval': '00:00:01',
             'Elsa__Cluster__Membership__ExpiryPeriod': '00:00:05', 'Elsa__Cluster__Membership__SkewAllowance': '00:00:01',
             'Elsa__Cluster__Membership__EntityFrameworkCore__Enabled': 'true', 'Elsa__Cluster__Membership__EntityFrameworkCore__Provider': 'PostgreSql',
             'Elsa__Cluster__Membership__EntityFrameworkCore__ConnectionName': 'Shared', 'Elsa__Cluster__Membership__EntityFrameworkCore__CleanupPeriod': '00:05:00',
             'Elsa__DataProtection__ApplicationName': 'Elsa-eff-i68', 'Elsa__DataProtection__EntityFrameworkCore__Enabled': 'true',
             'Elsa__DataProtection__EntityFrameworkCore__Provider': 'PostgreSql', 'Elsa__DataProtection__EntityFrameworkCore__ConnectionName': 'Shared',
             'CShells__Shells__default__Features__FileSystemDistributedLocking': 'false',
             'CShells__Shells__default__Features__WorkflowsRuntimeDistributed__SweepIntervalSeconds': '2'}
    for feature in ['DatabaseDistributedLocking', 'WorkflowsRuntimeEntityFrameworkCore', 'WorkflowsDesignEntityFrameworkCore', 'ActivitiesDesignEntityFrameworkCore',
                    'WorkflowsPublishingEntityFrameworkCore', 'WorkflowsRuntimeDistributedEntityFrameworkCorePersistence',
                    'WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence']:
        lines[f'CShells__Shells__default__Features__{feature}__Provider'] = 'PostgreSql'
        lines[f'CShells__Shells__default__Features__{feature}__ConnectionName'] = 'Shared'
    return ''.join(f"{k}='{v}'\n" for k, v in lines.items())


def start(host, control=None):
    count = len([k for k in pids if k[0] == host]); name = f'node-{host.lower()}-{count}'
    # A restarted sender gets its own host id: the durable membership guard refuses a second live process under one id.
    host_id = IDS[host] if host == 'A' else f'{IDS[host]}{count}'
    assert ssh(host, f'umask 077; cat > {RB}/node.env', data=env(host, host_id))[0] == 0
    extra = f"DOTNET_STARTUP_HOOKS={RB}/hook/ElsaEfficiencyClusterHook.dll ELSA_EFFICIENCY_CLUSTER_CONTROL_ROOT={control} " if control else ''
    script = f"cd {RB}/{WORKBENCH} && set -a && . {RB}/node.env && set +a && exec env {extra}./Elsa.Workbench --contentRoot {RB}/{WORKBENCH} --urls http://127.0.0.1:5095"
    code, out = ssh(host, f"setsid sh -c '{script}' < /dev/null > {RB}/{name}.log 2>&1 & echo $!")
    pids[(host, name)] = int(out.strip())
    deadline = time.monotonic() + 240
    while time.monotonic() < deadline:
        code, out = ssh(host, 'curl -s -o /dev/null -w %{http_code} http://127.0.0.1:5095/health/ready')
        if out.strip() == '200': return name
        alive = ssh(host, f'kill -0 {pids[(host, name)]} && echo alive')[1]
        if 'alive' not in alive: raise RuntimeError(f'{name} exited during startup')
        time.sleep(2)
    raise RuntimeError(f'{name} not ready')


def stop(host, name):
    pid = pids[(host, name)]; ssh(host, f'kill -TERM -- -{pid}; for i in $(seq 30); do kill -0 {pid} 2>/dev/null || exit 0; sleep 1; done; kill -9 -- -{pid}')


def file(host, path):
    code, out = ssh(host, f'cat {path} 2>/dev/null'); return out if code == 0 and out else None


def state(execution):
    row = psql(f"""select "Status" from elsa_runtime_workflow_execution_state where "WorkflowExecutionId" in ('{execution}','{enc(execution)}')""")
    return int(row) if row else None


def placement(execution):
    return psql(f"""select coalesce(json_agg(to_jsonb(p)),'[]') from elsa_distributed_execution_placement p where "WorkflowExecutionId" in ('{execution}','{enc(execution)}')""")


def activity_rows(execution):
    return int(psql(f"""select count(*) from elsa_runtime_activity_execution_state where "WorkflowExecutionId" in ('{execution}','{enc(execution)}')"""))


def case(shape, index):
    control = f'{RB}/control-{shape}'
    assert ssh('B', f'umask 077; mkdir {control}')[0] == 0
    b = start('B', control)
    ready = json.loads(file('B', f'{control}/ready.json') or 'null') if wait_for('B', f'{control}/ready.json', 60) else None
    code, out = ssh('A', f'python3 {RB}/api.py {shape}'); run = json.loads(out.strip().splitlines()[-1])
    execution = run['workflowExecutionId']; row = dict(shape=shape, run=run, senderReady=ready is not None)
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline and state(execution) != 1: time.sleep(.5)
    row['stateBeforeCancel'] = state(execution); row['placementBefore'] = json.loads(placement(execution))
    if shape == 'loop':
        first = activity_rows(execution); time.sleep(5); row['activityRowsProgressingBefore'] = [first, activity_rows(execution)]
    request = json.dumps({'WorkflowExecutionId': execution, 'ArtifactId': run['artifactId'], 'OwnerHostId': IDS['A'], 'CommandKey': f'eff-i68-cancel-{shape}-{index}'})
    assert ssh('B', f'cat > {control}/cancel.json.tmp && mv {control}/cancel.json.tmp {control}/cancel.json', data=request)[0] == 0
    cancel_at = time.time()
    finished = wait_for('B', f'{control}/finished.json', 270)
    row['senderFinished'] = json.loads(file('B', f'{control}/finished.json') or '{}')
    row['dispatch'] = json.loads(file('B', f'{control}/dispatch.json') or '{}')
    row['secondsToSettledReceipt'] = round(time.time() - cancel_at, 1)
    row['stateAfter'] = state(execution); row['placementAfter'] = json.loads(placement(execution))
    row['ownerApiView'] = json.loads(ssh('A', f'python3 {RB}/api.py status {execution}')[1].strip().splitlines()[-1])
    first = activity_rows(execution); time.sleep(10); row['activityRowsAfterCancel'] = [first, activity_rows(execution)]
    row['transportRows'] = int(psql(f"""select count(*) from elsa_distributed_command_transport where "WorkflowExecutionId" in ('{execution}','{enc(execution)}')"""))
    row['incidents'] = int(psql(f"""select count(*) from elsa_runtime_incident_state where "WorkflowExecutionId" in ('{execution}','{enc(execution)}')"""))
    statuses = [r.get('Status') for r in row['dispatch'].get('Receipts', row['dispatch'].get('receipts', []))] if isinstance(row['dispatch'], dict) else []
    row['passed'] = (finished and row['senderFinished'].get('status') == 'passed' and row['stateBeforeCancel'] == 1 and row['stateAfter'] == 5
                     and row['ownerApiView'].get('status') == 'Cancelled' and row['incidents'] == 0
                     and any(p.get('OwnerId') == IDS['A'] for p in row['placementBefore'])
                     and row['activityRowsAfterCancel'][0] == row['activityRowsAfterCancel'][1]
                     and (shape != 'loop' or row['activityRowsProgressingBefore'][1] > row['activityRowsProgressingBefore'][0]))
    row['receiptStatuses'] = statuses
    stop('B', b)
    return row


def wait_for(host, path, seconds):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if ssh(host, f'test -f {path} && echo yes')[1].strip() == 'yes': return True
        time.sleep(1)
    return False


psql(f'create database {DB}', 'postgres')
a = start('A')
record(dict(step='node-a-ready', members=json.loads(psql('select coalesce(json_agg(json_build_object(\'HostId\',"HostId",\'Status\',"Status")),\'[]\') from elsa_cluster_members'))))
for index, shape in enumerate(['delay', 'loop']):
    try: row = case(shape, index)
    except Exception as e: row = dict(shape=shape, passed=False, error=type(e).__name__ + ': ' + str(e)[:300])
    row['members'] = json.loads(psql('select coalesce(json_agg(json_build_object(\'HostId\',"HostId",\'Status\',"Status")),\'[]\') from elsa_cluster_members'))
    record(row)
stop('A', a)
cases = [r for r in ledger if 'shape' in r]
(OUT / 'finished').write_text('passed' if cases and all(r['passed'] for r in cases) else 'failed')
print(json.dumps(dict(cases=len(cases), passed=sum(r['passed'] for r in cases))), flush=True)
