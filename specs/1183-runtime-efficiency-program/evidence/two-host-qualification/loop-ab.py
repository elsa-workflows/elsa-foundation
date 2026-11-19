"""Two real Workbench nodes in one durable PostgreSQL cluster; node B sends duplicate actor Cancel to node A's execution."""
import base64, json, pathlib, subprocess, sys, time

T = pathlib.Path('/root/elsa-eff-i68'); OUT = T / sys.argv[1]; OUT.mkdir(); WORKBENCH = sys.argv[2]
HOSTS = {'A': '10.238.2.5', 'B': '10.238.2.6'}; IDS = {'A': 'eff-i68-node-a', 'B': 'eff-i68-node-b'}
RB = '/home/elsarunner/elsa-eff-i68'; PG = 'elsa-efficiency-i68-pg'; BASEDB = 'i68_' + sys.argv[1].replace('-', '_'); DB = BASEDB; PASSWORD = (T / 'pg-password').read_text().strip()
SSH = ['ssh', '-i', str(T / 'ssh-key'), '-o', 'StrictHostKeyChecking=accept-new', '-o', f'UserKnownHostsFile={T}/known_hosts',
       '-o', 'ControlMaster=auto', '-o', f'ControlPath={T}/cm-%h', '-o', 'ControlPersist=900', '-o', 'BatchMode=yes']
for _ip in HOSTS.values():
    subprocess.run(SSH + ['-fN', 'elsarunner@' + _ip], stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True, timeout=60)
ledger = []; pids = {}


def ssh(host, command, data=None, timeout=200):
    p = subprocess.run(SSH + ['elsarunner@' + HOSTS[host], command], input=data, capture_output=True, text=True, timeout=timeout)
    return p.returncode, p.stdout


def psql(sql, db=None):
    db = db or DB
    p = subprocess.run(['docker', 'exec', PG, 'psql', '-U', 'postgres', '-d', db, '-At', '-c', sql], capture_output=True, text=True, timeout=120)
    if p.returncode: raise RuntimeError('psql failed: ' + p.stderr[-300:])
    return p.stdout.strip()


def enc(value): return base64.b64encode(value.encode('utf-16-le')).decode()


def record(row):
    ledger.append(row); print(json.dumps(row), flush=True); (OUT / 'ledger.json').write_text(json.dumps(ledger, indent=1))


def env(host):
    shared = f'Host=10.238.2.4;Port=15432;Database={DB};Username=postgres;Password={PASSWORD}'
    lines = {'ASPNETCORE_ENVIRONMENT': 'Development', 'ConnectionStrings__Shared': shared,
             'Elsa__Cluster__Membership__HostId': IDS[host], 'Elsa__Cluster__Membership__HeartbeatInterval': '00:00:01',
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
    name = f'node-{OUT.name}-{host.lower()}-{len([k for k in pids if k[0] == host])}'
    extra = f"DOTNET_STARTUP_HOOKS={RB}/hook/ElsaEfficiencyClusterHook.dll ELSA_EFFICIENCY_CLUSTER_CONTROL_ROOT={control} " if control else ''
    script = f"cd {RB}/{WORKBENCH} && set -a && . {RB}/node-{OUT.name}.env && set +a && exec env {extra}./Elsa.Workbench --contentRoot {RB}/{WORKBENCH} --urls http://127.0.0.1:5095"
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



def dec(v):
    try: return base64.b64decode(v, validate=True).decode('utf-16-le')
    except Exception: return v
for index in range(int(sys.argv[3])):
    DB = f'{BASEDB}_{index}'
    psql(f'create database {DB}', 'postgres')
    diag = f"DOTNET_STARTUP_HOOKS='{RB}/diag/ElsaEfficiencyConflictHook.dll'\nELSA_EFFICIENCY_CONFLICT_LOG='{RB}/conflicts-{OUT.name}-{index}.jsonl'\n" if len(sys.argv) > 4 else ''
    assert ssh('A', f'umask 077; cat > {RB}/node-{OUT.name}.env', data=env('A') + diag)[0] == 0
    a = start('A')
    code, out = ssh('A', f'timeout 50 python3 {RB}/api.py loop', timeout=120)
    run = json.loads(out.strip().splitlines()[-1]) if out.strip() else {'executeReturned': False, 'exit': code}
    time.sleep(45)
    rows = psql('select "WorkflowExecutionId" from elsa_runtime_workflow_execution_state').splitlines()
    execution = dec(rows[0]) if len(rows) == 1 else None
    row = dict(index=index, workbench=WORKBENCH, database=DB, run=run, executions=len(rows), workflowExecutionId=execution)
    if execution:
        row['state'] = state(execution); first = activity_rows(execution); time.sleep(5); row['activityRows'] = [first, activity_rows(execution)]
        row['incidentSummaries'] = [dec(json.loads(i).get('message', ''))[:400] for i in psql('select "ContentJson" from elsa_runtime_incident_state').splitlines() if i]
    if diag: row['conflicts'] = [json.loads(l) for l in (ssh('A', f'cat {RB}/conflicts-{OUT.name}-{index}.jsonl 2>/dev/null')[1]).splitlines() if l][:5]
    record(row)
    stop('A', a)
(OUT / 'finished').write_text('done')
